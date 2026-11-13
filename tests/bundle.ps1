$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('.build\bundle-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'source'
$base = Join-Path $testRoot 'base'
$bundled = Join-Path $testRoot 'bundled'
$manifest = Join-Path $testRoot 'package.json'
New-Item -ItemType Directory -Path $source -Force | Out-Null
Set-Content -LiteralPath (Join-Path $source 'eboot.bin') -Value 'Unmodified base executable'
Set-Content -LiteralPath (Join-Path $source 'base-only.txt') -Value 'Preserve this file'
& (Join-Path $root 'build.ps1') -Mode Pack -Source $source -OutputDirectory $base -PackageManifest $manifest -AlterEgosArchive ''
$baseArchive = Join-Path $base 'data.bin'
# Recreate the unsigned legacy format to exercise migration without rebuilding game data.
$signedBase = [IO.File]::OpenRead($baseArchive)
$rawArchive = Join-Path $base 'legacy.7z'
try {
    $signedBase.Position = 65536; $rawOutput = [IO.File]::Create($rawArchive)
    try { $signedBase.CopyTo($rawOutput) } finally { $rawOutput.Dispose() }
} finally { $signedBase.Dispose() }
$baseArchive = $rawArchive
$baseMetadata = Get-Content $manifest -Raw | ConvertFrom-Json
$baseMetadata.ArchiveBytes = $baseMetadata.PayloadBytes
$baseMetadata.PSObject.Properties.Remove('PackageFormat')
$baseMetadata | ConvertTo-Json | Set-Content $manifest -Encoding UTF8
$before = (Get-FileHash -LiteralPath $baseArchive).Hash
& (Join-Path $root 'build.ps1') -Mode Bundle -BaseArchive $baseArchive -OutputDirectory $bundled -PackageManifest $manifest
if ((Get-FileHash -LiteralPath $baseArchive).Hash -ne $before) { throw 'Bundling changed the original archive.' }
$package = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($package.PayloadVersion -ne 2 -or $package.AlterEgosArchivePath -ne '__ufc6_installer__\alter-egos.rar') { throw 'Payload metadata is invalid.' }
$bundledHash = (Get-FileHash -LiteralPath (Join-Path $bundled 'data.bin')).Hash
if ($package.PackageFormat -ne 3) { throw 'Bundled package is not signed.' }
$env:UFC6_INSTALLER_LOG_DIR = Join-Path $testRoot 'logs'
foreach ($addon in @($false, $true)) {
    $destination = Join-Path $testRoot ("install-addon-$addon")
    $arguments = @('--extract', ('"' + $destination + '"'))
    if (!$addon) { $arguments += '--without-alter-egos' }
    $process = Start-Process -FilePath (Join-Path $bundled 'setup.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Bundled installation failed (addon=$addon)." }
    if ((Get-FileHash (Join-Path $destination 'base-only.txt')).Hash -ne (Get-FileHash (Join-Path $source 'base-only.txt')).Hash) { throw 'Base game file changed.' }
    if (Test-Path (Join-Path $destination '__ufc6_installer__')) { throw 'Payload directory leaked into installed game.' }
    $sameExecutable = (Get-FileHash (Join-Path $destination 'eboot.bin')).Hash -eq (Get-FileHash (Join-Path $source 'eboot.bin')).Hash
    if ($sameExecutable -eq $addon) { throw 'Alter Egos selection was not honored.' }
}
& (Join-Path $root 'build.ps1') -Mode Setup -OutputDirectory $bundled -PackageManifest $manifest -AlterEgosArchive (Join-Path $testRoot 'intentionally-absent.rar')
if ((Get-FileHash -LiteralPath (Join-Path $bundled 'data.bin')).Hash -ne $bundledHash) { throw 'Setup-only rebuild changed data.bin.' }
Write-Host 'PASS: bundle keeps original compressed streams and original archive unchanged; Alter Egos is optional inside data.bin; setup rebuild needs no RAR file.'
Write-Host "Bundle test artifacts: $testRoot"
