<#
.SYNOPSIS
  Points DISGAEA RPG (Steam) at the local server. The original config is kept as
  config_server.ini.original; run restore-game.ps1 to undo.
#>
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\DISGAEA RPG",
    [int]$Port = 8765
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\XdCrypt.ps1"

$ini = Join-Path $GameDir "DISGAEA RPG_Data\StreamingAssets\settings\config_server.ini"
if (-not (Test-Path $ini)) { throw "config_server.ini not found. Pass -GameDir with the game folder." }

$backup = "$ini.original"
if (-not (Test-Path $backup)) {
    Copy-Item $ini $backup
    Write-Host "Backup created: $backup"
}

# Keep server_key/build from the original so the client finds its server entry.
$original = ConvertFrom-XdSettings ([IO.File]::ReadAllBytes($backup))
$keep = ($original -split "`n" | Where-Object { $_ -match '^(server_key|build)=' }) -join "`n"
$text = "server_url=http://127.0.0.1:$Port/Server`n$keep`n"
[IO.File]::WriteAllBytes($ini, (ConvertTo-XdSettings $text))
Write-Host "Game redirected to http://127.0.0.1:$Port"
