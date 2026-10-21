param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& $Dotnet publish (Join-Path $PSScriptRoot 'UltraBootscreenEditor.csproj') -c Release -r win-x64 -o (Join-Path $PSScriptRoot 'release') --nologo
if ($LASTEXITCODE) { throw 'EXE build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'release\README.md')
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'release\UltraBootscreenEditor.exe') | Select-Object FullName, Length
