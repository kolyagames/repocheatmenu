# ============================================================================
# RepoCheatMenu - инструменты сборки
# Copyright (c) 2026 Kolya. Все права защищены.
# Исходный код закрыт: без разрешения автора не копировать, не публиковать
# и не выдавать за свою работу. Подробности - LICENSE.txt.
# ============================================================================

# Проверка мода против НАСТОЯЩИХ сборок игры.
#
# Мод компилируется против публицированных пакетов (R.E.P.O.GameLibs, UnityEngine.Modules): в них ВСЁ
# public, поэтому обращение к internal/private полю или методу компилируется без ошибок, а в игре падает
# с FieldAccessException / MethodAccessException (так было с FontEngine.TryAddGlyphToTexture и
# EnemyHealth.healthCurrent). Этот скрипт собирает копию проекта со ссылками на DLL из
# REPO_Data\Managed и BepInEx\core профиля - каждое такое обращение становится ошибкой компиляции.
#
# Запуск:  powershell -File tools\check-real-refs.ps1
# Результат: 0 = чисто, 1 = есть обращения к закрытым членам (список выше).
# CS7069 про System.Reflection.Emit.Label - шум окружения (HarmonyX собран против mscorlib), игнорируется.
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\REPO",
    [string]$Profile = "$env:LOCALAPPDATA\RepoLauncher\profiles\default"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'tools\.realref-check'
$managed = Join-Path $GameDir 'REPO_Data\Managed'
$core = Join-Path $Profile 'BepInEx\core'
if (-not (Test-Path $managed)) { Write-Host "Не найдена папка игры: $managed"; exit 2 }
if (-not (Test-Path $core)) { Write-Host "Не найден BepInEx профиля: $core"; exit 2 }

New-Item -ItemType Directory -Force $work | Out-Null
$refs = Get-ChildItem $managed -Filter *.dll |
    Where-Object { $_.Name -notmatch '^(mscorlib|netstandard|System|Microsoft|Mono\.)' } |
    ForEach-Object { "    <Reference Include=`"$($_.BaseName)`"><HintPath>$($_.FullName)</HintPath><Private>false</Private></Reference>" }
$refs += @('BepInEx', '0Harmony', 'MonoMod.Utils', 'MonoMod.RuntimeDetour', 'Mono.Cecil') |
    ForEach-Object { "    <Reference Include=`"$_`"><HintPath>$core\$_.dll</HintPath><Private>false</Private></Reference>" }
$src = Join-Path $root 'RepoCheatMenu'
$xml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10</LangVersion>
    <AssemblyName>RealRefCheck</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>CS0618;CS0612;CS1701;CS1702;MSB3277;MSB3243;CS0162;CS0168;CS0219;CS0414;CS0649;CS0169</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$src\*.cs" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.8.0" />
  </ItemGroup>
  <ItemGroup>
$($refs -join "`n")
  </ItemGroup>
</Project>
"@
[System.IO.File]::WriteAllText((Join-Path $work 'RealRefCheck.csproj'), $xml, (New-Object System.Text.UTF8Encoding($false)))

Push-Location $work
try { $out = & dotnet build -c Release 2>&1 | Out-String } finally { Pop-Location }

$errors = $out -split "`r?`n" | Where-Object { $_ -match ': error ' -and $_ -notmatch 'CS7069' } |
    ForEach-Object { ($_ -replace '\s*\[[^\]]*\.csproj\]\s*$', '').Trim() } | Sort-Object -Unique
if ($errors.Count -eq 0) {
    Write-Host 'OK: мод не обращается напрямую к закрытым членам игры, Unity и Photon.'
    exit 0
}
Write-Host "Найдено $($errors.Count) проблем (в игре это FieldAccessException/MethodAccessException или член не существует):"
$errors | ForEach-Object { Write-Host "  $_" }
exit 1
