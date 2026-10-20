# ============================================================================
# RepoCheatMenu - инструменты сборки
# Copyright (c) 2026 Kolya. Все права защищены.
# Исходный код закрыт: без разрешения автора не копировать, не публиковать
# и не выдавать за свою работу. Подробности - LICENSE.txt.
# ============================================================================

# Deploys RepoCheatMenu. With the hot-reload loader installed, a running game picks the new build up
# by itself (no restart); the script waits for the loader's own verdict and prints it.
#
#   powershell -File tools\deploy.ps1            build + deploy (hot if the game is running)
#   powershell -File tools\deploy.ps1 -NoBuild   deploy what is already built
#   powershell -File tools\deploy.ps1 -Legacy    go back to the plain single-DLL install (game closed)
#
# First install of the loader needs the game CLOSED (it is the one file that cannot be hot-swapped).
# After that every deploy is hot. Exit codes: 0 ok, 1 build/deploy/reload failed, 2 nothing deployed
# because the game is running and the loader is not installed yet.
param(
    [switch]$NoBuild,
    [switch]$Legacy,
    [int]$WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$coreProj  = Join-Path $root 'RepoCheatMenu\RepoCheatMenu.csproj'
$loadProj  = Join-Path $root 'RepoCheatMenu.Loader\RepoCheatMenu.Loader.csproj'
$coreDll   = Join-Path $root 'RepoCheatMenu\bin\Release\netstandard2.1\RepoCheatMenu.dll'
$loaderDll = Join-Path $root 'RepoCheatMenu.Loader\bin\Release\netstandard2.1\RepoCheatMenu.Loader.dll'
$backupDir = Join-Path $root 'dist\legacy-plugin'

# Both copies, as before: the r2modman profile is what actually loads, the game folder is what the
# old Install .bat updates and what loads if doorstop_config.ini ever rolls back.
$targets = @(
    (Join-Path $env:APPDATA 'r2modmanPlus-local\REPO\profiles\for cheat\BepInEx\plugins\local-RepoCheatMenu'),
    'C:\Program Files (x86)\Steam\steamapps\common\REPO\BepInEx\plugins\local-RepoCheatMenu'
)

# RepoLauncher profiles (one per modpack) always run through the hot-reload loader, so they are hot
# targets too - whichever one the game was started from picks the new build up.
$launcherProfiles = Join-Path $env:LOCALAPPDATA 'RepoLauncher\profiles'
if (Test-Path $launcherProfiles) {
    $targets += Get-ChildItem $launcherProfiles -Directory |
        ForEach-Object { Join-Path $_.FullName 'BepInEx\plugins\local-RepoCheatMenu' } |
        Where-Object { Test-Path $_ }
}

# The launcher re-installs from its package store whenever a pack is applied; if the store kept an
# older core it would overwrite what was just deployed. Keep the store in step.
$launcherStore = Join-Path $root 'dist\RepoLauncher\packages\RepoCheatMenu'

function Build-Project($proj) {
    $out = & dotnet build $proj -c Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Where-Object { $_ -match 'error' } | Select-Object -First 15 | ForEach-Object { Write-Host $_ }
        throw "Build failed: $proj"
    }
}

function Read-Status($dir) {
    $p = Join-Path $dir 'hot-status.json'
    if (-not (Test-Path $p)) { return $null }
    try { return Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
}

function Same-File($a, $b) {
    (Test-Path $a) -and (Test-Path $b) -and ((Get-FileHash $a -Algorithm MD5).Hash -eq (Get-FileHash $b -Algorithm MD5).Hash)
}

# Written next to the target and renamed over it, so the loader can never observe a half-copied file.
function Publish-Atomic($src, $dest) {
    $tmp = "$dest.tmp"
    Copy-Item $src $tmp -Force
    Move-Item $tmp $dest -Force
}

$gameRunning = [bool](Get-Process -Name 'REPO' -ErrorAction SilentlyContinue)

if ($Legacy) {
    if ($gameRunning) { Write-Host 'Game is running - close it before -Legacy.'; exit 2 }
    if (-not $NoBuild) { Build-Project $coreProj }
    foreach ($t in $targets) {
        if (-not (Test-Path $t)) { continue }
        Remove-Item (Join-Path $t 'RepoCheatMenu.Loader.dll'), (Join-Path $t 'RepoCheatMenu.core'), (Join-Path $t 'hot-status.json') -ErrorAction SilentlyContinue
        Copy-Item $coreDll (Join-Path $t 'RepoCheatMenu.dll') -Force
        Write-Host "legacy install -> $t"
    }
    exit 0
}

if (-not $NoBuild) {
    Write-Host 'Building...'
    Build-Project $coreProj
    Build-Project $loadProj
}
foreach ($f in @($coreDll, $loaderDll)) {
    if (-not (Test-Path $f)) { throw "Missing build output: $f" }
}

$hotTargets = @()   # targets that had the loader and got a new core while possibly live
$before = @{}
$installed = 0
$skipped = 0

foreach ($t in $targets) {
    if (-not (Test-Path $t)) { Write-Host "skip (no folder): $t"; continue }

    $loaderDest = Join-Path $t 'RepoCheatMenu.Loader.dll'
    $legacyDll  = Join-Path $t 'RepoCheatMenu.dll'
    $coreDest   = Join-Path $t 'RepoCheatMenu.core'
    $hasLoader  = Test-Path $loaderDest

    if (-not $hasLoader) {
        if ($gameRunning) {
            Write-Host "SKIP $t"
            Write-Host '  first install of the hot-reload loader needs the game closed.'
            $skipped++
            continue
        }
        if (Test-Path $legacyDll) {
            New-Item -ItemType Directory -Force $backupDir | Out-Null
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            Copy-Item $legacyDll (Join-Path $backupDir "RepoCheatMenu-$stamp.dll") -Force
            Remove-Item $legacyDll -Force   # a leftover plain plugin would start the mod a second time
        }
        Copy-Item $loaderDll $loaderDest -Force
        Publish-Atomic $coreDll $coreDest
        Write-Host "INSTALLED loader + core -> $t"
        $installed++
        continue
    }

    if (-not (Same-File $loaderDll $loaderDest)) {
        if ($gameRunning) { Write-Host "NOTE $t : the loader itself changed - it only updates with the game closed; core is still deployed." }
        else { Copy-Item $loaderDll $loaderDest -Force; Write-Host "updated loader -> $t" }
    }

    $before[$t] = Read-Status $t
    Publish-Atomic $coreDll $coreDest
    Write-Host "core -> $t"
    $hotTargets += $t
}

if (Test-Path $launcherStore) {
    Publish-Atomic $coreDll (Join-Path $launcherStore 'RepoCheatMenu.core')
    Write-Host "launcher store core -> $launcherStore"
}

if ($installed -gt 0 -and -not $gameRunning) {
    Write-Host ''
    Write-Host 'Loader installed. Start the game once; from now on deploys are hot (no restart).'
}

if ($skipped -gt 0 -and $hotTargets.Count -eq 0 -and $installed -eq 0) { exit 2 }

if ($gameRunning -and $hotTargets.Count -gt 0) {
    Write-Host ''
    Write-Host "Game is running - waiting up to $WaitSeconds s for the loader to hot-swap..."
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $last = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        foreach ($t in $hotTargets) {
            $now = Read-Status $t
            if ($null -eq $now) { continue }
            $was = $before[$t]
            if ($null -ne $was -and $now.time -eq $was.time) { continue }   # loader has not reported yet

            $line = "[$($now.state)] $($now.message) (core $($now.coreVersion), reloads: $($now.reloadCount))"
            if ($line -ne $last) { Write-Host $line; $last = $line }

            switch ($now.state) {
                'reloaded'    { Write-Host 'OK: the running game now has the new build.'; exit 0 }
                'rolled-back' { Write-Host 'FAILED: the new build did not start; the previous one is running again.'; exit 1 }
                'dead'        { Write-Host 'FAILED: neither build started - restart the game.'; exit 1 }
                'failed'      { Write-Host 'FAILED: the loader rejected the new build; the old one keeps running.'; exit 1 }
                'conflict'    { Write-Host 'FAILED: the mod is installed twice - see message above.'; exit 1 }
            }
        }
    }
    Write-Host 'No answer from the loader. Possible reasons: the game runs from the other folder (then this is fine),'
    Write-Host 'the loader was never started in this session (it was installed after the game launched), or the swap is still waiting for a safe moment.'
    exit 1
}

Write-Host 'Done.'
exit 0
