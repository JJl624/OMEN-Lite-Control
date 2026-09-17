param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('OmenModeSwitcher.cs','EcReader.cs','DriverSetup.cs','KeyboardLightingControl.cs') | ForEach-Object { Join-Path (Join-Path $PSScriptRoot 'src') $_ }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe = Join-Path $OutputDirectory 'OMEN-Lite-Control.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/win32manifest:$PSScriptRoot\src\OmenModeSwitcher.manifest" `
    "/resource:$PSScriptRoot\ec\LpcACPIEC.bin,OmenLiteControl.LpcACPIEC.bin" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Management.dll "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Write-Host "Built $exe"
