$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root 'work'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$testExe = Join-Path $scratch 'EcReaderTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 `
    /reference:System.Management.dll "/out:$testExe" "$root\EcReader.cs" "$PSScriptRoot\EcReaderTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'EC tests failed.' }
$setupTest = Join-Path $scratch 'DriverSetupTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 `
    /reference:System.Management.dll "/out:$setupTest" "$root\EcReader.cs" "$root\DriverSetup.cs" "$PSScriptRoot\DriverSetupTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Setup test compilation failed.' }
& $setupTest "$root\driver\PawnIO_setup.exe"
if ($LASTEXITCODE -ne 0) { throw 'Setup tests failed.' }
$accessTest = Join-Path $scratch 'HardwareAccessTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 `
    /reference:System.Management.dll "/out:$accessTest" "$root\EcReader.cs" "$PSScriptRoot\HardwareAccessTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Hardware access test compilation failed.' }
& $accessTest
if ($LASTEXITCODE -ne 0) { throw 'Hardware access tests failed.' }
