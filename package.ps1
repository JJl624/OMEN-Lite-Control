param([string]$ZipPath = (Join-Path $PSScriptRoot 'dist\OMEN-Lite-Control-portable.zip'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $ZipPath) -Force | Out-Null
# Runtime files only; keep third-party source and licenses with their components.
$files = @('OMEN-Lite-Control.exe','README.md','README.zh-CN.md','LICENSE','THIRD_PARTY_NOTICES.md')
$paths = @($files | ForEach-Object { Get-Item -LiteralPath (Join-Path $PSScriptRoot $_) })
foreach ($folder in @('assets','docs','ec','driver')) {
    $paths += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -Recurse -File -Force)
}
$stream = [IO.File]::Open($ZipPath, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $paths) {
            $entry = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Host "Packaged $ZipPath"
