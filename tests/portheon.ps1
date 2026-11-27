[CmdletBinding()]
param([switch]$Live, [switch]$Overlay, [switch]$Icons)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root '.build\portheon-test'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $directory 'portheon-test.exe'
& $compiler /nologo /target:exe /platform:x64 /main:PortheonDownloadTest "/out:$harness" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\Package.cs') (Join-Path $root 'src\PackageInput.cs') (Join-Path $root 'src\SignedPackage.cs') (Join-Path $root 'src\Installer.cs') (Join-Path $root 'src\PortheonDownloader.cs') (Join-Path $root 'src\InstallLog.cs') (Join-Path $root 'src\InstallationPerformance.cs') (Join-Path $PSScriptRoot 'PortheonDownloadTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cannot compile PORTHEON test harness.' }
if ($Live) { if ($Icons) { & $harness $directory --live-icons } elseif ($Overlay) { & $harness $directory --live-overlay } else { & $harness $directory --live } } else { & $harness $directory }
if ($LASTEXITCODE -ne 0) { throw 'PORTHEON tests failed.' }
