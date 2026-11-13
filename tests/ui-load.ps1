# Load package metadata through the real signature-verifying startup path.
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $Installer).ProviderPath)
$resources = $assembly.GetManifestResourceNames()
if ($resources -contains 'package.json' -or $resources -contains 'alter-egos.rar') { throw 'Package metadata or Alter Egos is embedded in the generic installer.' }
$program = $assembly.GetType('Program')
$staticFlags = [Reflection.BindingFlags]'NonPublic,Static'
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$program.GetField('BaseDirectory', $staticFlags).SetValue($null, (Split-Path -Parent (Resolve-Path -LiteralPath $Installer).ProviderPath))
$program.GetMethod('LoadPackage', $staticFlags).Invoke($null, @())
$errorMessage = $program.GetField('PackageError', $staticFlags).GetValue($null)
if ($errorMessage) { throw $errorMessage }
$package = $program.GetField('Package', $staticFlags).GetValue($null)
