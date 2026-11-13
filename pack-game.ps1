# Background worker for data.bin, including its optional Alter Egos payload. No installer build.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$statePath = Join-Path $PSScriptRoot '.build\pack-state.json'
$started = [DateTime]::UtcNow.ToString('o')
$state = [ordered]@{
    Status = 'Running'; StartedUtc = $started; FinishedUtc = $null
    ProcessId = $PID
    Source = 'T:\JOGOS II\ufc6 repack\EA SPORTS UFC 6\PPSA23566-EASPORTSUFC6GAME'
    OutputDirectory = 'E:\UFC6-installer'
    Archive = 'E:\UFC6-installer\data.bin'
    Manifest = (Join-Path $PSScriptRoot '.build\package.json')
    Error = $null
}
function Save-State { $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8 }
Save-State
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Mode Pack -Source $state.Source -OutputDirectory $state.OutputDirectory -PackageManifest $state.Manifest -Compression Normal
    $state.Status = 'Complete'
} catch {
    $state.Status = 'Failed'; $state.Error = $_.Exception.Message
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
} finally {
    $state.FinishedUtc = [DateTime]::UtcNow.ToString('o'); Save-State
}
if ($state.Status -eq 'Failed') { exit 1 }
