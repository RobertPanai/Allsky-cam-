# Compila AllSkyBrain.exe (autonomo, win-x64) sul tuo PC. Richiede .NET 10 SDK.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet publish src/AllSky.Tray/AllSky.Tray.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
Write-Host "Fatto: publish\AllSkyBrain.exe"
