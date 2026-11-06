# Pass every argument directly to the native launcher, preserving spaces and Unicode.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'launcher/RabbitEars.Desktop.exe') @args
exit $LASTEXITCODE
