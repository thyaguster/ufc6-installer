[CmdletBinding()]
param(
    [ValidateSet('All', 'Pack', 'Setup', 'Bundle', 'Seal')][string]$Mode = 'All',
    [string]$Source = 'T:\JOGOS II\ufc6 repack\EA SPORTS UFC 6\PPSA23566-EASPORTSUFC6GAME',
    [string]$OutputDirectory = "$PSScriptRoot\dist",
    [string]$PackageManifest = "$PSScriptRoot\.build\package.json",
    [string]$BaseArchive = '',
    [ValidateSet('Normal', 'Ultra')][string]$Compression = 'Normal',
    [string]$SevenZipDirectory = 'C:\Program Files\7-Zip',
    [string]$AlterEgosArchive = "$PSScriptRoot\PPSA23566.rar",
    [string]$XboxIconsPatcher = 'C:\Scripts\kytyps5-ufc6-xbox-buttons-mod\release\UFC6-Xbox-Icons\UFC6-Xbox-Icons.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$archiveTool = Join-Path $SevenZipDirectory '7z.exe'
foreach ($name in @('7z.exe', '7z.dll', 'License.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $SevenZipDirectory $name) -PathType Leaf)) { throw "Missing 7-Zip component: $name" }
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$PackageManifest = [IO.Path]::GetFullPath($PackageManifest)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $PackageManifest) -Force | Out-Null
$archive = Join-Path $OutputDirectory 'data.bin'

. (Join-Path $PSScriptRoot 'package-tools.ps1')
. (Join-Path $PSScriptRoot 'signing-tools.ps1')

function Publish-SignedPackage([string]$RawArchive, $Metadata) {
    $signedPath = Join-Path $OutputDirectory ('.signed-' + [Guid]::NewGuid().ToString('N'))
    $metadataPath = Join-Path (Split-Path -Parent $PackageManifest) ('.signing-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        $Metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8
        Write-SignedPackage $RawArchive $signedPath $metadataPath
        Move-Item -LiteralPath $signedPath -Destination $archive
        Copy-Item -LiteralPath $metadataPath -Destination $PackageManifest
    } finally {
        if (Test-Path -LiteralPath $signedPath) { Remove-Item -LiteralPath $signedPath }
        if (Test-Path -LiteralPath $metadataPath) { Remove-Item -LiteralPath $metadataPath }
    }
}

if ($Mode -eq 'Seal') {
    if (!$BaseArchive) { throw 'Seal requires -BaseArchive and its saved -PackageManifest.' }
    if (Test-Path -LiteralPath $archive) { throw 'Use a new output directory; the original package will be preserved.' }
    $BaseArchive = (Resolve-Path -LiteralPath $BaseArchive).ProviderPath
    $metadata = Get-Content -LiteralPath $PackageManifest -Raw | ConvertFrom-Json
    Publish-SignedPackage $BaseArchive $metadata
}

if ($Mode -eq 'Bundle') {
    if (!$BaseArchive) { throw 'Bundle requires -BaseArchive and its saved -PackageManifest.' }
    $BaseArchive = (Resolve-Path -LiteralPath $BaseArchive).ProviderPath
    if (Test-Path -LiteralPath $archive) { throw 'Choose a new output directory; the existing data.bin will be preserved.' }
    $manifest = Get-Content -LiteralPath $PackageManifest -Raw | ConvertFrom-Json
    if ((Get-Item -LiteralPath $BaseArchive).Length -ne $manifest.ArchiveBytes) { throw 'Base archive size does not match the manifest.' }
    if ($manifest.PSObject.Properties['PackageFormat'] -and $manifest.PackageFormat -eq 3) { throw 'Bundle expects an unsigned base archive. Use Seal for signed metadata changes or Pack for new game contents.' }
    $listing = & $archiveTool l -slt $BaseArchive | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect base archive.' }
    $header = [regex]::Match($listing, '(?m)^Headers Size = (\d+)')
    if (!$header.Success) { throw 'Cannot determine the original compressed-stream bounds.' }
    $packedLength = $manifest.ArchiveBytes - [long]$header.Groups[1].Value
    $temporaryArchive = Join-Path $OutputDirectory ('.bundling-' + [Guid]::NewGuid().ToString('N'))
    try {
        Add-OptionalPayload $BaseArchive $temporaryArchive $manifest
        Write-Host 'Verifying the original archive and the unchanged compressed game streams...'
        if (!('ArchiveVerifier' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'tools\ArchiveVerifier.cs') }
        $newHash = [ArchiveVerifier]::VerifyRebundle($BaseArchive, $temporaryArchive, $packedLength, $manifest.Sha256)
        Set-PackageProperty $manifest 'ArchiveBytes' (Get-Item -LiteralPath $temporaryArchive).Length
        Set-PackageProperty $manifest 'Sha256' $newHash
        Publish-SignedPackage $temporaryArchive $manifest
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
    }
}

if ($Mode -notin @('Setup', 'Bundle', 'Seal')) {
    $Source = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\')
    if (![IO.Directory]::Exists($Source)) { throw 'Source must be a folder.' }
    if ($OutputDirectory.StartsWith($Source + '\', [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory -eq $Source) {
        throw 'Output must be outside the source folder.'
    }
    if (Test-Path -LiteralPath $archive) { throw 'data.bin already exists. Use a new output directory to make a new package.' }
    $items = @(Get-ChildItem -LiteralPath $Source -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Source contains links or reparse points; use an ordinary folder tree.' }
    $files = @($items | Where-Object { !$_.PSIsContainer })
    if (!$files.Count) { throw 'Source contains no files.' }
    if (Test-Path -LiteralPath (Join-Path $Source $payloadFolderName)) { throw 'Source uses the reserved installer payload directory.' }
    [long]$bytes = ($files | Measure-Object Length -Sum).Sum
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($OutputDirectory))
    # Compression can approach the original size. Do not rely on a sample projection.
    if ($drive.AvailableFreeSpace -lt ($bytes * 1.01 + 256MB)) { throw 'The output drive needs room for an incompressible archive. Choose a drive with enough free space.' }
    $temporaryArchive = Join-Path $OutputDirectory ('.packing-' + [Guid]::NewGuid().ToString('N'))
    $level = if ($Compression -eq 'Ultra') { '-mx=9' } else { '-mx=5' }
    try {
        Push-Location -LiteralPath $Source
        try {
            & $archiveTool a -t7z $temporaryArchive '.\*' -r -m0=LZMA2 $level -md=64m -mmt=2 -ms=on -bsp1
            if ($LASTEXITCODE -ne 0) { throw "Compression failed with exit code $LASTEXITCODE." }
        } finally { Pop-Location }
        $payloadMetadata = [ordered]@{}
        Add-OptionalPayload $temporaryArchive $temporaryArchive $payloadMetadata
        & $archiveTool t -t7z $temporaryArchive -bsp1
        if ($LASTEXITCODE -ne 0) { throw 'The archive failed its integrity test.' }
        # Detect source additions, removals, or edits during a long compression run.
        $after = @(Get-ChildItem -LiteralPath $Source -Recurse -Force -File)
        $beforeIndex = @{}; foreach ($file in $files) { $beforeIndex[$file.FullName] = @($file.Length, $file.LastWriteTimeUtc.Ticks) }
        if ($after.Count -ne $files.Count) { throw 'The source changed while packing. Build again from a stable copy.' }
        foreach ($file in $after) {
            $before = $beforeIndex[$file.FullName]
            if (!$before -or $before[0] -ne $file.Length -or $before[1] -ne $file.LastWriteTimeUtc.Ticks) { throw 'The source changed while packing. Build again from a stable copy.' }
        }
        $gameVersion = ''
        $gameMetadata = Join-Path $Source 'sce_sys\param.json'
        if (Test-Path -LiteralPath $gameMetadata -PathType Leaf) {
            $metadata = Get-Content -LiteralPath $gameMetadata -Raw | ConvertFrom-Json
            if ($metadata.PSObject.Properties['contentVersion']) { $gameVersion = [string]$metadata.contentVersion }
        }
        $manifest = [ordered]@{
            Title = 'EA SPORTS UFC 6'; UnpackedBytes = $bytes; FileCount = $files.Count
            GameVersion = $gameVersion
            ArchiveBytes = (Get-Item -LiteralPath $temporaryArchive).Length
            Sha256 = '' # Calculated while writing the signed container.
        }
        foreach ($property in $payloadMetadata.GetEnumerator()) { Set-PackageProperty $manifest $property.Key $property.Value }
        Publish-SignedPackage $temporaryArchive $manifest
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
    }
}

if ($Mode -ne 'Pack') {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.x C# compiler is required.' }
    $temporarySetup = Join-Path $OutputDirectory ('.setup-' + [Guid]::NewGuid().ToString('N') + '.exe')
    $setupManifest = Join-Path (Split-Path -Parent $PackageManifest) ('setup-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        $extraResources = @()
        $settings = [pscustomobject]@{XboxIconsBytes=0; XboxIconsSha256=''}
        if ($XboxIconsPatcher) {
            $XboxIconsPatcher = (Resolve-Path -LiteralPath $XboxIconsPatcher).ProviderPath
            $settings | Add-Member -NotePropertyName XboxIconsBytes -NotePropertyValue (Get-Item -LiteralPath $XboxIconsPatcher).Length -Force
            $settings | Add-Member -NotePropertyName XboxIconsSha256 -NotePropertyValue (Get-FileHash -LiteralPath $XboxIconsPatcher).Hash -Force
            $extraResources += "/resource:$XboxIconsPatcher,xbox-icons.exe"
        } else { $settings | Add-Member -NotePropertyName XboxIconsBytes -NotePropertyValue 0 -Force }
        $settings | ConvertTo-Json | Set-Content -LiteralPath $setupManifest -Encoding UTF8
        & $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$(Join-Path $PSScriptRoot 'assets\installer.ico')" "/out:$temporarySetup" "/win32manifest:$(Join-Path $PSScriptRoot 'src\app.manifest')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/resource:$setupManifest,installer.json" "/resource:$publicKeyPath,package-public-key.xml" "/resource:$(Join-Path $PSScriptRoot 'assets\portheon-logo.png'),portheon-logo.png" "/resource:$(Join-Path $PSScriptRoot 'assets\overlay-logo.png'),overlay-logo.png" "/resource:$archiveTool,7z.exe" "/resource:$(Join-Path $SevenZipDirectory '7z.dll'),7z.dll" "/resource:$(Join-Path $SevenZipDirectory 'License.txt'),7zip-license.txt" @extraResources (Join-Path $PSScriptRoot 'src\Package.cs') (Join-Path $PSScriptRoot 'src\SignedPackage.cs') (Join-Path $PSScriptRoot 'src\Installer.cs') (Join-Path $PSScriptRoot 'src\PortheonDownloader.cs') (Join-Path $PSScriptRoot 'src\InstallLog.cs') (Join-Path $PSScriptRoot 'src\InstallationPerformance.cs') (Join-Path $PSScriptRoot 'src\AssemblyInfo.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
        Move-Item -LiteralPath $temporarySetup -Destination (Join-Path $OutputDirectory 'setup.exe') -Force
    } finally {
        if (Test-Path -LiteralPath $temporarySetup) { Remove-Item -LiteralPath $temporarySetup }
        if (Test-Path -LiteralPath $setupManifest) { Remove-Item -LiteralPath $setupManifest }
    }
}
Write-Host "Finished: $OutputDirectory"
