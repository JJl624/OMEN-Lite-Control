$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$nvapi = Join-Path $root 'deps\nvapi'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (-not (Test-Path $nvapi)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $nvapi) | Out-Null
    git clone --depth 1 https://github.com/NVIDIA/nvapi.git $nvapi
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /platform:x64 /optimize+ `
    /win32manifest:"$root\src\OmenLiteControl.manifest" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Management.dll `
    /out:"$dist\OMEN-Lite-Control.exe" "$root\src\OmenLiteControl.cs"

cl.exe /nologo /LD /EHsc /I "$nvapi" "$root\native\OmenNvApi.cpp" `
    /link "/LIBPATH:$nvapi\amd64" nvapi64.lib "/OUT:$dist\OmenNvApi.dll"

Copy-Item "$root\README.md","$root\LICENSE","$root\THIRD_PARTY_NOTICES.md" $dist
Write-Host "Build complete: $dist"
