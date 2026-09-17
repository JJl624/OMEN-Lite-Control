param([string]$ZipPath = (Join-Path $PSScriptRoot 'dist\OMEN-Lite-Control-portable.zip'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Add-File($Archive, [string]$Source, [string]$Name) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $Source, $Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
function Add-Text($Archive, [string]$Name, [string]$Text) {
    $entry = $Archive.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object IO.StreamWriter($entry.Open(), [Text.UTF8Encoding]::new($true))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}
function Read-ZipText($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing license: $Name" }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot 'OMEN-Lite-Control.exe')).ProductVersion
$sourceUrl = "https://github.com/JJl624/OMEN-Lite-Control/archive/refs/tags/v$version.zip"
# Full notices stay with the binaries; source is in GitHub's Source code attachment.
$license = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Raw -Encoding UTF8
$license += "`r`nCorresponding application and dependency source: $sourceUrl`r`n"
$license += "`r`n=== EC module: LGPL ===`r`n" + (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ec\source\COPYING') -Raw -Encoding UTF8)
$driverSource = [IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot 'driver\PawnIO-2.2.0-source.zip'))
try {
    $license += "`r`n=== PawnIO: GPL ===`r`n" + (Read-ZipText $driverSource 'PawnIO-2.2.0/COPYING')
    $license += "`r`n=== PawnPP ===`r`n" + (Read-ZipText $driverSource 'PawnIO-2.2.0/PawnPP/LICENSE')
    $header = Read-ZipText $driverSource 'PawnIO-2.2.0/PawnIO/include/pawnio_um.h'
    $license += "`r`n=== PawnIO interface exception ===`r`n" + (($header -split '\r?\n' | Select-Object -First 45) -join "`r`n")
} finally { $driverSource.Dispose() }
$header = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ec\source\include\core.inc') -Encoding UTF8
$license += "`r`n=== PawnIO module headers: 0BSD ===`r`n" + (($header | Select-Object -First 15) -join "`r`n")
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $ZipPath) -Force | Out-Null
$stream = [IO.File]::Open($ZipPath, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @('OMEN-Lite-Control.exe','driver/PawnIO_setup.exe')) {
            Add-File $archive (Join-Path $PSScriptRoot $name) $name
        }
        Add-Text $archive 'LICENSE' $license
        $usage = @'
OMEN Lite Control — HP 84DB / BIOS F.19

完整解压，以管理员身份运行 OMEN-Lite-Control.exe。
首次回读性能状态时，按界面提示安装驱动。安装兼容版本的 PawnIO 后可删除 driver 文件夹；需通过程序安装或更新驱动时再恢复。
键盘选色后点击应用；设置仅保存在程序目录的 data 文件夹。

Extract all files and run OMEN-Lite-Control.exe as administrator.
Install the driver from the app when prompted for mode readback. After compatible PawnIO is installed, the driver folder can be deleted; restore it only to install or update the driver from the app.
Choose keyboard colors, then click Apply. Settings are stored only in the app directory’s data folder.

https://github.com/JJl624/OMEN-Lite-Control
'@
        Add-Text $archive 'README.txt' $usage
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Host "Packaged $ZipPath"
