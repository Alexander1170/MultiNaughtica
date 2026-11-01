<#
.SYNOPSIS
    Removes the Nitrox co-op build that install.ps1 installed: the program files, the shortcuts, optionally the settings and the worlds.

.DESCRIPTION
    Removes only the files that the installer put there (the list is in installed-files.txt next to the program), one by one, then the folders that became empty. Anything else in the
    folder (a file you put there yourself) stays. Your worlds (%APPDATA%\Nitrox\saves) are kept unless you ask for them to go (-RemoveSaves, and you have to type a word to confirm).

.PARAMETER InstallDir    The install folder (default: the folder this script is in, else %LOCALAPPDATA%\NitroxCoop).
.PARAMETER TestRoot      Test mode: works on the test install of install.ps1 -TestRoot and touches nothing else.
.PARAMETER DryRun        Only report.
.PARAMETER Yes           Do not ask for confirmation (never deletes worlds without -RemoveSaves).
.PARAMETER RemoveSettings  Also forget the game folder the launcher was told (HKCU\SOFTWARE\Nitrox\PreferredGamePath) and the server list.
.PARAMETER RemoveSaves   Also delete your worlds (%APPDATA%\Nitrox\saves). Asks you to type "delete".
#>
[CmdletBinding()]
param(
    [string]$InstallDir = '',
    [string]$TestRoot = '',
    [switch]$DryRun,
    [switch]$Yes,
    [switch]$RemoveSettings,
    [switch]$RemoveSaves
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Say([string]$text, [string]$color = '') { if ($color) { Write-Host $text -ForegroundColor $color } else { Write-Host $text } }
function Say-Dry([string]$text) { Write-Host ("  [dry run] would: " + $text) -ForegroundColor DarkGray }

function Remove-EmptyFolders([string]$root) {
    # deepest first; only a folder with nothing in it is removed
    if (-not (Test-Path -LiteralPath $root)) { return }
    $folders = @(Get-ChildItem -LiteralPath $root -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending)
    foreach ($folder in $folders) {
        if (-not (Get-ChildItem -LiteralPath $folder.FullName -Force | Select-Object -First 1)) { [System.IO.Directory]::Delete($folder.FullName, $false) }
    }
    if (-not (Get-ChildItem -LiteralPath $root -Force | Select-Object -First 1)) { [System.IO.Directory]::Delete($root, $false) }
}

function Remove-FilesOf([string]$root) {
    # every file below the folder, one by one (used for the worlds, after the explicit confirmation)
    if (-not (Test-Path -LiteralPath $root)) { return 0 }
    $count = 0
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) { Remove-Item -LiteralPath $file.FullName -Force; $count++ }
    Remove-EmptyFolders $root
    return $count
}

try {
    Say ''
    Say '=== Nitrox Co-op uninstaller ===' 'Cyan'
    if ($TestRoot) { Say "TEST MODE: works on the test install in $TestRoot only." 'Yellow' }
    if ($DryRun) { Say 'DRY RUN: nothing will be changed.' 'Yellow' }

    if (-not $InstallDir) {
        if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'installed-files.txt')) { $InstallDir = $PSScriptRoot }
        elseif ($TestRoot) { $InstallDir = Join-Path $TestRoot 'NitroxCoop' }
        else { $InstallDir = Join-Path $env:LOCALAPPDATA 'NitroxCoop' }
    }
    $InstallDir = [System.IO.Path]::GetFullPath($InstallDir.Trim('"'))
    $manifest = Join-Path $InstallDir 'installed-files.txt'
    if (-not (Test-Path -LiteralPath $manifest)) { throw "Nothing to remove: $InstallDir has no installed-files.txt (it was not installed by this installer, or it is gone already)." }

    $running = @()
    foreach ($process in Get-Process -Name 'Nitrox.Launcher', 'Nitrox.Server.Subnautica' -ErrorAction SilentlyContinue) {
        try { if ($process.MainModule.FileName -like ($InstallDir.TrimEnd('\') + '\*')) { $running += $process.ProcessName } } catch { }
    }
    if ($running.Count -gt 0) { throw "Close the program first ($($running -join ', ') is running from $InstallDir)." }

    $files = @(Get-Content -LiteralPath $manifest | Where-Object { $_ })
    Say "Folder: $InstallDir ($($files.Count) program files)"
    if (-not $Yes -and -not $DryRun) {
        $answer = (Read-Host '  Remove Nitrox Co-op from this PC? Your worlds are kept. [y/N]').Trim().ToLowerInvariant()
        if ($answer -notin @('y', 'yes', 'j', 'ja')) { Say 'Nothing was changed.'; exit 0 }
    }

    # shortcuts
    if ($TestRoot) { $desktop = Join-Path $TestRoot 'Shortcuts\Desktop'; $menu = Join-Path $TestRoot 'Shortcuts\StartMenu' }
    else { $desktop = [Environment]::GetFolderPath('Desktop'); $menu = [Environment]::GetFolderPath('Programs') }
    foreach ($link in @((Join-Path $desktop 'Nitrox Co-op.lnk'), (Join-Path $menu 'Nitrox Co-op.lnk'), (Join-Path $menu 'Uninstall Nitrox Co-op.lnk'))) {
        if (Test-Path -LiteralPath $link) {
            if ($DryRun) { Say-Dry "remove the shortcut $link" } else { Remove-Item -LiteralPath $link -Force; Say "  Removed shortcut: $link" }
        }
    }

    # program files, one by one. The two files that run this uninstaller (Uninstall.cmd waits for it and Windows opens it again when it returns) are removed a few seconds later,
    # by a hidden process, after the uninstaller has ended.
    $removed = 0
    $deferred = New-Object System.Collections.Generic.List[string]
    foreach ($relative in $files) {
        $full = Join-Path $InstallDir $relative
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            if ($relative -ieq 'Uninstall.cmd' -or $relative -ieq 'uninstall.ps1') { $deferred.Add($full); $removed++; continue }
            if ($DryRun) { $removed++ } else { Remove-Item -LiteralPath $full -Force; $removed++ }
        }
    }
    if ($DryRun) { Say-Dry "remove $removed program files, the list of them and the install log, then the folders that become empty" }
    else {
        foreach ($extra in @('installed-files.txt', 'install.log')) {
            $path = Join-Path $InstallDir $extra
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        Remove-EmptyFolders $InstallDir
        Say "  Removed $removed program files." 'Green'
        if ($deferred.Count -gt 0) {
            $list = ($deferred | ForEach-Object { "'" + ($_ -replace "'", "''") + "'" }) -join ','
            $folder = "'" + ($InstallDir -replace "'", "''") + "'"
            $cleanup = "Start-Sleep -Seconds 4; foreach (`$f in @($list)) { Remove-Item -LiteralPath `$f -Force -ErrorAction SilentlyContinue }; try { if (-not (Get-ChildItem -LiteralPath $folder -Force | Select-Object -First 1)) { [System.IO.Directory]::Delete($folder, `$false) } } catch { }"
            Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-Command', $cleanup -WindowStyle Hidden | Out-Null
            Say '  The uninstaller itself is removed in a few seconds.'
        }
        elseif (Test-Path -LiteralPath $InstallDir) { Say "  The folder $InstallDir still holds files that are not part of the program (left alone)." 'Yellow' }
    }

    # settings
    if ($RemoveSettings) {
        if ($TestRoot) {
            $settings = Join-Path $TestRoot 'Settings'
            if ($DryRun) { Say-Dry "remove the test settings in $settings" } else { $null = Remove-FilesOf $settings; Say '  Removed the test settings.' }
        }
        else {
            if ($DryRun) { Say-Dry 'forget the game folder (HKCU\SOFTWARE\Nitrox\PreferredGamePath) and delete the server list (%APPDATA%\Nitrox\servers)' }
            else {
                try { Remove-ItemProperty -LiteralPath 'HKCU:\SOFTWARE\Nitrox' -Name PreferredGamePath -ErrorAction Stop; Say '  Forgot the game folder.' } catch { }
                $servers = Join-Path $env:APPDATA 'Nitrox\servers'
                if (Test-Path -LiteralPath $servers) { Remove-Item -LiteralPath $servers -Force; Say '  Removed the server list.' }
            }
        }
    }
    else { Say '  Your settings (game folder, server list) were kept.' }

    # worlds
    if ($RemoveSaves) {
        $saves = if ($TestRoot) { Join-Path $TestRoot 'Saves' } else { Join-Path $env:APPDATA 'Nitrox\saves' }
        if (-not (Test-Path -LiteralPath $saves)) { Say '  There are no worlds to delete.' }
        elseif ($DryRun) { Say-Dry "delete ALL worlds in $saves" }
        else {
            Say "  This deletes ALL your worlds in $saves and cannot be undone." 'Red'
            $confirm = Read-Host '  Type the word delete to do it, anything else keeps them'
            if ($confirm.Trim().ToLowerInvariant() -eq 'delete') { $count = Remove-FilesOf $saves; Say "  Deleted the worlds ($count files)." 'Yellow' }
            else { Say '  Worlds kept.' }
        }
    }
    else { Say '  Your worlds were kept.' }

    Say ''
    Say 'Done.' 'Green'
    exit 0
}
catch {
    Say ''
    Say ('Something went wrong: ' + $_.Exception.Message) 'Red'
    exit 1
}
