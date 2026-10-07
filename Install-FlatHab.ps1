#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or removes the Flat Hab mod (and BepInEx 5) for Hardspace: Shipbreaker.

.EXAMPLE
    .\Install-FlatHab.ps1
    Finds the game in your Steam libraries, installs BepInEx 5 if needed, and copies FlatHab.dll.

.EXAMPLE
    .\Install-FlatHab.ps1 -GameDir 'D:\Games\Hardspace Shipbreaker'

.EXAMPLE
    .\Install-FlatHab.ps1 -Uninstall
    Removes the mod. Add -RemoveBepInEx to remove BepInEx too (this also removes any other BepInEx mods).
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$PluginPath,
    [switch]$Uninstall,
    [switch]$RemoveBepInEx
)

$ErrorActionPreference = 'Stop'

$BepInExVersion = '5.4.23.5'
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$BepInExSha256 = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'

function Find-GameDir {
    $libraries = [System.Collections.Generic.List[string]]::new()
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $libraries.Add($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($match in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches).Matches) {
                $libraries.Add($match.Groups[1].Value.Replace('\\', '\'))
            }
        }
    }
    foreach ($library in $libraries | Select-Object -Unique) {
        $candidate = [IO.Path]::GetFullPath((Join-Path $library 'steamapps\common\Hardspace Shipbreaker'))
        if (Test-Path (Join-Path $candidate 'Shipbreaker.exe')) {
            return $candidate
        }
    }
    return $null
}

if (-not $GameDir) {
    $GameDir = Find-GameDir
    if (-not $GameDir) {
        throw 'Could not find Hardspace: Shipbreaker in your Steam libraries. Pass -GameDir "<game folder>".'
    }
}
if (-not (Test-Path (Join-Path $GameDir 'Shipbreaker.exe'))) {
    throw "Shipbreaker.exe not found in '$GameDir'."
}
if (Get-Process -Name Shipbreaker -ErrorAction SilentlyContinue) {
    throw 'Close Hardspace: Shipbreaker first.'
}
Write-Host "Game folder: $GameDir"

$pluginDir = Join-Path $GameDir 'BepInEx\plugins\FlatHab'

if ($Uninstall) {
    Remove-Item $pluginDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $GameDir 'BepInEx\config\structed.shipbreaker.flathab.cfg') -Force -ErrorAction SilentlyContinue
    Write-Host 'Flat Hab removed.'
    if ($RemoveBepInEx) {
        foreach ($item in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'BepInEx') {
            Remove-Item (Join-Path $GameDir $item) -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host 'BepInEx removed.'
    }
    return
}

if (-not $PluginPath) {
    $PluginPath = @(
        (Join-Path $PSScriptRoot 'FlatHab.dll'),
        (Join-Path $PSScriptRoot 'src\FlatHab\bin\Release\FlatHab.dll')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $PluginPath -or -not (Test-Path $PluginPath)) {
    throw 'FlatHab.dll not found. Put it next to this script, build it (see README), or pass -PluginPath.'
}

if (-not (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll'))) {
    $zip = Join-Path ([IO.Path]::GetTempPath()) "BepInEx_win_x64_$BepInExVersion.zip"
    Write-Host "Downloading BepInEx $BepInExVersion..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
    try {
        if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $BepInExSha256) {
            throw 'BepInEx download failed its checksum check.'
        }
        Expand-Archive -Path $zip -DestinationPath $GameDir -Force
    }
    finally {
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
    }
    Write-Host "BepInEx $BepInExVersion installed."
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $PluginPath $pluginDir -Force
Write-Host "Flat Hab installed to '$pluginDir'."
