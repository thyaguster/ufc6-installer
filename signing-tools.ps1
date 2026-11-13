$signingSources = @('src\Package.cs','src\SignedPackage.cs','tools\PackageTool.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$sourceHashes = ($signingSources | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join ''
$hashAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $toolId = [BitConverter]::ToString($hashAlgorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceHashes))).Replace('-','').Substring(0,16) } finally { $hashAlgorithm.Dispose() }
$packageTool = Join-Path $PSScriptRoot ('.build\package-tools\package-tool-' + $toolId + '.exe')
$publicKeyPath = Join-Path $PSScriptRoot 'assets\package-signing-public.xml'
$privateKeyPath = Join-Path $PSScriptRoot '.secrets\package-signing-key.dpapi'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path (Split-Path -Parent $packageTool) -Force | Out-Null
if (!(Test-Path -LiteralPath $packageTool)) {
    & $compiler /nologo /target:exe /platform:x64 /main:PackageTool "/out:$packageTool" /reference:System.Web.Extensions.dll /reference:System.Security.dll $signingSources
    if ($LASTEXITCODE -ne 0) { throw 'Cannot compile signed-package tool.' }
}
$packageTool | Set-Content -LiteralPath (Join-Path $PSScriptRoot '.build\package-tool-path.txt') -Encoding UTF8
& $packageTool init $privateKeyPath $publicKeyPath
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize package-signing keys.' }
$publicDocument = [xml](Get-Content -LiteralPath $publicKeyPath -Raw)
$publicNodes = @($publicDocument.SelectNodes('/RSAKeyValue/*'))
if ($publicNodes.Count -ne 2 -or @($publicNodes | Where-Object { $_.Name -notin @('Modulus','Exponent') }).Count) {
    throw 'The published verification key must contain only public RSA parameters. Never embed a private key.'
}

function Write-SignedPackage([string]$RawArchive, [string]$SignedArchive, [string]$ManifestPath) {
    & $packageTool seal $RawArchive $SignedArchive $ManifestPath $privateKeyPath $publicKeyPath
    if ($LASTEXITCODE -ne 0) { throw 'The package could not be signed.' }
}
