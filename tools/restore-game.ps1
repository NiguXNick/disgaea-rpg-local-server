<#
.SYNOPSIS
  Undoes setup-game.ps1 and restores the master data files the server upgraded.
#>
param([string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\DISGAEA RPG")
$ErrorActionPreference = "Stop"

$ini = Join-Path $GameDir "DISGAEA RPG_Data\StreamingAssets\settings\config_server.ini"
if (Test-Path "$ini.original") {
    Copy-Item "$ini.original" $ini -Force
    Remove-Item "$ini.original"
    Write-Host "config_server.ini original restaurado."
} else {
    Write-Host "Nenhum backup de config_server.ini encontrado."
}

$master = Join-Path $env:USERPROFILE "AppData\LocalLow\Boltrend\DISGAEA RPG\Boltrend\XDMaster"
$baks = Get-ChildItem $master -Filter "*.bin.bak" -ErrorAction SilentlyContinue
foreach ($b in $baks) {
    Copy-Item $b.FullName ($b.FullName -replace '\.bak$', '') -Force
    Remove-Item $b.FullName
}
Write-Host "$(@($baks).Count) arquivos de master data restaurados."
