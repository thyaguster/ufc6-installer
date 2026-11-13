$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root '.build\performance-test'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $directory 'performance-test.exe'
& $compiler /nologo /target:exe /platform:x64 "/out:$harness" (Join-Path $root 'src\InstallationPerformance.cs') (Join-Path $root 'src\InstallLog.cs') (Join-Path $PSScriptRoot 'PerformanceTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Performance test compilation failed.' }
& $harness
if ($LASTEXITCODE -ne 0) { throw 'Performance test failed.' }
