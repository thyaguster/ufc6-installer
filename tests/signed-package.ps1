# Windows PowerShell 5.1: CLI integration plus in-process package-swap protection.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('.build\signed-security-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$env:UFC6_INSTALLER_LOG_DIR = Join-Path $testRoot 'logs'
$generic = Join-Path $testRoot 'generic'
& (Join-Path $root 'build.ps1') -Mode Setup -OutputDirectory $generic -PackageManifest (Join-Path $testRoot 'does-not-exist.json')
$genericExe = Join-Path $generic 'setup.exe'
$genericHash = (Get-FileHash $genericExe).Hash
function Run-Setup([string]$Exe, [string[]]$Arguments) {
    $quoted = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    return (Start-Process -FilePath $Exe -ArgumentList $quoted -WindowStyle Hidden -Wait -PassThru).ExitCode
}
$packages = @()
foreach ($version in @('01.006.000','01.007.000')) {
    $directory = Join-Path $testRoot $version
    $source = Join-Path $directory 'source'; $output = Join-Path $directory 'dist'
    New-Item -ItemType Directory -Path (Join-Path $source 'sce_sys') -Force | Out-Null
    Set-Content (Join-Path $source 'sce_sys\param.json') ('{"contentVersion":"' + $version + '"}')
    Set-Content (Join-Path $source 'game.txt') ('Game contents ' + $version)
    $manifest = Join-Path $directory 'package.json'
    & (Join-Path $root 'build.ps1') -Mode Pack -Source $source -OutputDirectory $output -PackageManifest $manifest -AlterEgosArchive ''
    Copy-Item $genericExe (Join-Path $output 'setup.exe')
    $exe = Join-Path $output 'setup.exe'; $archive = Join-Path $output 'data.bin'
    $inspection = Join-Path $directory 'inspection.json'
    if ((Run-Setup $exe @('--inspect-package',$inspection)) -ne 0) { throw 'Generic setup rejected a valid signed package.' }
    $metadata = Get-Content $inspection -Raw | ConvertFrom-Json
    if ($metadata.GameVersion -ne $version) { throw 'Version was not loaded from the signed package.' }
    $destination = Join-Path $directory 'installed'
    if ((Run-Setup $exe @('--extract',$destination,'--without-alter-egos')) -ne 0) { throw 'Valid signed package failed extraction.' }
    foreach ($file in Get-ChildItem $source -Recurse -File) {
        $relative = $file.FullName.Substring($source.Length + 1)
        if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash (Join-Path $destination $relative)).Hash) { throw 'Signed extraction changed a file.' }
    }
    if ((Get-FileHash $exe).Hash -ne $genericHash) { throw 'Package required a different installer binary.' }
    $packages += [pscustomobject]@{Directory=$directory;Output=$output;Exe=$exe;Archive=$archive;Manifest=$manifest;Metadata=$metadata}
}
if ($packages[0].Metadata.Sha256 -eq $packages[1].Metadata.Sha256) { throw 'Expected different payload hashes.' }
Write-Host 'PASS: one identical generic EXE installs two different signed game versions and loads their metadata offline.'
$target = $packages[1]; $original = [IO.File]::ReadAllBytes($target.Archive)
$mutations = @('signature','metadata','payload','truncated-header','truncated-payload','trailing-data','version','length','padding')
try {
    foreach ($mutation in $mutations) {
        $bytes = [byte[]]$original.Clone()
        switch ($mutation) {
            'signature' { $index=32+[BitConverter]::ToInt32($bytes,12); $bytes[$index]=$bytes[$index] -bxor 1 }
            'metadata' {
                $text=[Text.Encoding]::UTF8.GetString($bytes,32,[BitConverter]::ToInt32($bytes,12))
                $index=32+$text.IndexOf('01.007.000')+5
                $bytes[$index]=$bytes[$index] -bxor 1
            }
            'payload' { $bytes[65568]=$bytes[65568] -bxor 1 }
            'truncated-header' { $bytes=[byte[]]$bytes[0..1023] }
            'truncated-payload' { $bytes=[byte[]]$bytes[0..($bytes.Length-2)] }
            'trailing-data' { $bytes=[byte[]]($bytes+@(0)) }
            'version' { $bytes[8]=4 }
            'length' { [BitConverter]::GetBytes([int]100000).CopyTo($bytes,12) }
            'padding' { $bytes[60000]=1 }
        }
        [IO.File]::WriteAllBytes($target.Archive,$bytes)
        $destination=Join-Path $target.Directory ('rejected-'+$mutation)
        if ((Run-Setup $target.Exe @('--extract',$destination,'--without-alter-egos')) -eq 0) { throw "Accepted $mutation corruption." }
        if (Test-Path $destination) { throw "Rejected $mutation left installation files." }
    }
} finally { [IO.File]::WriteAllBytes($target.Archive,$original) }
Write-Host 'PASS: altered metadata/signatures/payloads, truncation, trailing data, unsupported format, invalid lengths and padding are rejected before extraction.'
# Build a genuinely valid package signed by a different key; the shipped EXE must reject it.
$tool=(Get-Content (Join-Path $root '.build\package-tool-path.txt') -Raw).Trim()
$otherPrivate=Join-Path $testRoot 'untrusted-key.dpapi'; $otherPublic=Join-Path $testRoot 'untrusted-public.xml'
& $tool init $otherPrivate $otherPublic
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare foreign-key test.' }
$raw=Join-Path $testRoot 'raw.7z'
$stream=[IO.File]::OpenRead($target.Archive)
try { $stream.Position=65536; $out=[IO.File]::Create($raw); try { $stream.CopyTo($out) } finally { $out.Dispose() } } finally { $stream.Dispose() }
$otherManifest=Join-Path $testRoot 'foreign-metadata.json'; Copy-Item $target.Manifest $otherManifest
$foreign=Join-Path $testRoot 'foreign.bin'
& $tool seal $raw $foreign $otherManifest $otherPrivate $otherPublic
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare foreign-key package.' }
try {
    Copy-Item $foreign $target.Archive -Force
    if ((Run-Setup $target.Exe @('--inspect-package',(Join-Path $testRoot 'foreign.json'))) -eq 0) { throw 'An untrusted signing key was accepted.' }
} finally { [IO.File]::WriteAllBytes($target.Archive,$original) }
Write-Host 'PASS: a package signed by another key is rejected.'
# Re-sign only metadata, retaining the payload and the exact same EXE.
$revised=Join-Path $testRoot 'revised'; $revisedManifest=Join-Path $testRoot 'revised.json'
$metadata=Get-Content $target.Manifest -Raw | ConvertFrom-Json; $metadata.GameVersion='01.008.000'
$metadata | ConvertTo-Json | Set-Content $revisedManifest -Encoding UTF8
& (Join-Path $root 'build.ps1') -Mode Seal -BaseArchive $target.Archive -OutputDirectory $revised -PackageManifest $revisedManifest
Copy-Item $genericExe (Join-Path $revised 'setup.exe') -Force
$inspection=Join-Path $testRoot 'revised-inspection.json'
if ((Run-Setup (Join-Path $revised 'setup.exe') @('--inspect-package',$inspection)) -ne 0) { throw 'Generic EXE rejected correctly re-signed metadata.' }
$newMetadata=Get-Content $inspection -Raw | ConvertFrom-Json
if ($newMetadata.GameVersion -ne '01.008.000' -or $newMetadata.Sha256 -ne $target.Metadata.Sha256) { throw 'Metadata-only revision changed or lost the payload.' }
$Installer=$target.Exe
. (Join-Path $PSScriptRoot 'ui-load.ps1')
try {
    # Same file length and payload, different trusted manifest: old UI choices must not be reused.
    Copy-Item (Join-Path $revised 'data.bin') $target.Archive -Force
    $rejectedDestination=Join-Path $testRoot 'swapped-package-install'
    $progress=[Action[int,string]]{param($percent,$message)}
    try {
        $program.GetMethod('Install',$staticFlags).Invoke($null,@([string]$rejectedDestination,[Threading.CancellationToken]::None,[Action[int,string]]$progress,$false,$false))
        throw 'A package swap after startup was accepted.'
    } catch {
        $reason=$_.Exception
        while ($reason.InnerException) { $reason=$reason.InnerException }
        if ($reason.Message -notmatch 'changed after setup opened') { throw }
    }
    if (Test-Path $rejectedDestination) { throw 'Package swap left installation files.' }
} finally { [IO.File]::WriteAllBytes($target.Archive,$original) }
if ((Get-FileHash $target.Exe).Hash -ne $genericHash) { throw 'Generic installer changed during package updates.' }
Write-Host 'PASS: signed metadata-only updates need no EXE rebuild; replacing data.bin after startup is detected.'
Write-Host "Signed-package test artifacts: $testRoot"
