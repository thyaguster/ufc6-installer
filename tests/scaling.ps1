# Run with Windows PowerShell 5.1 to inspect the actual .NET Framework controls.
param([string]$Installer = "$PSScriptRoot\..\.build\smoke-dist\setup.exe")
$ErrorActionPreference = 'Stop'
Add-Type 'using System.Runtime.InteropServices; public static class ScalingDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[void][ScalingDpi]::SetProcessDPIAware()
. (Join-Path $PSScriptRoot 'ui-load.ps1')
# Exercise real game-sized figures instead of only the small fixture values.
$package.UnpackedBytes = 167610554470; $package.ArchiveBytes = 59876298006; $package.FileCount = 405
$program.GetField('Package', [Reflection.BindingFlags]'NonPublic,Static').SetValue($null, $package)
$configJson = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\optional-tools.json') -Raw
$config = $assembly.GetType('PortheonDownloader').GetMethod('ParseOptionalToolsConfig', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, [object[]]@([string]$configJson))
$issues = New-Object 'Collections.Generic.List[string]'
function Check-Controls($Parent, [string]$Context) {
    foreach ($control in $Parent.Controls) {
        if ($control -is [Windows.Forms.Label] -and $control.Text -and !$control.AutoEllipsis) {
            $graphics = $control.CreateGraphics()
            try {
                $preferred = [Windows.Forms.TextRenderer]::MeasureText($graphics, $control.Text, $control.Font,
                    [Drawing.Size]::new($control.Width, [int]::MaxValue),
                    [Windows.Forms.TextFormatFlags]'WordBreak,NoPrefix,TextBoxControl')
            } finally { $graphics.Dispose() }
            # Auto-sized link labels use their own text padding/rendering rules.
            if ($control.AutoSize) { $preferred = $control.GetPreferredSize([Drawing.Size]::Empty) }
            if ($preferred.Height -gt $control.Height + 2) {
                $issues.Add("${Context}: text needs $($preferred.Height)px, has $($control.Height)px: $($control.Text)")
            }
        }
        if ($control -is [Windows.Forms.Button] -and $control.Text) {
            $graphics = $control.CreateGraphics()
            try {
                $textSize = [Windows.Forms.TextRenderer]::MeasureText($graphics, $control.Text, $control.Font,
                    [Drawing.Size]::new([int]::MaxValue, [int]::MaxValue), [Windows.Forms.TextFormatFlags]::SingleLine)
            } finally { $graphics.Dispose() }
            if ($textSize.Width -gt $control.Width - 4 -or $textSize.Height -gt $control.Height) {
                $issues.Add("${Context}: button text does not fit: $($control.Text)")
            }
        }
        if ($Parent -isnot [Windows.Forms.Form] -and
            ($control.Left -lt -1 -or $control.Top -lt -1 -or $control.Right -gt $Parent.ClientSize.Width + 2 -or $control.Bottom -gt $Parent.ClientSize.Height + 2)) {
            $issues.Add("${Context}: child exceeds parent: $($control.GetType().Name) $($control.Text)")
        }
        Check-Controls $control $Context
    }
}
$cases = @(
    @{Scale=1.0; Width=1280; Height=720},
    @{Scale=1.0; Width=1024; Height=600},
    @{Scale=1.25; Width=1366; Height=768},
    @{Scale=1.25; Width=1920; Height=1080},
    @{Scale=1.5; Width=1366; Height=768},
    @{Scale=2.0; Width=1920; Height=1080}
)
foreach ($case in $cases) {
    $form = [Activator]::CreateInstance($assembly.GetType('InstallerForm'), $true)
    try {
        $form.Opacity = 0; $form.ShowInTaskbar = $false; $form.Show()
        [Windows.Forms.Application]::DoEvents()
        $graphics = $form.CreateGraphics()
        try { $actual = $graphics.DpiX / 96.0 } finally { $graphics.Dispose() }
        $form.GetType().GetMethod('ScalePreview', $flags).Invoke($form, @([single]($case.Scale / $actual)))
        $form.GetType().GetMethod('ConfigureViewport', $flags).Invoke($form, @([Drawing.Size]::new($case.Width, $case.Height), [single]$case.Scale))
        $form.GetType().GetField('optionalToolsChecked', $flags).SetValue($form, $true)
        $form.GetType().GetMethod('ApplyToolSlots', $flags).Invoke($form, @($config))
        $firstCard = $form.GetType().GetField('overlayPanel', $flags).GetValue($form)
        $secondCard = $form.GetType().GetField('launcherPanel', $flags).GetValue($form)
        $modsStatus = $form.GetType().GetField('optionalToolsStatus', $flags).GetValue($form)
        $firstTitle = $form.GetType().GetField('overlayTitle', $flags).GetValue($form)
        $secondTitle = $form.GetType().GetField('launcherTitle', $flags).GetValue($form)
        $firstLogo = $form.GetType().GetField('overlayLogo', $flags).GetValue($form)
        $secondLogo = $form.GetType().GetField('secondModLogo', $flags).GetValue($form)
        $buttonPreview = $form.GetType().GetField('xboxPreview', $flags).GetValue($form)
        if ($secondCard.Top - $firstCard.Bottom -lt [Math]::Round(8 * $case.Scale) -or $modsStatus.Top -lt $secondCard.Bottom -or
            $firstTitle.Left -le $firstLogo.Right -or $secondTitle.Left -le $secondLogo.Right -or
            $buttonPreview.Left -le $secondTitle.Right -or $buttonPreview.Right -gt $secondCard.ClientSize.Width) {
            $issues.Add("$($case.Scale)x: remote mod cards, logos, Xbox preview or status overlap after scaling")
        }
        $form.GetType().GetField('installOverlay', $flags).GetValue($form).Checked = $true
        $form.GetType().GetField('installPortheon', $flags).GetValue($form).Checked = $true
        $form.GetType().GetField('folder', $flags).GetValue($form).Text = 'C:\Games\' + ('Long destination folder\' * 8) + 'EA SPORTS UFC 6'
        foreach ($page in @(0, 1, 2)) {
            $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @($page))
            [Windows.Forms.Application]::DoEvents()
            Check-Controls $form "$($case.Scale)x / page $($page + 1)"
            if ($case.Scale -eq 1.25 -and $case.Width -eq 1920 -and $page -eq 1) {
                $preview = Join-Path ([IO.Path]::GetTempPath()) 'ufc6-mods-page-125.png'
                $bitmap = New-Object Drawing.Bitmap($form.Width, $form.Height)
                try { $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new([Drawing.Point]::Empty, $form.Size)); $bitmap.Save($preview) } finally { $bitmap.Dispose() }
            }
            $form.AutoScrollPosition = [Drawing.Point]::new(0, $form.DisplayRectangle.Height)
            [Windows.Forms.Application]::DoEvents()
            foreach ($field in @('install', 'back', 'cancel')) {
                $button = $form.GetType().GetField($field, $flags).GetValue($form)
                if (!$form.ClientRectangle.Contains($button.Bounds)) { $issues.Add("$($case.Scale)x: $field button cannot be reached by scrolling") }
            }
        }
        Write-Host "Checked all three pages at $($case.Scale)x / $($case.Width)x$($case.Height)."
    } finally { $form.Dispose() }
}
if ($issues.Count) { throw ($issues | Select-Object -Unique | Out-String) }
Write-Host 'PASS: text fit, nested control bounds, real game sizes, long destination paths, combined tools and navigation reachability.'
