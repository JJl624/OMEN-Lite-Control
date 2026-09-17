param([string]$ZipPath = (Join-Path $PSScriptRoot 'dist\OMEN-Lite-Control-portable.zip'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Add-File($Archive, [string]$Source, [string]$Name) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $Source, $Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
function Add-Bytes($Archive, [string]$Name, [byte[]]$Bytes) {
    $entry = $Archive.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $output = $entry.Open()
    try { $output.Write($Bytes, 0, $Bytes.Length) } finally { $output.Dispose() }
}
# Consolidate third-party source and licenses, without polluting runtime folders.
$legal = New-Object IO.MemoryStream
try {
    $sources = New-Object IO.Compression.ZipArchive($legal, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        Add-File $sources (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') 'THIRD_PARTY_NOTICES.md'
        Add-File $sources (Join-Path $PSScriptRoot 'driver\PawnIO-2.2.0-source.zip') 'driver/PawnIO-2.2.0-source.zip'
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ec\source') -File -Recurse -Force) {
            Add-File $sources $file.FullName $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
        }
    } finally { $sources.Dispose() }
    $ZipPath = [IO.Path]::GetFullPath($ZipPath)
    New-Item -ItemType Directory -Path (Split-Path -Parent $ZipPath) -Force | Out-Null
    $stream = [IO.File]::Open($ZipPath, [IO.FileMode]::Create)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            # Exact allowlist: no directory-wide copies of application or development files.
            foreach ($name in @('OMEN-Lite-Control.exe','ec/LpcACPIEC.bin','driver/PawnIO_setup.exe','LICENSE')) {
                Add-File $archive (Join-Path $PSScriptRoot $name) $name
            }
            $usage = @'
OMEN Lite Control — HP 84DB / BIOS F.19

完整解压，以管理员身份运行 OMEN-Lite-Control.exe。
首次回读性能状态时，按界面提示安装驱动。保留 ec 和 driver 文件夹。
键盘选色后点击应用；设置自动保存在 data 文件夹。

Extract all files and run OMEN-Lite-Control.exe as administrator.
Install the driver from the app when prompted for mode readback. Keep ec and driver.
Choose keyboard colors, then click Apply. Settings are saved in data.

Third-party source and licenses: THIRD-PARTY.zip
https://github.com/JJl624/OMEN-Lite-Control
'@
            Add-Bytes $archive 'README.txt' ([Text.UTF8Encoding]::new($true).GetPreamble() + [Text.Encoding]::UTF8.GetBytes($usage))
            Add-Bytes $archive 'THIRD-PARTY.zip' $legal.ToArray()
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
} finally { $legal.Dispose() }
Write-Host "Packaged $ZipPath"
