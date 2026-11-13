param([string]$Installer = "$PSScriptRoot\..\.build\smoke-dist\setup.exe")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
Add-Type 'using System; using System.Runtime.InteropServices; public static class NativeButtonPreview { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags); }'
. (Join-Path $PSScriptRoot 'ui-load.ps1')
function Get-Buttons($parent) {
    foreach ($control in $parent.Controls) {
        if ($control.GetType().Name -eq 'SoftButton') { $control }
        Get-Buttons $control
    }
}
foreach ($scale in @(1, 1.25, 1.5, 2)) {
    $form = [Activator]::CreateInstance($assembly.GetType('InstallerForm'), $true)
    try {
        $form.Opacity = 0; $form.ShowInTaskbar = $false; $form.Show()
        [Windows.Forms.Application]::DoEvents()
        $g = $form.CreateGraphics()
        try { $actual = $g.DpiX / 96.0 } finally { $g.Dispose() }
        $form.GetType().GetMethod('ScalePreview', $flags).Invoke($form, @([single]($scale / $actual)))
        $header = @($form.Controls | Where-Object Text -eq 'INSTALLATION  /  GAME PACKAGE')[0]
        $badge = @($form.Controls | Where-Object AccessibleName -eq 'Game version being installed')[0]
        if ($header.Bounds.IntersectsWith($badge.Bounds)) { throw 'Header covers the version badge in the live control hierarchy.' }
        foreach ($button in @(Get-Buttons $form)) {
            $wasEnabled = $button.Enabled
            foreach ($state in @('normal','hover','pressed','disabled')) {
                $type = $button.GetType()
                $button.Enabled = $state -ne 'disabled'
                $type.GetMethod('OnMouseLeave', $flags).Invoke($button, @([EventArgs]::Empty))
                if ($state -eq 'hover' -or $state -eq 'pressed') {
                    $type.GetMethod('OnMouseEnter', $flags).Invoke($button, @([EventArgs]::Empty))
                }
                if ($state -eq 'pressed') {
                    $type.GetMethod('OnMouseDown', $flags).Invoke($button, @([Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 1, 10, 10, 0)))
                }
                $bitmap = New-Object Drawing.Bitmap($button.Width, $button.Height)
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    # Emulate a native state repaint that skips OnPaintBackground.
                    # A dirty backing surface must never survive around the corners.
                    $graphics.Clear([Drawing.Color]::Magenta)
                    $paint = [Windows.Forms.PaintEventArgs]::new($graphics, $button.ClientRectangle)
                    try { $type.GetMethod('OnPaint', $flags).Invoke($button, @($paint)) } finally { $paint.Dispose() }
                    foreach ($point in @([Drawing.Point]::new(0,0), [Drawing.Point]::new($button.Width-1,0), [Drawing.Point]::new(0,$button.Height-1), [Drawing.Point]::new($button.Width-1,$button.Height-1))) {
                        if ($bitmap.GetPixel($point.X,$point.Y).ToArgb() -ne $button.Parent.BackColor.ToArgb()) {
                            throw "$($scale)x $state '$($button.Text)': stale corner pixels remain."
                        }
                    }
                } finally { $graphics.Dispose(); $bitmap.Dispose() }
            }
            $button.Enabled = $wasEnabled
            $button.GetType().GetMethod('OnMouseLeave', $flags).Invoke($button, @([EventArgs]::Empty))
        }
        if ($scale -eq 1.25) {
            $directory = Join-Path (Split-Path -Parent $PSScriptRoot) '.build\ui-qa'
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            foreach ($page in @(0, 1)) {
                $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @($page))
                $form.Refresh(); [Windows.Forms.Application]::DoEvents()
                $bitmap = [Drawing.Bitmap]::new($form.Width, $form.Height)
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $context = $graphics.GetHdc()
                    try { if (![NativeButtonPreview]::PrintWindow($form.Handle, $context, 2)) { throw 'Native window capture failed.' } }
                    finally { $graphics.ReleaseHdc($context) }
                    $bitmap.Save((Join-Path $directory ('live-page-' + ($page + 1) + '.png')))
                } finally { $graphics.Dispose(); $bitmap.Dispose() }
            }
        }
        Write-Host "PASS: all rounded buttons repaint clean corners at $($scale)x in normal, hover, pressed and disabled states; version badge is unobstructed."
    } finally { $form.Dispose() }
}
