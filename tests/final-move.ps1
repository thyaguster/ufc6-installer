param([string]$Installer = "$PSScriptRoot\..\release-setup\setup.exe")
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path ([IO.Path]::GetTempPath()) ('ufc6-final-move-test-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe "/out:$harness" (Join-Path $PSScriptRoot 'FinalMoveTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Final move test compilation failed.' }
    & $harness ([IO.Path]::GetFullPath($Installer))
    if ($LASTEXITCODE -ne 0) { throw 'Final move test failed.' }
} finally {
    if (Test-Path -LiteralPath $harness) { Remove-Item -LiteralPath $harness }
}
