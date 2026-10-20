# ============================================================================
# RepoCheatMenu - инструменты сборки
# Copyright (c) 2026 Kolya. Все права защищены.
# Исходный код закрыт: без разрешения автора не копировать, не публиковать
# и не выдавать за свою работу. Подробности - LICENSE.txt.
# ============================================================================

# Builds the launcher folder: dist\RepoLauncher\
#   RepoLauncher.exe      single self-contained file, nothing to install
#   modpacks.json         which packs exist and what each installs (editable)
#   packages\             the files the packs install:
#       RepoCheatMenu\        mod loader + core + its libraries (built from this repo)
#       nickklmao-MenuLib\    copied from an installed r2modman profile
#       Vippy-REPOFidelity\   copied from an installed r2modman profile
#
#   powershell -File tools\make-launcher.ps1
#
# The two third-party packages are copied from mods you already have installed through r2modman;
# they belong to their authors, so this folder is for your own machine, not for redistribution.
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$out      = Join-Path $root 'dist\RepoLauncher'
$pkgRoot  = Join-Path $out 'packages'
$coreProj = Join-Path $root 'RepoCheatMenu\RepoCheatMenu.csproj'
$loadProj = Join-Path $root 'RepoCheatMenu.Loader\RepoCheatMenu.Loader.csproj'
$launProj = Join-Path $root 'Launcher\RepoLauncher.csproj'
$coreBin  = Join-Path $root 'RepoCheatMenu\bin\Release\netstandard2.1'
$loadBin  = Join-Path $root 'RepoCheatMenu.Loader\bin\Release\netstandard2.1'

function Invoke-Dotnet {
    param([string[]]$DotnetArgs)
    $output = & dotnet @DotnetArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Where-Object { $_ -match 'error' } | Select-Object -First 15 | ForEach-Object { Write-Host $_ }
        throw "dotnet $($DotnetArgs -join ' ') failed"
    }
}

Write-Host 'Building mod core + loader...'
Invoke-Dotnet @('build', $coreProj, '-c', 'Release')
Invoke-Dotnet @('build', $loadProj, '-c', 'Release')

# Renames everything in the core except the handful of names the mod reaches by string at
# runtime (scripting surface, cheat-toggle lookup) - see obfuscate.ps1/obfuscar.template.xml for
# why. Overwrites RepoCheatMenu\bin\Release\netstandard2.1\RepoCheatMenu.dll in place, so the
# copy-into-package step right below picks up the obfuscated version automatically.
& (Join-Path $PSScriptRoot 'obfuscate.ps1')

New-Item -ItemType Directory -Force $out | Out-Null

if (-not $SkipPublish) {
    Write-Host 'Publishing launcher (single file, self-contained)...'
    Invoke-Dotnet @('publish', $launProj, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', '-o', $out)
}

# --- RepoCheatMenu package: loader dll + core (the mod dll renamed to .core) + its libraries ---
$pkg = Join-Path $pkgRoot 'RepoCheatMenu'
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item (Join-Path $loadBin 'RepoCheatMenu.Loader.dll') $pkg
Copy-Item (Join-Path $coreBin 'RepoCheatMenu.dll') (Join-Path $pkg 'RepoCheatMenu.core')
Get-ChildItem $coreBin -Filter '*.dll' |
    Where-Object { $_.Name -ne 'RepoCheatMenu.dll' } |
    ForEach-Object { Copy-Item $_.FullName $pkg }
# Rockstar's UI font (SF Pro Display regular + medium), loaded at run time by CustomGui for the F6 menu / HUD.
$fontSrc = Join-Path $root 'RepoCheatMenu\fonts'
if (Test-Path $fontSrc) { Copy-Item $fontSrc (Join-Path $pkg 'fonts') -Recurse -Force }
Write-Host "package RepoCheatMenu: $((Get-ChildItem $pkg).Count) files"

# --- third-party packages, taken from an installed r2modman profile ---
$profiles = Join-Path $env:APPDATA 'r2modmanPlus-local\REPO\profiles'
foreach ($name in @('nickklmao-MenuLib', 'Vippy-REPOFidelity')) {
    $dest = Join-Path $pkgRoot $name
    $src = Get-ChildItem $profiles -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName "BepInEx\plugins\$name" } |
        Where-Object { Test-Path (Join-Path $_ '*.dll') } | Select-Object -First 1
    if (-not $src) {
        Write-Host "WARNING: $name not found in any r2modman profile - install it there once, then rerun."
        continue
    }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Get-ChildItem $src -File | Where-Object { $_.Extension -ne '.md' -and $_.Name -ne 'icon.png' } |
        ForEach-Object { Copy-Item $_.FullName $dest }
    Write-Host "package ${name}: from $src"
}

# --- modpacks.json: created by the exe on first run, version stamped from the mod ---
$exe = Join-Path $out 'RepoLauncher.exe'
$json = Join-Path $out 'modpacks.json'
if (-not (Test-Path $json)) { & $exe --demo --plain --root (Join-Path $env:TEMP 'RepoLauncher-init') | Out-Null }
$version = (Select-String -Path (Join-Path $root 'RepoCheatMenu\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
(Get-Content $json -Raw -Encoding UTF8) -replace '"version":\s*"[^"]*"', "`"version`": `"$version`"" |
    Set-Content $json -Encoding UTF8

Copy-Item (Join-Path $root 'LICENSE.txt') (Join-Path $out 'LICENSE.txt') -Force

Write-Host ''
Write-Host "Done: $out"
Write-Host "Run:  $exe"
