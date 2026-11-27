$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('.build\smoke-' + [Guid]::NewGuid().ToString('N'))
$env:UFC6_INSTALLER_LOG_DIR = Join-Path $testRoot 'logs'
$source = Join-Path $testRoot 'source'
$output = Join-Path $testRoot 'distribution'
$destination = Join-Path $testRoot 'installed game'
New-Item -ItemType Directory -Path "$source\nested folder", "$source\empty folder" -Force | Out-Null
Set-Content -LiteralPath "$source\example.txt" -Value 'Round-trip test fixture.'
Set-Content -LiteralPath "$source\eboot.bin" -Value 'Base game executable fixture.'
Set-Content -LiteralPath "$source\nested folder\ação 日本語.txt" -Value 'Unicode names and spaces.'
Set-Content -LiteralPath "$source\.hidden" -Value 'Hidden file'
(Get-Item -LiteralPath "$source\.hidden" -Force).Attributes = 'Hidden'
$manifest = Join-Path $testRoot 'package.json'
& (Join-Path $root 'build.ps1') -Source $source -OutputDirectory $output -PackageManifest $manifest
$exe = Join-Path $output 'setup.exe'
$archive = Join-Path $output 'data.bin'
function Invoke-TestInstaller([string]$Target, [switch]$WithoutAlterEgos, [string]$Mode = 'Balanced') {
    $arguments = @('--extract', ('"' + $Target + '"'))
    if ($WithoutAlterEgos) { $arguments += '--without-alter-egos' }
    $arguments += "--mode=$Mode"
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
if ((Invoke-TestInstaller $destination -WithoutAlterEgos) -ne 0) { throw 'Round-trip extraction failed.' }
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
    $relative = $file.FullName.Substring($source.Length + 1)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination $relative)).Hash) { throw "File mismatch: $relative" }
}
if (!(Test-Path -LiteralPath "$destination\empty folder" -PathType Container)) { throw 'Empty folder was lost.' }
if (Test-Path -LiteralPath "$destination\fakelib") { throw 'Unselected Alter Egos files were installed.' }
if (Test-Path -LiteralPath "$destination\__ufc6_installer__") { throw 'Package payloads leaked into a base-only installation.' }
if (Test-Path -LiteralPath "$destination\Mods\Xbox Icons") { throw 'An unselected icon mod was installed with the game.' }
foreach ($mode in @('Fast','Quiet')) {
    $modeTarget = Join-Path $testRoot ("mode-$mode")
    if ((Invoke-TestInstaller $modeTarget -WithoutAlterEgos -Mode $mode) -ne 0) { throw "Extraction failed in $mode mode." }
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        $relative = $file.FullName.Substring($source.Length + 1)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $modeTarget $relative)).Hash) { throw "File mismatch in $mode mode: $relative" }
    }
}
Write-Host 'PASS: Fast, Balanced and Quiet produce identical game files.'
if ((Invoke-TestInstaller $destination) -eq 0) { throw 'Existing destination was accepted.' }
$original = [IO.File]::ReadAllBytes($archive)
try {
    $corrupt = [byte[]]$original.Clone(); $corrupt[65568] = $corrupt[65568] -bxor 255
    [IO.File]::WriteAllBytes($archive, $corrupt)
    $corruptTarget = Join-Path $testRoot 'corrupt destination'
    if ((Invoke-TestInstaller $corruptTarget) -eq 0) { throw 'Corrupted archive was accepted.' }
    if (Test-Path -LiteralPath $corruptTarget) { throw 'Corruption left a destination folder.' }
} finally { [IO.File]::WriteAllBytes($archive, $original) }
$before = (Get-FileHash -LiteralPath $archive).Hash
& (Join-Path $root 'build.ps1') -Mode Setup -OutputDirectory $output -PackageManifest $manifest
if ((Get-FileHash -LiteralPath $archive).Hash -ne $before) { throw 'Setup-only build changed the archive.' }
$afterRebuild = Join-Path $testRoot 'installed after rebuild'
if ((Invoke-TestInstaller $afterRebuild) -ne 0) { throw 'Rebuilt setup failed extraction.' }
$reference = Join-Path $testRoot 'addon-reference'
& 'C:\Program Files\7-Zip\7z.exe' x (Join-Path $root 'PPSA23566.rar') "-o$reference" -y -bso0 -bsp0
if ($LASTEXITCODE -ne 0) { throw 'Cannot extract Alter Egos reference.' }
$addonRoot = Join-Path $reference 'PPSA23566'
$packagedManifest = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$installedFiles = @(Get-ChildItem -LiteralPath $afterRebuild -Recurse -File -Force)
if ($packagedManifest.FileCount + $packagedManifest.AlterEgosFileDelta -ne $installedFiles.Count) { throw 'Installed file count in manifest is incorrect.' }
$installedBytes = ($installedFiles | Measure-Object Length -Sum).Sum
if ($packagedManifest.UnpackedBytes + $packagedManifest.AlterEgosInstalledDeltaBytes -ne $installedBytes) { throw 'Installed size in manifest is incorrect.' }
foreach ($file in Get-ChildItem -LiteralPath $addonRoot -Recurse -File) {
    $relative = $file.FullName.Substring($addonRoot.Length + 1)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $afterRebuild $relative)).Hash) { throw "Alter Egos mismatch: $relative" }
}
if (Test-Path -LiteralPath "$afterRebuild\PPSA23566") { throw 'Alter Egos was installed into a nested game folder.' }
if (Test-Path -LiteralPath "$afterRebuild\__ufc6_installer__") { throw 'Package payloads leaked into the game installation.' }
if (@(Get-ChildItem -LiteralPath $afterRebuild -Force -Directory | Where-Object Name -Like '.alter-egos-*').Count) { throw 'Alter Egos temporary files were left behind.' }
$savedArchive = Join-Path $output 'saved-data.bin'
Move-Item -LiteralPath $archive -Destination $savedArchive
try {
    if ((Invoke-TestInstaller (Join-Path $testRoot 'missing archive')) -eq 0) { throw 'Missing archive was accepted.' }
} finally { Move-Item -LiteralPath $savedArchive -Destination $archive }
$errorLog = Join-Path $output 'install-error.txt'
if (Test-Path -LiteralPath $errorLog) { Remove-Item -LiteralPath $errorLog }
if (@(Get-ChildItem -LiteralPath $output -File).Count -ne 2) { throw 'Distribution does not contain exactly two files.' }
$logs = (Get-ChildItem -LiteralPath $env:UFC6_INSTALLER_LOG_DIR -Filter '*.log' | Get-Content) -join "`n"
foreach ($required in @('Options: AlterEgos=', 'ButtonIcons=PlayStation', 'SUCCESS: game installation finalized', 'ERROR', 'failed its integrity check')) {
    if (!$logs.Contains($required)) { throw "Missing diagnostic log entry: $required" }
}
$version = (Get-Item -LiteralPath $exe).VersionInfo
if ($version.ProductName -ne 'EA SPORTS UFC 6 Installer' -or $version.FileVersion -ne '1.0.0.0') { throw 'Executable metadata is missing.' }
Write-Host 'PASS: persistent diagnostic logs contain selected options, success stages and failures; executable product/version metadata verified.'
Write-Host 'PASS: file hashes, Unicode paths, hidden files, empty folders, existing destination refusal, corruption and missing archive detection, independent setup rebuild, default Alter Egos overlay, and opt-out preserving the base executable.'
Write-Host "Test artifacts: $testRoot"
