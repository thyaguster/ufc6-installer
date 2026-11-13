# Shared package metadata and optional payload handling. Game assets belong in data.bin.
$payloadFolderName = '__ufc6_installer__'
$payloadEntry = '__ufc6_installer__\alter-egos.rar'

function Read-ArchiveFiles([string]$Path) {
    $listing = (& $archiveTool l -slt $Path | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect archive: $Path" }
    $entries = @()
    foreach ($block in ($listing -split '(?:\r?\n){2,}')) {
        $properties = @{}
        foreach ($line in ($block -split '\r?\n')) {
            if ($line -match '^([^=]+?) = (.*)$') { $properties[$matches[1]] = $matches[2] }
        }
        if ($properties.ContainsKey('Size') -and ($properties['Folder'] -eq '-' -or
            (!$properties.ContainsKey('Folder') -and $properties.ContainsKey('Attributes') -and $properties['Attributes'] -notmatch '^D'))) {
            $entries += [pscustomobject]@{ Path = $properties['Path']; Size = [long]$properties['Size']; CRC = $properties['CRC'] }
        }
    }
    return $entries
}


function Set-PackageProperty($Manifest, [string]$Name, $Value) {
    if ($Manifest -is [Collections.IDictionary]) { $Manifest[$Name] = $Value }
    else { $Manifest | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force }
}

function Add-OptionalPayload([string]$ExistingArchive, [string]$NewArchive, $Manifest) {
    Set-PackageProperty $Manifest 'PayloadVersion' 2
    Set-PackageProperty $Manifest 'AlterEgosArchiveBytes' 0
    Set-PackageProperty $Manifest 'AlterEgosArchivePath' ''
    if (!$AlterEgosArchive) {
        if ($ExistingArchive -ne $NewArchive) { Copy-Item -LiteralPath $ExistingArchive -Destination $NewArchive }
        return
    }
    $addonSource = (Resolve-Path -LiteralPath $AlterEgosArchive).ProviderPath
    & $archiveTool t $addonSource -bsp0 -bso0
    if ($LASTEXITCODE -ne 0) { throw 'The Alter Egos archive failed its integrity test.' }
    $addonFiles = @(Read-ArchiveFiles $addonSource)
    if (!$addonFiles.Count) { throw 'The Alter Egos archive contains no files.' }
    $baseFiles = @{}
    foreach ($entry in (Read-ArchiveFiles $ExistingArchive)) {
        $entryPath = $entry.Path.Replace('/', '\')
        if ($entryPath -eq $payloadFolderName -or $entryPath.StartsWith($payloadFolderName + '\')) { throw 'This archive already contains installer payloads.' }
        $baseFiles[$entryPath] = $entry.Size
    }
    [long]$delta = 0; [long]$expanded = 0; [int]$addedFiles = 0
    foreach ($entry in $addonFiles) {
        $path = $entry.Path.Replace('/', '\')
        if (!$path.StartsWith('PPSA23566\') -or $path -match '(^|\\)\.\.(\\|$)' -or $path.Contains(':')) { throw "Unexpected Alter Egos path: $path" }
        $relative = $path.Substring('PPSA23566\'.Length)
        $expanded += $entry.Size; $delta += $entry.Size
        if ($baseFiles.ContainsKey($relative)) { $delta -= $baseFiles[$relative] } else { $addedFiles++ }
    }
    $addonHash = (Get-FileHash -LiteralPath $addonSource -Algorithm SHA256).Hash
    $stage = Join-Path (Split-Path -Parent $NewArchive) ('.payload-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path (Join-Path $stage $payloadFolderName) -Force | Out-Null
        $stagedAddon = Join-Path $stage $payloadEntry
        Copy-Item -LiteralPath $addonSource -Destination $stagedAddon
        if ((Get-FileHash -LiteralPath $stagedAddon).Hash -ne $addonHash) { throw 'Alter Egos changed while being packaged.' }
        Push-Location -LiteralPath $stage
        try {
            if ($ExistingArchive -eq $NewArchive) {
                & $archiveTool a -t7z $NewArchive $payloadFolderName -mx=0 -ms=off -bsp1
            } else {
                # Copy the old compressed streams into a new archive and add one stored block.
                # The source archive is never updated or recompressed.
                & $archiveTool u -t7z $ExistingArchive $payloadFolderName -u- "-up1q1r2x1y1z1w1!$NewArchive" -mx=0 -ms=off -bsp1
            }
            if ($LASTEXITCODE -ne 0) { throw 'Cannot add Alter Egos to data.bin.' }
        } finally { Pop-Location }
        & $archiveTool t -t7z $NewArchive $payloadEntry -bso0 -bsp0
        if ($LASTEXITCODE -ne 0) { throw 'Bundled Alter Egos failed its archive integrity test.' }
        foreach ($property in @{
            AlterEgosArchiveBytes = (Get-Item -LiteralPath $addonSource).Length
            AlterEgosArchivePath = $payloadEntry
            AlterEgosUnpackedBytes = $expanded; AlterEgosInstalledDeltaBytes = $delta
            AlterEgosFileDelta = $addedFiles; AlterEgosSha256 = $addonHash
        }.GetEnumerator()) { Set-PackageProperty $Manifest $property.Key $property.Value }
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
}
