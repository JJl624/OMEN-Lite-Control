param([string]$ZipPath = (Join-Path $PSScriptRoot 'dist\OMEN-Lite-Control-portable.zip'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Add-File($Archive, [string]$Source, [string]$Name) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $Source, $Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
function Add-Text($Archive, [string]$Name, [string]$Text) {
    $entry = $Archive.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object IO.StreamWriter($entry.Open(), [Text.UTF8Encoding]::new($true))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}
# License texts and the pinned dependency source reference are self-contained.
$license = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Raw -Encoding UTF8
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $ZipPath) -Force | Out-Null
$stream = [IO.File]::Open($ZipPath, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @('OMEN-Lite-Control.exe')) {
            Add-File $archive (Join-Path $PSScriptRoot $name) $name
        }
        Add-Text $archive 'LICENSE' $license
        $usage = @'
OMEN Lite Control — HP 84DB / BIOS F.19

完整解压，以管理员身份运行 OMEN-Lite-Control.exe，无需安装附加驱动。
记录模式仅代表最后一次被 BIOS 接受的请求，不是实际硬件状态；可再次点击记录模式重新应用。
快捷键按记录循环；灯光联动仅在主动切换模式时应用。启动和刷新不会按旧记录自动写入灯光。
设置保存在 data/config.xml。键盘颜色通过 HP WMI 读取；可选最小化到托盘。

Extract and run OMEN-Lite-Control.exe as administrator. No additional driver is required.
The recorded mode is the last request accepted by BIOS, not hardware readback. Select it again to reapply.
Hotkeys cycle the record; linked lighting applies only on explicit mode changes, not startup or refresh.
Settings are stored in data/config.xml. Keyboard colors use HP WMI. Minimize to tray is optional.

https://github.com/JJl624/OMEN-Lite-Control
'@
        Add-Text $archive 'README.txt' $usage
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Host "Packaged $ZipPath"
