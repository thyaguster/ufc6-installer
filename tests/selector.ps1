# Run with Windows PowerShell 5.1 to load the installer's .NET Framework UI.
param([string]$Installer = "$PSScriptRoot\..\.build\smoke-dist\setup.exe")
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-load.ps1')
$form = [Activator]::CreateInstance($assembly.GetType('InstallerForm'), $true)
try {
    $flags = [Reflection.BindingFlags]'NonPublic,Instance'
    $configJson = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\optional-tools.json') -Raw
    $configured = $assembly.GetType('PortheonDownloader').GetMethod('ParseOptionalToolsConfig', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, [object[]]@([string]$configJson))
    $form.GetType().GetMethod('ApplyToolSlots', $flags).Invoke($form, @($configured))
    $form.GetType().GetField('optionalToolsChecked', $flags).SetValue($form, $true)
    if ($form.GetType().GetField('xboxIcons', $flags) -or $form.GetType().GetField('playStationIcons', $flags)) { throw 'The game setup page still exposes an icon selector.' }
    $alterEgos = $form.GetType().GetField('alterEgos', $flags).GetValue($form)
    if (!$alterEgos.Checked) { throw 'Alter Egos switch is not enabled by default.' }
    $toggle = [Windows.Forms.CheckBox].GetMethod('OnClick', $flags)
    $toggle.Invoke($alterEgos, @([EventArgs]::Empty))
    if ($alterEgos.Checked) { throw 'Alter Egos switch did not turn off.' }
    $toggle.Invoke($alterEgos, @([EventArgs]::Empty))
    if (!$alterEgos.Checked -or $alterEgos.AccessibleRole -ne 'CheckButton') { throw 'Alter Egos switch did not turn on or lost its accessibility role.' }
    Write-Host 'PASS: pill switch defaults on, toggles off/on, and retains checkbox accessibility.'
    $iconsMod = $form.GetType().GetField('installPortheon', $flags).GetValue($form)
    $launcherPanel = $form.GetType().GetField('launcherPanel', $flags).GetValue($form)
    $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @(1))
    $toolSlots = $form.GetType().GetField('toolSlots', $flags).GetValue($form)
    if ($null -eq $toolSlots[1]) { throw 'Controller Icons Mod card should be enabled by the GitHub configuration.' }
    $form.GetType().GetMethod('SetOptionalToolsOnline', $flags).Invoke($form, @($true, $null))
    if (!$iconsMod.Enabled -or $iconsMod.Checked) { throw 'Controller Icons Mod switch should be available and off by default.' }
    $toggle.Invoke($iconsMod, @([EventArgs]::Empty))
    if (!$iconsMod.Checked) { throw 'Controller Icons Mod switch did not turn on.' }
    $toggle.Invoke($iconsMod, @([EventArgs]::Empty))
    if ($iconsMod.Checked -or $iconsMod.AccessibleRole -ne 'CheckButton') { throw 'Controller Icons Mod switch did not turn off or lost accessibility.' }
    $disabledConfigJson = $configJson | ConvertFrom-Json
    $disabledConfigJson.slots[1].enabled = $false
    $disabledConfig = $assembly.GetType('PortheonDownloader').GetMethod('ParseOptionalToolsConfig', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, [object[]]@([string]($disabledConfigJson | ConvertTo-Json -Depth 10)))
    $form.GetType().GetMethod('ApplyToolSlots', $flags).Invoke($form, @($disabledConfig))
    $toolSlots = $form.GetType().GetField('toolSlots', $flags).GetValue($form)
    if ($iconsMod.Enabled -or $null -ne $toolSlots[1]) { throw 'Disabling the second GitHub mod did not hide its card.' }
    $form.GetType().GetMethod('ApplyToolSlots', $flags).Invoke($form, @($configured))
    Write-Host 'PASS: controller icons mod is available after the overlay and can be disabled remotely.'
    $overlay = $form.GetType().GetField('installOverlay', $flags).GetValue($form)
    if ($overlay.Checked) { throw 'Optional overlay switch should be off by default.' }
    $toggle.Invoke($overlay, @([EventArgs]::Empty))
    if (!$overlay.Checked) { throw 'Overlay switch did not turn on.' }
    $toggle.Invoke($overlay, @([EventArgs]::Empty))
    if ($overlay.Checked -or $overlay.AccessibleRole -ne 'CheckButton') { throw 'Overlay switch did not turn off or lost accessibility.' }
    if ($overlay.Parent.Top -ge $iconsMod.Parent.Top) { throw 'Overlay card should appear before Controller Icons Mod.' }
    Write-Host 'PASS: overlay card precedes Controller Icons Mod and its optional pill switch defaults off and toggles.'
    $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @(1))
    $form.GetType().GetMethod('SetOptionalToolsOnline', $flags).Invoke($form, @($false, 'No internet connection to GitHub · Mods are unavailable'))
    $status = $form.GetType().GetField('optionalToolsStatus', $flags).GetValue($form)
    $overlayGithub = $form.GetType().GetField('overlayGithub', $flags).GetValue($form)
    $portheonGithub = $form.GetType().GetField('portheonGithub', $flags).GetValue($form)
    if ($overlay.Enabled -or $iconsMod.Enabled -or $overlayGithub.Enabled -or $portheonGithub.Enabled -or $status.Text -notmatch 'No internet connection') { throw 'Offline state did not disable mods or explain why.' }
    $form.GetType().GetMethod('SetOptionalToolsOnline', $flags).Invoke($form, @($true, $null))
    if (!$overlay.Enabled -or !$iconsMod.Enabled -or !$overlayGithub.Enabled -or !$portheonGithub.Enabled -or $status.Text -notmatch 'loaded from GitHub') { throw 'Online state did not restore both mods.' }
    $manualJson = '{"slots":[{"enabled":true,"name":"Replacement ZIP tool","description":"Install it yourself from GitHub","note":"Read the project instructions","instructions":"Download the ZIP, extract it, then copy its files into the game folder.","installMode":"github","repository":"example/replacement"},{"enabled":false}]}'
    $manualConfig = $assembly.GetType('PortheonDownloader').GetMethod('ParseOptionalToolsConfig', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, [object[]]@([string]$manualJson))
    $form.GetType().GetMethod('ApplyToolSlots', $flags).Invoke($form, @($manualConfig))
    $instructions = $form.GetType().GetField('toolInstructions', $flags).GetValue($form)
    $title = $form.GetType().GetField('overlayTitle', $flags).GetValue($form)
    if ($title.Text -ne 'Replacement ZIP tool' -or $overlay.Visible -or !$instructions[0].Enabled -or !$overlayGithub.Enabled -or $launcherPanel.Visible) { throw 'Manual replacement tool card was not configured correctly.' }
    $form.GetType().GetMethod('ApplyToolSlots', $flags).Invoke($form, @($configured))
    Write-Host 'PASS: GitHub-only replacement software displays its instructions without an auto-install switch.'
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
    $overlay.Checked = $true; $iconsMod.Checked = $true
    $next.PerformClick()
    if ($page.GetValue($form) -ne 1 -or !$overlay.Visible -or !$launcherPanel.Visible -or $alterEgos.Visible) { throw "Next did not show both mods: page=$($page.GetValue($form)), overlay=$($overlay.Visible), icons=$($launcherPanel.Visible), alter=$($alterEgos.Visible)" }
    $qa = Join-Path ([IO.Path]::GetTempPath()) 'ufc6-installer-ui-qa'
    New-Item -ItemType Directory -Path $qa -Force | Out-Null
    $modsPreview = New-Object Drawing.Bitmap($form.Width, $form.Height)
    try { $form.DrawToBitmap($modsPreview, [Drawing.Rectangle]::new([Drawing.Point]::Empty, $form.Size)); $modsPreview.Save((Join-Path $qa 'mods-page.png')) } finally { $modsPreview.Dispose() }
    $next.PerformClick()
    $summary = $form.GetType().GetField('summaryTools', $flags).GetValue($form)
    if ($page.GetValue($form) -ne 2 -or !$mode.Visible -or $overlay.Visible -or $next.Text -ne 'Install game') { throw 'Final installation page is incorrect.' }
    if ($summary.Text -notmatch 'Parsectagon Overlay' -or $summary.Text -notmatch 'Controller Icons Mod' -or $form.GetType().GetField('summaryInstall', $flags).GetValue($form).Text -ne 'Install game files') { throw 'Summary lost chosen options.' }
    if ($form.GetType().GetField('attempted', $flags).GetValue($form)) { throw 'Next started installation before confirmation.' }
    $destination = $form.GetType().GetField('folder', $flags).GetValue($form)
    $originalDestination = $destination.Text
    $destination.Text = $PSScriptRoot
    if ($next.Enabled) { throw 'Install was enabled for an existing destination.' }
    $existingGame = Join-Path ([IO.Path]::GetTempPath()) ('ufc6-installer-existing-' + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path (Join-Path $existingGame 'sce_sys') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $existingGame 'sce_sys\param.json') -Value '{}'
    Set-Content -LiteralPath (Join-Path $existingGame 'eboot.bin') -Value ''
    try {
        $destination.Text = $existingGame
        if (!$next.Enabled -or $next.Text -ne 'Set up mods') { throw 'A detected UFC 6 installation did not enable the mods path.' }
        if ($alterEgos.Enabled) { throw 'Existing game options should be left unchanged.' }
        if ($form.GetType().GetField('summaryAlterEgos', $flags).GetValue($form).Text -ne 'Unchanged') { throw 'Summary did not identify an existing game as unchanged.' }
    } finally {
        $destination.Text = $originalDestination
        Remove-Item -LiteralPath $existingGame -Recurse -Force -ErrorAction SilentlyContinue
    }
    $back.PerformClick(); $back.PerformClick()
    if ($page.GetValue($form) -ne 0 -or !$overlay.Checked -or !$iconsMod.Checked) { throw 'Back navigation lost selected mods.' }
    $next.PerformClick(); $next.PerformClick()
    $running = $form.GetType().GetField('running', $flags)
    $running.SetValue($form, $true)
    $form.GetType().GetMethod('ShowPage', $flags).Invoke($form, @(0))
    if ($page.GetValue($form) -ne 2) { throw 'Navigation was allowed during installation.' }
    $running.SetValue($form, $false)
    $overlay.Checked = $false; $iconsMod.Checked = $false
    Write-Host 'PASS: three-page navigation, page visibility, preserved choices, summary and navigation lock during installation.'
    $mode.GetType().GetMethod('ShowOptions', $flags).Invoke($mode, @())
    if (!$menu.Visible -or $menu.Items.Count -ne 3 -or !$menu.Items[1].Checked) { throw 'Mode popup did not open with Balanced checked.' }
    $bitmap = New-Object Drawing.Bitmap($menu.Width, $menu.Height)
    try { $menu.DrawToBitmap($bitmap, $menu.ClientRectangle); $bitmap.Save((Join-Path $qa 'performance-menu.png')) } finally { $bitmap.Dispose() }
    $menu.Items[2].PerformClick()
    $menu.Close()
    if ($mode.SelectedItem -ne 'Quiet' -or $property.GetValue($null, $null).ToString() -ne 'Quiet') { throw 'Popup selection did not update the installation mode.' }
    $mode.SelectedIndex = 1
    Write-Host 'PASS: dark dropdown popup, keyboard selection, accessibility value and live mode change.'
    Write-Host 'PASS: performance dropdown defaults to Balanced and applies mode changes.'
    Write-Host 'PASS: game setup has no icon selector; both mod cards are configured through GitHub.'
} finally { $form.Dispose() }
