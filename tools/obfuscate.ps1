# ============================================================================
# RepoCheatMenu - инструменты сборки
# Copyright (c) 2026 Kolya. Все права защищены.
# Исходный код закрыт: без разрешения автора не копировать, не публиковать
# и не выдавать за свою работу. Подробности - LICENSE.txt.
# ============================================================================

# Obfuscates RepoCheatMenu.dll in place (RepoCheatMenu\bin\Release\netstandard2.1\RepoCheatMenu.dll)
# after a Release build - renames everything except ScriptGlobals/CheatState/Plugin, which are
# reached by string name at runtime (see tools\obfuscar.template.xml's own comment for exactly
# why). Called from make-launcher.ps1, right after the core builds and before it's copied into
# the package - NEVER call this from deploy.ps1's hot-reload path, that stays fast and readable
# for local iteration.
#
#   powershell -File tools\obfuscate.ps1
#
# Harmless to call on a dll that's already obfuscated-then-rebuilt: `dotnet build` always
# produces a fresh, unobfuscated RepoCheatMenu.dll first, so there's nothing to double-obfuscate.
$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$coreBin  = Join-Path $root 'RepoCheatMenu\bin\Release\netstandard2.1'
$coreDll  = Join-Path $coreBin 'RepoCheatMenu.dll'
$template = Join-Path $PSScriptRoot 'obfuscar.template.xml'
$generated = Join-Path $PSScriptRoot 'obfuscar.generated.xml'
$mapDir   = Join-Path $PSScriptRoot 'obfuscar-mapping'

if (-not (Test-Path $coreDll)) { throw "Не найден $coreDll - сначала собери core (dotnet build -c Release)." }

# Finds the newest installed version of a NuGet package and returns one of its subfolders -
# these are the dependencies BepInEx.Core's own .targets file deliberately excludes from the
# build output (ReferenceCopyLocalPaths removal for BepInEx.BaseLib/HarmonyX/Mono.Cecil/
# MonoMod.*, since the real copies come from the game's own installed BepInEx at runtime) -
# Obfuscar still needs to SEE them to resolve attribute/base-type references while renaming.
function Find-PackageDir([string]$packageId, [string]$subPath) {
    $hit = Get-ChildItem (Join-Path $nugetRoot $packageId) -Directory -ErrorAction Stop |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $hit) { throw "NuGet-пакет не найден в кэше: $packageId (нужен для Obfuscar - откуда он резолвит типы)" }
    $dir = Join-Path $hit.FullName $subPath
    if (-not (Test-Path $dir)) { throw "В пакете $packageId нет папки $subPath (версия $($hit.Name) - проверь путь)" }
    return $dir
}

$nugetRoot = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$nugetRoot = $nugetRoot.Trim()
if (-not (Test-Path $nugetRoot)) { throw "Не нашёл папку NuGet-кэша: $nugetRoot" }

$searchPaths = @(
    $coreBin,
    (Find-PackageDir 'harmonyx'               'lib\netstandard2.0'),
    (Find-PackageDir 'unityengine.modules'     'lib\netstandard2.0'),
    (Find-PackageDir 'r.e.p.o.gamelibs.steam'  'ref\netstandard2.1'),
    (Find-PackageDir 'bepinex.baselib'         'lib\netstandard2.0'),
    (Find-PackageDir 'mono.cecil'              'lib\netstandard2.0'),
    (Find-PackageDir 'monomod.runtimedetour'   'lib\netstandard2.0'),
    (Find-PackageDir 'monomod.utils'           'lib\netstandard2.0')
)

$outPath = Join-Path $coreBin 'obfuscated'
if (Test-Path $outPath) { Remove-Item $outPath -Recurse -Force }

# Plain .Replace() throughout - NOT -replace (regex): these paths contain '.', and a path could
# in principle contain '$' too, both of which -replace's pattern/replacement sides treat specially.
$searchXml = ($searchPaths | ForEach-Object { "`t<AssemblySearchPath path=`"$_`" />" }) -join "`n"
$xml = Get-Content $template -Raw -Encoding UTF8
$xml = $xml.Replace('<AssemblySearchPath path="{{HARMONY_DIR}}" />', $searchXml)
$xml = $xml.Replace('{{IN_PATH}}', $coreBin).Replace('{{OUT_PATH}}', $outPath)
[System.IO.File]::WriteAllText($generated, $xml, (New-Object System.Text.UTF8Encoding($false)))

Write-Host 'Обфускация core...'
$toolOutput = & dotnet obfuscar.console $generated 2>&1
if ($LASTEXITCODE -ne 0) {
    $toolOutput | ForEach-Object { Write-Host $_ }
    throw 'Obfuscar упал - core НЕ тронут, в пакет уйдёт обычная сборка только если ты это проигнорируешь.'
}

$obfDll = Join-Path $outPath 'RepoCheatMenu.dll'
if (-not (Test-Path $obfDll)) { throw "Obfuscar отчитался об успехе, но не создал $obfDll" }

# Keep the name -> renamed-name map so a crash reported against a shipped (obfuscated) build can
# still be decoded locally later - never shipped, see .gitignore.
New-Item -ItemType Directory -Force $mapDir | Out-Null
$version = (Select-String -Path (Join-Path $root 'RepoCheatMenu\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
$mapSrc = Join-Path $outPath 'Mapping.txt'
if (Test-Path $mapSrc) { Copy-Item $mapSrc (Join-Path $mapDir "$version.txt") -Force }
else { Write-Host "WARNING: карта имён не найдена ($mapSrc) - декодировать краш из обфусцированного билда будет нечем." }

# RepoCheatMenu.dll itself now gets overwritten with the obfuscated version - any later
# `dotnet build` naturally produces a fresh unobfuscated one again, so this never sticks around
# stale for local debugging/hot-reload (deploy.ps1 always rebuilds before it deploys).
Copy-Item $obfDll $coreDll -Force
$pdb = Join-Path $coreBin 'RepoCheatMenu.pdb'
if (Test-Path $pdb) { Remove-Item $pdb -Force }   # no longer matches the renamed IL - would just mislead a stack trace

Remove-Item $outPath -Recurse -Force
Remove-Item $generated -Force

Write-Host "Обфускация готова (карта имён: $mapDir\$version.txt)"
