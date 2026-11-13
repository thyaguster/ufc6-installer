# Run with Windows PowerShell 5.1 to load the installer's .NET Framework UI.
param([string]$Installer = "$PSScriptRoot\..\.build\smoke-dist\setup.exe")
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-load.ps1')
$form = [Activator]::CreateInstance($assembly.GetType('InstallerForm'), $true)
try {
    $flags = [Reflection.BindingFlags]'NonPublic,Instance'
    $playStation = $form.GetType().GetField('playStationIcons', $flags).GetValue($form)
    $xbox = $form.GetType().GetField('xboxIcons', $flags).GetValue($form)
    if (!$playStation.Checked -or $xbox.Checked) { throw 'PlayStation was not selected by default.' }
    $click = [Windows.Forms.RadioButton].GetMethod('OnClick', $flags)
    $click.Invoke($xbox, @([EventArgs]::Empty))
    if (!$xbox.Checked -or $playStation.Checked) { throw 'Clicking Xbox did not exclusively select it.' }
    $click.Invoke($playStation, @([EventArgs]::Empty))
    if (!$playStation.Checked -or $xbox.Checked) { throw 'Clicking PlayStation did not exclusively select it.' }
    if ($playStation.AccessibleRole -ne 'RadioButton' -or $xbox.AccessibleRole -ne 'RadioButton') { throw 'Selector accessibility role was lost.' }
    $alterEgos = $form.GetType().GetField('alterEgos', $flags).GetValue($form)
    if (!$alterEgos.Checked) { throw 'Alter Egos switch is not enabled by default.' }
    $toggle = [Windows.Forms.CheckBox].GetMethod('OnClick', $flags)
    $toggle.Invoke($alterEgos, @([EventArgs]::Empty))
    if ($alterEgos.Checked) { throw 'Alter Egos switch did not turn off.' }
    $toggle.Invoke($alterEgos, @([EventArgs]::Empty))
    if (!$alterEgos.Checked -or $alterEgos.AccessibleRole -ne 'CheckButton') { throw 'Alter Egos switch did not turn on or lost its accessibility role.' }
    Write-Host 'PASS: pill switch defaults on, toggles off/on, and retains checkbox accessibility.'
    $portheon = $form.GetType().GetField('installPortheon', $flags).GetValue($form)
    if ($portheon.Checked) { throw 'Optional PORTHEON switch should be off by default.' }
    $toggle.Invoke($portheon, @([EventArgs]::Empty))
    if (!$portheon.Checked) { throw 'PORTHEON switch did not turn on.' }
    $toggle.Invoke($portheon, @([EventArgs]::Empty))
    if ($portheon.Checked -or $portheon.AccessibleRole -ne 'CheckButton') { throw 'PORTHEON switch did not turn off or lost accessibility.' }
    Write-Host 'PASS: PORTHEON pill switch defaults off and toggles both ways.'
    $overlay = $form.GetType().GetField('installOverlay', $flags).GetValue($form)
    if ($overlay.Checked) { throw 'Optional overlay switch should be off by default.' }
    $toggle.Invoke($overlay, @([EventArgs]::Empty))
    if (!$overlay.Checked) { throw 'Overlay switch did not turn on.' }
    $toggle.Invoke($overlay, @([EventArgs]::Empty))
    if ($overlay.Checked -or $overlay.AccessibleRole -ne 'CheckButton') { throw 'Overlay switch did not turn off or lost accessibility.' }
    if ($overlay.Parent.Top -ge $portheon.Parent.Top) { throw 'Overlay card should appear before PORTHEON.' }
    Write-Host 'PASS: overlay card precedes PORTHEON and its optional pill switch defaults off and toggles.'
    $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @(1))
    $form.GetType().GetMethod('SetOptionalToolsOnline', $flags).Invoke($form, @($false, 'No internet connection to GitHub · Optional tools are unavailable'))
    $status = $form.GetType().GetField('optionalToolsStatus', $flags).GetValue($form)
    $overlayGithub = $form.GetType().GetField('overlayGithub', $flags).GetValue($form)
    $portheonGithub = $form.GetType().GetField('portheonGithub', $flags).GetValue($form)
    if ($overlay.Enabled -or $portheon.Enabled -or $overlayGithub.Enabled -or $portheonGithub.Enabled -or $status.Text -notmatch 'No internet connection') { throw 'Offline state did not disable optional tools or explain why.' }
    $form.GetType().GetMethod('SetOptionalToolsOnline', $flags).Invoke($form, @($true, $null))
    if (!$overlay.Enabled -or !$portheon.Enabled -or !$overlayGithub.Enabled -or !$portheonGithub.Enabled -or $status.Text -notmatch 'available') { throw 'Online state did not restore optional tools.' }
    $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @(0))
    Write-Host 'PASS: optional tools clearly show offline status, disable Internet actions, and restore when GitHub is reachable.'
    $mode = $form.GetType().GetField('performanceMode', $flags).GetValue($form)
    if ($mode.SelectedItem -ne 'Balanced' -or !$mode.Enabled) { throw 'Performance selector is not enabled with Balanced selected.' }
    $property = $assembly.GetType('InstallationPerformance').GetProperty('Mode', [Reflection.BindingFlags]'NonPublic,Static')
    $mode.SelectedIndex = 2
    if ($property.GetValue($null, $null).ToString() -ne 'Quiet') { throw 'Performance selector did not apply Quiet mode.' }
    $mode.SelectedIndex = 0
    if ($property.GetValue($null, $null).ToString() -ne 'Fast') { throw 'Performance selector did not apply Fast mode.' }
    $mode.SelectedIndex = 1
    $key = $mode.GetType().GetMethod('OnKeyDown', $flags)
    $key.Invoke($mode, @([Windows.Forms.KeyEventArgs]::new([Windows.Forms.Keys]::Down)))
    if ($mode.SelectedItem -ne 'Quiet') { throw 'Down arrow did not select Quiet.' }
    $key.Invoke($mode, @([Windows.Forms.KeyEventArgs]::new([Windows.Forms.Keys]::Up)))
    if ($mode.SelectedItem -ne 'Balanced') { throw 'Up arrow did not select Balanced.' }
    if ($mode.AccessibleRole -ne 'ComboBox' -or $mode.AccessibilityObject.Value -ne 'Balanced') { throw 'Mode dropdown accessibility value is incorrect.' }
    $menu = $mode.GetType().GetField('menu', $flags).GetValue($mode)
    $form.Opacity = 0; $form.ShowInTaskbar = $false; $menu.Opacity = 0
    $form.Show(); [Windows.Forms.Application]::DoEvents()
    $next = $form.GetType().GetField('install', $flags).GetValue($form)
    $back = $form.GetType().GetField('back', $flags).GetValue($form)
    $page = $form.GetType().GetField('currentPage', $flags)
    if ($page.GetValue($form) -ne 0 -or $back.Enabled -or $next.Text -ne 'Next') { throw 'Initial wizard navigation is incorrect.' }
    $overlay.Checked = $true; $xbox.Checked = $true
    $next.PerformClick()
    if ($page.GetValue($form) -ne 1 -or !$overlay.Visible -or $alterEgos.Visible) { throw 'Next did not show only optional tools.' }
    $next.PerformClick()
    $summary = $form.GetType().GetField('summaryTools', $flags).GetValue($form)
    if ($page.GetValue($form) -ne 2 -or !$mode.Visible -or $overlay.Visible -or $next.Text -ne 'Install game') { throw 'Final installation page is incorrect.' }
    if ($summary.Text -notmatch 'Actions Overlay' -or $form.GetType().GetField('summaryIcons', $flags).GetValue($form).Text -ne 'Xbox') { throw 'Summary lost chosen options.' }
    if ($form.GetType().GetField('attempted', $flags).GetValue($form)) { throw 'Next started installation before confirmation.' }
    $destination = $form.GetType().GetField('folder', $flags).GetValue($form)
    $originalDestination = $destination.Text
    $destination.Text = $PSScriptRoot
    if ($next.Enabled) { throw 'Install was enabled for an existing destination.' }
    $destination.Text = $originalDestination
    $back.PerformClick(); $back.PerformClick()
    if ($page.GetValue($form) -ne 0 -or !$overlay.Checked -or !$xbox.Checked) { throw 'Back navigation lost selected options.' }
    $next.PerformClick(); $next.PerformClick()
    $running = $form.GetType().GetField('running', $flags)
    $running.SetValue($form, $true)
    $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @(0))
    if ($page.GetValue($form) -ne 2) { throw 'Navigation was allowed during installation.' }
    $running.SetValue($form, $false)
    $overlay.Checked = $false; $playStation.Checked = $true
    Write-Host 'PASS: three-page navigation, page visibility, preserved choices, summary and navigation lock during installation.'
    $mode.GetType().GetMethod('ShowOptions', $flags).Invoke($mode, @())
    if (!$menu.Visible -or $menu.Items.Count -ne 3 -or !$menu.Items[1].Checked) { throw 'Mode popup did not open with Balanced checked.' }
    $qa = Join-Path (Split-Path -Parent $PSScriptRoot) '.build\ui-qa'
    New-Item -ItemType Directory -Path $qa -Force | Out-Null
    $bitmap = New-Object Drawing.Bitmap($menu.Width, $menu.Height)
    try { $menu.DrawToBitmap($bitmap, $menu.ClientRectangle); $bitmap.Save((Join-Path $qa 'performance-menu.png')) } finally { $bitmap.Dispose() }
    $menu.Items[2].PerformClick()
    $menu.Close()
    if ($mode.SelectedItem -ne 'Quiet' -or $property.GetValue($null, $null).ToString() -ne 'Quiet') { throw 'Popup selection did not update the installation mode.' }
    $mode.SelectedIndex = 1
    Write-Host 'PASS: dark dropdown popup, keyboard selection, accessibility value and live mode change.'
    Write-Host 'PASS: performance dropdown defaults to Balanced and applies mode changes.'
    Write-Host 'PASS: actual installer defaults to PlayStation; Xbox and PlayStation cards select exclusively; radio accessibility retained.'
} finally { $form.Dispose() }
