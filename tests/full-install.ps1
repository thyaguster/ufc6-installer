# Run after packing completes. Keeps a private full installation for gameplay QA.
[CmdletBinding()]
param([string]$ValidationRoot = 'R:\UFC6-installer-validation')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $root '.build\full-test-state.json'
$state = [ordered]@{ Status='WaitingForArchive'; ProcessId=$PID; StartedUtc=[DateTime]::UtcNow.ToString('o'); FinishedUtc=$null; Installation=$null; VerifiedFiles=0; XboxIcons='Pending'; Gameplay='Pending interactive launch and in-game check'; Error=$null }
function Save-State { $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8 }
Save-State
try {
    while ($true) {
        $pack = Get-Content -LiteralPath (Join-Path $root '.build\pack-state.json') -Raw | ConvertFrom-Json
        if ($pack.Status -eq 'Failed') { throw "Game packing failed: $($pack.Error)" }
        if ($pack.Status -eq 'Complete') { break }
        if (!(Get-Process -Id $pack.ProcessId -ErrorAction SilentlyContinue)) { throw 'The packing process stopped without completing its archive.' }
        Start-Sleep -Seconds 15
    }
    $state.Status = 'BuildingInstaller'; Save-State
    & (Join-Path $root 'build.ps1') -Mode Setup -OutputDirectory $pack.OutputDirectory -PackageManifest $pack.Manifest
    $exe = Join-Path $pack.OutputDirectory 'setup.exe'
    $session = Join-Path $ValidationRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Path $session | Out-Null
    $destination = Join-Path $session 'EA SPORTS UFC 6'
    $state.Installation = $destination; $state.Status = 'Installing'; Save-State
    $env:UFC6_INSTALLER_LOG_DIR = Join-Path $root '.build\full-install-logs'
    $process = Start-Process -FilePath $exe -ArgumentList @('--extract', ('"' + $destination + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Full installer failed (exit $($process.ExitCode)); see full-install-logs." }
    $state.Status = 'VerifyingFiles'; Save-State
    $source = '\\seedbox\d\JOGOS II\ufc6 repack\EA SPORTS UFC 6\PPSA23566-EASPORTSUFC6GAME'
    $expected = @{}
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -Force -File) { $expected[$file.FullName.Substring($source.Length + 1)] = $file }
    $reference = Join-Path $session 'alter-egos-reference'
    & 'C:\Program Files\7-Zip\7z.exe' x (Join-Path $root 'PPSA23566.rar') "-o$reference" -y -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract Alter Egos comparison files.' }
    $addonRoot = Join-Path $reference 'PPSA23566'
    foreach ($file in Get-ChildItem -LiteralPath $addonRoot -Recurse -File) { $expected[$file.FullName.Substring($addonRoot.Length + 1)] = $file }
    $actual = @(Get-ChildItem -LiteralPath $destination -Recurse -Force -File)
    if ($actual.Count -ne $expected.Count) { throw "Installed file count mismatch: $($actual.Count) vs $($expected.Count)." }
    foreach ($entry in $expected.GetEnumerator()) {
        $installed = Join-Path $destination $entry.Key
        if ((Get-FileHash -LiteralPath $entry.Value.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash) { throw "Full installation hash mismatch: $($entry.Key)" }
        $state.VerifiedFiles++; if ($state.VerifiedFiles % 5 -eq 0) { Save-State }
    }
    $state.Status = 'TestingXboxIcons'; Save-State
    $patcher = Join-Path $session 'UFC6-Xbox-Icons.exe'
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
    $input = $assembly.GetManifestResourceStream('xbox-icons.exe')
    try { $output = [IO.File]::Create($patcher); try { $input.CopyTo($output) } finally { $output.Dispose() } } finally { $input.Dispose() }
    foreach ($action in @('install','status','restore')) {
        $report = Join-Path $session ("xbox-$action.json")
        $process = Start-Process -FilePath $patcher -ArgumentList @("--$action", '--game-dir', ('"' + $destination + '"'), '--report', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or !$result.success) { throw "Xbox $action failed: $($result.messages -join ' ')" }
        if ($action -eq 'status' -and ($result.messages -join ' ') -notmatch '430/430 ranges installed') { throw 'The full-game Xbox patch status did not verify all 430 ranges.' }
    }
    $state.XboxIcons = 'Install, all 430 ranges, and restoration passed on full game'
    $state.Status = 'InstallationVerified'; $state.FinishedUtc = [DateTime]::UtcNow.ToString('o'); Save-State
    Write-Host "Full installation verified: $destination"
    Write-Host 'Gameplay remains pending an interactive PORTHEON launch and in-game check.'
} catch {
    $state.Status='Failed'; $state.Error=$_.Exception.Message; $state.FinishedUtc=[DateTime]::UtcNow.ToString('o'); Save-State
    Write-Error -Message $_.Exception.Message -ErrorAction Continue; exit 1
}
