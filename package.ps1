param([string]$ZipPath = (Join-Path $PSScriptRoot 'dist\OMEN-Lite-Control-v0.3.0-portable.zip'))
$ErrorActionPreference = 'Stop'
$stage = Join-Path $PSScriptRoot ('work\package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
# Explicit allowlist: never package local presets, backups or developer scratch files.
$files = @('OMEN-Lite-Control.exe','OmenModeSwitcher.exe','OmenModeSwitcher.cs',
    'EcReader.cs','DriverSetup.cs','OmenModeSwitcher.manifest','build.ps1','package.ps1',
    'README.md','README.zh-CN.md','README-independent.txt','LICENSE','THIRD_PARTY_NOTICES.md')
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
foreach ($name in @('assets','docs','ec','driver','tests')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage -Recurse
}
New-Item -ItemType Directory -Path (Split-Path -Parent $ZipPath) -Force | Out-Null
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $ZipPath -Force
Write-Host "Packaged $ZipPath"
