@echo off
rem Builds dist\TimeTracker.exe - one self-contained file, runs on any Windows 10/11 x64.
rem Each run replaces the previous exe.
cd /d "%~dp0"
if not exist dist mkdir dist

dotnet publish src\TimeTracker\TimeTracker.csproj -p:PublishProfile=Portable -nologo || goto :fail

echo.
echo Done:
dir /b dist
pause
exit /b 0

:fail
echo.
echo Build failed. If TimeTracker is running (check the tray), quit it and try again.
pause
exit /b 1
