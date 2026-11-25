[CmdletBinding()]
param(
    [string]$GameRoot = 'T:\JOGOS II\ufc6 repack\EA SPORTS UFC 6\PPSA23566-EASPORTSUFC6GAME',
    [string]$ModRoot = 'C:\Scripts\kytyps5-ufc6-xbox-buttons-mod\release\UFC6-Xbox-Icons'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('.build\xbox-integration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$testRoot\tools" -Force | Out-Null
$fixture = Join-Path $testRoot 'game'
$profile = Join-Path $ModRoot 'profiles\PPSA23566-01.006.000.json'
$patcher = Join-Path $ModRoot 'UFC6-Xbox-Icons.exe'
& python (Join-Path $PSScriptRoot 'create-xbox-fixture.py') $GameRoot $profile $fixture
if ($LASTEXITCODE -ne 0) { throw 'Cannot create a compatible private fixture.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $testRoot 'icon-test.exe'
& $compiler /nologo /target:exe /platform:x64 /main:IconIntegrationTest "/out:$harness" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/resource:$patcher,xbox-icons.exe" (Join-Path $root 'src\Package.cs') (Join-Path $root 'src\PackageInput.cs') (Join-Path $root 'src\SignedPackage.cs') (Join-Path $root 'src\Installer.cs') (Join-Path $root 'src\PortheonDownloader.cs') (Join-Path $root 'src\InstallLog.cs') (Join-Path $root 'src\InstallationPerformance.cs') (Join-Path $PSScriptRoot 'IconIntegrationTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cannot compile integration harness.' }
& $harness $fixture "$testRoot\tools" (Get-FileHash -LiteralPath $patcher).Hash
if ($LASTEXITCODE -ne 0) { throw 'Installer integration failed.' }
& python -c 'import hashlib,json,pathlib,sys; root=pathlib.Path(sys.argv[1]); p=json.loads(pathlib.Path(sys.argv[2]).read_text()); bad=[]
for e in p["entries"]:
 with (root/e["path"]).open("rb") as f:
  f.seek(e["offset"]); data=f.read(e["length"])
 if hashlib.sha256(data).hexdigest()!=e["known_installed_sha256"]: bad.append(e["id"])
assert not bad,bad
print("PASS: all 430 installed Xbox ranges match the mod profile.")' $fixture $profile
if ($LASTEXITCODE -ne 0) { throw 'Xbox range verification failed.' }
$restoreExe = Join-Path $fixture 'Mods\Xbox Icons\UFC6-Xbox-Icons.exe'
$report = Join-Path $testRoot 'restore.json'
$process = Start-Process -FilePath $restoreExe -ArgumentList @('--restore', '--game-dir', ('"' + $fixture + '"'), '--report', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json).success) { throw 'Restoring PlayStation icons failed.' }
Write-Host 'PASS: included mod tool restored PlayStation icons from the local backup.'
Write-Host "Test artifacts: $testRoot"
