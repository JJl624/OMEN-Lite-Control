param([Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ZipPath))
try {
    $expected = @('OMEN-Lite-Control.exe','README.txt','LICENSE','THIRD-PARTY.zip','ec/LpcACPIEC.bin','driver/PawnIO_setup.exe')
    $difference = Compare-Object ($expected | Sort-Object) (@($zip.Entries.FullName) | Sort-Object)
    if ($difference) { throw 'Unexpected portable package contents.' }
    $sourceStream = $zip.GetEntry('THIRD-PARTY.zip').Open()
    $memory = New-Object IO.MemoryStream
    try {
        $sourceStream.CopyTo($memory)
        $memory.Position = 0
        $sources = New-Object IO.Compression.ZipArchive($memory, [IO.Compression.ZipArchiveMode]::Read)
        try {
            foreach ($name in @('THIRD_PARTY_NOTICES.md','ec/source/COPYING','ec/source/LpcACPIEC.p','driver/PawnIO-2.2.0-source.zip')) {
                if (!$sources.GetEntry($name)) { throw "Missing third-party source/license: $name" }
            }
        } finally { $sources.Dispose() }
    } finally { $memory.Dispose(); $sourceStream.Dispose() }
} finally { $zip.Dispose() }
Write-Host 'PASS: exact six-file runtime package; third-party source and licenses retained.'
