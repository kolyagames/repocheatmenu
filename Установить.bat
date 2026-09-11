@echo off
chcp 65001 >NUL
setlocal

set "PROJECT=%~dp0RepoCheatMenu\RepoCheatMenu.csproj"
set "OUTDIR=%~dp0RepoCheatMenu\bin\Release\netstandard2.1"
set "DEST=C:\Program Files (x86)\Steam\steamapps\common\REPO\BepInEx\plugins\local-RepoCheatMenu"

echo ================================================
echo   RepoCheatMenu - сборка и установка одним кликом
echo ================================================
echo.

if not exist "%PROJECT%" (
    echo [!] Не нашёл проект: "%PROJECT%"
    echo     Похоже, этот файл переместили отдельно от папки RepoCheatMenu.
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq REPO.exe" 2>NUL | find /I "REPO.exe" >NUL
if not errorlevel 1 (
    echo [!] Игра REPO сейчас запущена - файлы плагина заблокированы Windows,
    echo     копирование не пройдёт.
    echo.
    echo     Закрой игру полностью и запусти этот файл снова.
    echo.
    pause
    exit /b 1
)

echo Собираю мод...
echo.
dotnet build "%PROJECT%" -c Release
if errorlevel 1 (
    echo.
    echo [!] Сборка не удалась - ошибки компилятора выше.
    echo     Ничего не установлено, старая версия в игре не тронута.
    pause
    exit /b 1
)

if not exist "%DEST%" (
    echo Папка плагина не найдена, создаю: "%DEST%"
    mkdir "%DEST%"
)

echo.
echo Копирую в игру...
copy /Y "%OUTDIR%\*.dll" "%DEST%\" >NUL
if errorlevel 1 (
    echo [!] Не удалось скопировать файлы - см. ошибку выше.
    pause
    exit /b 1
)

echo.
echo ================================================
echo   Готово! Можно запускать игру.
echo ================================================
echo.
pause
