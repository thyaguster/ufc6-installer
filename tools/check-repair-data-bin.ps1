param(
    [Parameter(Mandatory=$true)][string]$DataBin,
    [string]$Manifest = "$PSScriptRoot\..\data-bin-repair.json",
    [string]$ReplacementPart
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RangeHash([IO.FileStream]$Stream, [long]$Offset, [long]$Length) {
    $hash = [Security.Cryptography.SHA256]::Create()
    $buffer = New-Object byte[] (4MB)
    try {
        $Stream.Position = $Offset
        [long]$remaining = $Length
        while ($remaining -gt 0) {
            $read = $Stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
            if ($read -le 0) { throw 'data.bin ended while checking a range.' }
            [void]$hash.TransformBlock($buffer, 0, $read, $buffer, 0)
            $remaining -= $read
        }
        [void]$hash.TransformFinalBlock([byte[]]@(), 0, 0)
        return [BitConverter]::ToString($hash.Hash).Replace('-','')
    } finally { $hash.Dispose() }
}

$package = (Resolve-Path -LiteralPath $DataBin).ProviderPath
$manifestData = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($manifestData.format -ne 1 -or $manifestData.headerBytes -ne 65536 -or !$manifestData.parts.Count) { throw 'Unsupported repair manifest.' }
[long]$expectedOffset = 65536
for ($index = 0; $index -lt $manifestData.parts.Count; $index++) {
    $entry = $manifestData.parts[$index]
    if ($entry.name -ne ('data.bin.' + ($index + 1).ToString('D3')) -or
        [long]$entry.offset -ne $expectedOffset -or [long]$entry.length -le 0 -or
        $entry.sha256 -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'Repair manifest has invalid part entries.' }
    $expectedOffset += [long]$entry.length
}
if ($expectedOffset -ne [long]$manifestData.archiveBytes -or
    $manifestData.headerSha256 -notmatch '\A[0-9a-fA-F]{64}\z' -or
    $manifestData.payloadSha256 -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'Repair manifest has invalid package totals or hashes.' }
$info = Get-Item -LiteralPath $package
if ($info.Length -ne [long]$manifestData.archiveBytes) { throw "data.bin length is $($info.Length), expected $($manifestData.archiveBytes). A partial download cannot be repaired with these parts." }

if ($ReplacementPart) {
    $replacement = (Resolve-Path -LiteralPath $ReplacementPart).ProviderPath
    $name = Split-Path -Leaf $replacement
    $part = @($manifestData.parts | Where-Object name -EQ $name)
    if ($part.Count -ne 1) { throw "The replacement must be one of the numbered parts listed in the manifest: $name" }
    $part = $part[0]
    Write-Host "Checking replacement $name..."
    if ((Get-Item -LiteralPath $replacement).Length -ne [long]$part.length -or (Get-FileHash -LiteralPath $replacement -Algorithm SHA256).Hash -ne $part.sha256) {
        throw "The downloaded $name does not match the repair manifest. Nothing was changed."
    }
    $source = [IO.File]::Open($replacement, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $target = [IO.File]::Open($package, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        if ($target.Length -ne [long]$manifestData.archiveBytes -or (Get-RangeHash $target 0 65536) -ne $manifestData.headerSha256) { throw 'data.bin changed or has a damaged signed header. Nothing was changed.' }
        Write-Host "Writing $name into data.bin..."
        $target.Position = [long]$part.offset
        $buffer = New-Object byte[] (4MB)
        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) { $target.Write($buffer, 0, $read) }
        $target.Flush()
        if ((Get-RangeHash $target ([long]$part.offset) ([long]$part.length)) -ne $part.sha256) { throw "Repair of $name failed. Keep the replacement part and retry." }
        Write-Host "Repaired $name in data.bin. Run this script again without -ReplacementPart to check every part."
    } finally { $target.Dispose(); $source.Dispose() }
    return
}

$stream = [IO.File]::Open($package, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    if ((Get-RangeHash $stream 0 65536) -ne $manifestData.headerSha256) { throw 'The signed header is damaged or this is a different data.bin.' }
    $bad = @()
    foreach ($part in $manifestData.parts) {
        Write-Host "Checking $($part.name)..."
        $actual = Get-RangeHash $stream ([long]$part.offset) ([long]$part.length)
        if ($actual -eq $part.sha256) { Write-Host "OK  $($part.name)" }
        else { Write-Host "BAD $($part.name)"; $bad += $part.name }
    }
    if ($bad.Count) { throw "Damaged range(s): $($bad -join ', '). Download only those numbered parts, repair them with -ReplacementPart, then run setup.exe again." }
    Write-Host 'All ranges match. data.bin should pass the installer integrity check.'
} finally { $stream.Dispose() }
