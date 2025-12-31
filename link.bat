@echo off
setlocal enabledelayedexpansion

set "TARGET=C:\Users\callu\OneDrive - Coventry College\APPPS\Modding Toolkit-601c9bd\Assets"

echo Searching for .shader files recursively...
set "COUNT=0"

for /r %%f in (*.shader) do (
    echo Found: %%f
    set "FILENAME=%%~nxf"
    mklink "%TARGET%\!FILENAME!" "%%f" >nul 2>&1
    if errorlevel 1 (
        echo [FAILED] Could not link !FILENAME!
    ) else (
        echo [SUCCESS] Linked !FILENAME!
        set /a COUNT+=1
    )
)

echo.
echo Total files processed: !COUNT!
pause