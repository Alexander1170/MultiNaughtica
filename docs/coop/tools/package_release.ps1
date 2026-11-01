<#
.SYNOPSIS
    Builds the Release of the co-op fork and zips the output folder of the launcher (its contents at the root of the zip). Nothing is uploaded, pushed or
    distributed: the zip lands in -OutDir (outside the repository).

.DESCRIPTION
    The zip holds the output of `dotnet build Nitrox.Launcher/Nitrox.Launcher.csproj -c Release -r win-x64`: the launcher and the server (exe, dll, deps,
    runtimeconfig) at the root, Resources\, and lib\ with their dependencies and the game-side libraries in lib\net472 (the launcher copies
    lib\net472\NitroxPatcher.dll into the game). `dotnet publish` gives a flat layout without lib\net472 and must not be used (the build refuses it).
    The build is framework-dependent: whoever runs it needs the .NET 10 runtime and the ASP.NET Core 10 runtime installed (INSTALL.md says how).
    The repository's workflows publish no release zip, so there is no upstream zip to be identical to; this is the layout of the launcher's own output folder.

    Steps: refuse a dirty tree (unless -AllowDirty; untracked source files count, untracked docs do not), build, check that the folder is complete and
    really is a Release build, add the notices, INSTALL.md, PLAYER_MODELS.md and VERSION.txt, zip with forward slashes in the entry names, write a .sha256 next to the zip.

.EXAMPLE
    .\package_release.ps1                 # build and zip into a folder "release" next to the repository
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$OutDir = (Join-Path (Split-Path -Parent $RepoRoot) 'release'),
    [string]$Runtime = 'win-x64',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
function Say([string]$msg) { Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $msg) }

# ---- where we are
$props = [xml](Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Raw)
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$coopBuild = ($props.Project.PropertyGroup | Where-Object { $_.CoopBuild } | Select-Object -First 1).CoopBuild
if (-not $version -or -not $coopBuild) { throw 'Version / CoopBuild not found in Directory.Build.props' }
$coopVersion = "$version-coop.$coopBuild"

Push-Location $RepoRoot
try {
    $commit = (git rev-parse HEAD).Trim()
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    # untracked source files are compiled by the SDK's default globbing, so they count; untracked documents and run folders under docs/ do not
    $dirty = @(git status --porcelain --untracked-files=all | Where-Object { $_ -notmatch '^\?\? docs/' })
}
finally { Pop-Location }
if ($dirty.Count -gt 0 -and -not $AllowDirty) {
    throw "The working tree has $($dirty.Count) uncommitted change(s); commit them first or use -AllowDirty (the zip would not match any commit)`n$($dirty -join "`n")"
}
Say "Packaging $coopVersion from $branch @ $($commit.Substring(0, 8))$(if ($dirty.Count) { ' (DIRTY)' })"

# ---- build
$project = Join-Path $RepoRoot 'Nitrox.Launcher\Nitrox.Launcher.csproj'
$outputDir = Join-Path $RepoRoot "Nitrox.Launcher\bin\Release\net10.0\$Runtime"
Say 'dotnet build -c Release (incremental, up to a few minutes)'
dotnet build $project -c Release -r $Runtime -m:1 --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
if (-not (Test-Path -LiteralPath $outputDir)) { throw "No build output at $outputDir" }

# ---- stage
$staging = Join-Path $OutDir "staging\MultiNaughtica_${coopVersion}_$Runtime"
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Say "Copying $outputDir"
Copy-Item -Path (Join-Path $outputDir '*') -Destination $staging -Recurse -Force

# ---- the folder has to be complete
$required = @(
    'Nitrox.Launcher.exe', 'Nitrox.Launcher.dll', 'Nitrox.Launcher.runtimeconfig.json',
    'Nitrox.Server.Subnautica.exe', 'Nitrox.Server.Subnautica.dll', 'Nitrox.Server.Subnautica.runtimeconfig.json',
    'Resources', 'lib',
    'lib\net472\NitroxPatcher.dll', 'lib\net472\NitroxClient.dll', 'lib\net472\Nitrox.Model.dll', 'lib\net472\Nitrox.Model.Subnautica.dll',
    'lib\net472\0Harmony.dll', 'lib\net472\LiteNetLib.dll', 'lib\net472\BinaryPack.dll'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $staging $_)) })
if ($missing.Count -gt 0) { throw "The build output is incomplete, missing: $($missing -join ', ')" }

# ---- and has to be a Release build (the Debug client carries the harness and a different packet layer, they cannot talk to a Release server)
# (in a job of its own: a reflection-only load keeps the file locked until the process ends)
$checked = 'lib\net472\NitroxClient.dll', 'lib\net472\NitroxPatcher.dll', 'lib\net472\Nitrox.Model.dll', 'lib\net472\Nitrox.Model.Subnautica.dll'
$debugBuilds = Start-Job -ArgumentList (,@($checked | ForEach-Object { Join-Path $staging $_ })) -ScriptBlock {
    param($paths)
    foreach ($path in $paths) {
        $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($path)
        foreach ($attr in [System.Reflection.CustomAttributeData]::GetCustomAttributes($asm) | Where-Object { $_.AttributeType.Name -eq 'DebuggableAttribute' }) {
            if (([int]$attr.ConstructorArguments[0].Value -band 256) -ne 0) { $path }
        }
    }
} | Receive-Job -Wait -AutoRemoveJob
if ($debugBuilds) { throw "built with optimizations disabled (Debug), build with -c Release: $($debugBuilds -join ', ')" }

# the Debug server build writes server.Development.json (SaveInterval=0, no port forwarding): it must never ship
$development = @(Get-ChildItem -LiteralPath $staging -Recurse -File -Filter '*.Development.json')
if ($development.Count -gt 0) { throw "Debug leftovers in the build output: $($development.FullName -join ', ')" }
Say 'The folder is complete and the game-side libraries are Release builds'

# ---- notes and the one-click installer inside the zip
$install = Join-Path $RepoRoot 'docs\coop\INSTALL.md'
if (-not (Test-Path -LiteralPath $install)) { throw "docs\coop\INSTALL.md is missing" }
Copy-Item -LiteralPath $install -Destination (Join-Path $staging 'INSTALL.md')
# Install.cmd / install.ps1 (the friend double-clicks Install.cmd), Uninstall.cmd / uninstall.ps1: at the root of the zip. They have to parse, or the friend's first click fails.
$installerSource = Join-Path $RepoRoot 'docs\coop\installer'
foreach ($name in 'Install.cmd', 'install.ps1', 'Uninstall.cmd', 'uninstall.ps1') {
    $source = Join-Path $installerSource $name
    if (-not (Test-Path -LiteralPath $source)) { throw "docs\coop\installer\$name is missing" }
    if ($name -like '*.ps1') {
        $parseErrors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$parseErrors)
        if ($parseErrors) { throw "$name does not parse: $($parseErrors[0].Message) (line $($parseErrors[0].Extent.StartLineNumber))" }
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging $name)
}
# the notices that go with the program (GPL v3): the licence, the notice of the changes and the readme; and the guide for player models
foreach ($name in 'LICENSE.txt', 'NOTICE.md', 'README.md') {
    $source = Join-Path $RepoRoot $name
    if (-not (Test-Path -LiteralPath $source)) { throw "$name is missing in the repository" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging $name)
}
Copy-Item -LiteralPath (Join-Path $RepoRoot 'docs\coop\PLAYER_MODELS.md') -Destination (Join-Path $staging 'PLAYER_MODELS.md')
# no player model may ship: players add their own
$models = @(Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Extension -in '.glb', '.gltf' })
if ($models.Count -gt 0) { throw "Player models in the build output: $($models.FullName -join ', ')" }
Say 'Added the notices, INSTALL.md, PLAYER_MODELS.md and the installer files (Install.cmd, install.ps1, Uninstall.cmd, uninstall.ps1)'
$info = @(
    "MultiNaughtica (a Nitrox fork for Subnautica), $coopVersion",
    "Built from $branch @ $commit$(if ($dirty.Count) { ' (with uncommitted changes)' })",
    "Packed on $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    '',
    'Everybody who plays together has to use the same zip (the same line above): the game refuses a server of another build and says so.',
    'Unzip it, then double-click Install.cmd (INSTALL.md says the rest).'
)
Set-Content -LiteralPath (Join-Path $staging 'VERSION.txt') -Value $info -Encoding UTF8

# ---- zip, upstream layout: the contents of the folder at the root, forward slashes in the entry names
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zipPath = Join-Path $OutDir "MultiNaughtica_${coopVersion}_$($Runtime -replace '-', '_').zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Say "Zipping to $zipPath"
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $prefix = $staging.TrimEnd('\') + '\'
    $count = 0
    foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File) {
        $entryName = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $count++
    }
}
finally { $zip.Dispose() }

# the staging copy has served its purpose: the zip is the deliverable, a second copy of the folder next to it only confuses
Remove-Item -LiteralPath (Join-Path $OutDir 'staging') -Recurse -Force -ErrorAction SilentlyContinue

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding ASCII
$sizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
Say "Done: $zipPath ($sizeMb MB, $count files)"
Say "sha256 $hash"
[pscustomobject]@{ Zip = $zipPath; SizeMB = $sizeMb; Files = $count; Sha256 = $hash; CoopVersion = $coopVersion; Commit = $commit }
