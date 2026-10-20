# ============================================================================
# RepoCheatMenu - инструменты сборки
# Copyright (c) 2026 Kolya. Все права защищены.
# Исходный код закрыт: без разрешения автора не копировать, не публиковать
# и не выдавать за свою работу. Подробности - LICENSE.txt.
# ============================================================================

# One-button build: bumps the mod's version, builds core + loader + launcher (via make-launcher.ps1),
# and drops the finished launcher folder onto the Desktop. Any failure is written to tools\build-log.txt
# instead of just vanishing when the console window closes - the version is only kept bumped if the
# build actually succeeded, so a failed attempt doesn't burn a version number for nothing.
#
#   powershell -File tools\build-all.ps1
#
# Double-click "Собрать и обновить.bat" in the repo root to run this without opening a terminal.
param(
    [switch]$NoBump   # keep the current version instead of incrementing it (useful for a quick retry)
)

$ErrorActionPreference = 'Stop'

$root       = Split-Path -Parent $PSScriptRoot
$pluginCs   = Join-Path $root 'RepoCheatMenu\Plugin.cs'
$launcherOut = Join-Path $root 'dist\RepoLauncher'
$logFile    = Join-Path $PSScriptRoot 'build-log.txt'
$desktop    = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RepoLauncher'

function Write-Log($text) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    "`n===== $stamp =====`n$text`n" | Add-Content -Path $logFile -Encoding UTF8
}

function Get-Version {
    $m = Select-String -Path $pluginCs -Pattern 'Version = "([^"]+)"'
    if (-not $m) { throw "Не нашёл строку версии в $pluginCs" }
    return $m.Matches[0].Groups[1].Value
}

# Bumps the last dot-separated number (1.0.37 -> 1.0.38). Returns the new version string.
function Step-Version([string]$current) {
    $parts = $current.Split('.')
    $parts[-1] = [string]([int]$parts[-1] + 1)
    return [string]::Join('.', $parts)
}

function Set-Version([string]$new) {
    $text = Get-Content $pluginCs -Raw -Encoding UTF8
    $updated = $text -replace 'Version = "[^"]+"', "Version = `"$new`""
    Set-Content $pluginCs -Value $updated -Encoding UTF8 -NoNewline
}

# Updates the Desktop copy WITHOUT ever deleting it first - a plain "wipe then copy" would also
# destroy any extra mod packages the user drops straight into the Desktop folder's packages\
# themselves (outside the repo's own dist\RepoLauncher), which is exactly what happened once
# already. Instead this only overwrites the specific files/folders this build actually owns -
# the exe and the three packages this repo's own make-launcher.ps1 manages - and merges modpacks.
# json's version numbers into whatever is already on the Desktop rather than replacing the file,
# so hand-added packs/packages entries in it survive too.
function Publish-ToDesktop([string]$src, [string]$dest) {
    New-Item -ItemType Directory -Force $dest | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $dest 'packages') | Out-Null

    Copy-Item (Join-Path $src 'RepoLauncher.exe') (Join-Path $dest 'RepoLauncher.exe') -Force
    $licenseSrc = Join-Path $src 'LICENSE.txt'
    if (Test-Path $licenseSrc) { Copy-Item $licenseSrc (Join-Path $dest 'LICENSE.txt') -Force }

    foreach ($pkg in @('RepoCheatMenu', 'nickklmao-MenuLib', 'Vippy-REPOFidelity')) {
        $pkgSrc = Join-Path $src "packages\$pkg"
        if (-not (Test-Path $pkgSrc)) { continue }
        $pkgDest = Join-Path $dest "packages\$pkg"
        if (Test-Path $pkgDest) { Remove-Item $pkgDest -Recurse -Force }   # this one folder is fully build-controlled
        Copy-Item $pkgSrc $pkgDest -Recurse -Force
    }

    $destJson = Join-Path $dest 'modpacks.json'
    if (Test-Path $destJson) {
        # Already has a modpacks.json on the Desktop (possibly hand-edited with extra packs/
        # packages) - patch its version fields in place instead of overwriting it.
        (Get-Content $destJson -Raw -Encoding UTF8) -replace '"version":\s*"[^"]*"', "`"version`": `"$newVersion`"" |
            Set-Content $destJson -Encoding UTF8
    } else {
        Copy-Item (Join-Path $src 'modpacks.json') $destJson -Force
    }
}

$oldVersion = Get-Version
$newVersion = if ($NoBump) { $oldVersion } else { Step-Version $oldVersion }

Write-Host '================================================'
Write-Host "  RepoCheatMenu - сборка одним кликом"
Write-Host "  версия: $oldVersion -> $newVersion"
Write-Host '================================================'
Write-Host ''

if (-not $NoBump) { Set-Version $newVersion }

# Everything from here runs under one transcript so a failure's full dotnet output lands in the
# log file, not just the one-line summary PowerShell would otherwise throw.
$transcript = Join-Path $PSScriptRoot '._build-transcript.tmp.txt'
Remove-Item $transcript -ErrorAction SilentlyContinue
Start-Transcript -Path $transcript -Force | Out-Null

try {
    & (Join-Path $PSScriptRoot 'make-launcher.ps1')

    if (-not (Test-Path $launcherOut)) { throw "Сборка не создала $launcherOut" }

    Stop-Transcript | Out-Null
    Remove-Item $transcript -ErrorAction SilentlyContinue

    Write-Host ''
    Write-Host "Копирую готовый лоадер на рабочий стол..."
    Publish-ToDesktop $launcherOut $desktop
    "Версия: $newVersion`nСобрано: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" |
        Set-Content (Join-Path $desktop 'ВЕРСИЯ.txt') -Encoding UTF8

    Write-Host ''
    Write-Host '================================================'
    Write-Host "  Готово! Версия $newVersion"
    Write-Host "  $desktop"
    Write-Host '================================================'
    exit 0
}
catch {
    Stop-Transcript | Out-Null
    $details = if (Test-Path $transcript) { Get-Content $transcript -Raw -Encoding UTF8 } else { '' }
    Write-Log "СБОРКА ПРОВАЛИЛАСЬ (версия откатена $newVersion -> $oldVersion)`n$($_.Exception.Message)`n--- подробности ---`n$details"
    Remove-Item $transcript -ErrorAction SilentlyContinue

    # A failed build keeps the old version - no point burning a version number on a build that
    # never shipped.
    if (-not $NoBump) { Set-Version $oldVersion }

    Write-Host ''
    Write-Host '================================================'
    Write-Host '  [!] СБОРКА НЕ УДАЛАСЬ'
    Write-Host "  Подробности: $logFile"
    Write-Host '================================================'
    exit 1
}
