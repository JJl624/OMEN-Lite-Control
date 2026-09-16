# Compatibility entry point; the application no longer needs the NVIDIA SDK.
& (Join-Path (Split-Path -Parent $PSScriptRoot) 'build.ps1')
