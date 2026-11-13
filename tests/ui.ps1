[CmdletBinding()]
param([string]$Installer = "$PSScriptRoot\..\.build\smoke-dist\setup.exe")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = (Resolve-Path -LiteralPath $Installer).ProviderPath
$directory = Join-Path $root '.build\ui-qa'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$cases = @(
    @{Name='100'; Scale='1'; Width=1280; Height=720},
    @{Name='100-small'; Scale='1'; Width=1024; Height=600},
    @{Name='125'; Scale='1.25'; Width=1366; Height=768},
    @{Name='150'; Scale='1.5'; Width=1366; Height=768},
    @{Name='200'; Scale='2'; Width=1920; Height=1080}
)
foreach ($case in $cases) {
    foreach ($position in @('top','bottom')) {
        $path = Join-Path $directory ($case.Name + '-' + $position + '.png')
        $arguments = @('--preview', ('"' + $path + '"'), $case.Scale, $case.Width, $case.Height)
        if ($position -eq 'bottom') { $arguments += 'bottom' }
        $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $path)) { throw "UI render failed: $($case.Name) / $position" }
    }
}
$preview = Join-Path $root '.build\installer-preview.png'
$process = Start-Process -FilePath $exe -ArgumentList @('--preview', ('"' + $preview + '"'), '1', '1920', '1440') -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Full preview failed.' }
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::ExtractAssociatedIcon($exe)
try { $bitmap = $icon.ToBitmap(); try { $bitmap.Save((Join-Path $directory 'installer-icon.png')) } finally { $bitmap.Dispose() } } finally { $icon.Dispose() }
foreach ($page in @(1, 2)) {
    $path = Join-Path $root ('.build\installer-preview-page-' + ($page + 1) + '.png')
    $process = Start-Process -FilePath $exe -ArgumentList @('--preview', ('"' + $path + '"'), '1', '1920', '1440', 'top', $page) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Page preview failed: $page" }
    foreach ($case in $cases) {
        $path = Join-Path $directory ($case.Name + '-page-' + ($page + 1) + '.png')
        $process = Start-Process -FilePath $exe -ArgumentList @('--preview', ('"' + $path + '"'), $case.Scale, $case.Width, $case.Height, 'bottom', $page) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Scaled page preview failed: $page" }
    }
}
$cases | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'render-cases.json') -Encoding UTF8
Write-Host "Rendered all three pages at 100%, 125%, 150% and 200%, including a small-screen case: $directory"
Write-Host 'These simulate scale factors for visual inspection; they do not change Windows display settings.'
