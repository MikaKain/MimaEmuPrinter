@echo off
rem Lance publish.ps1 depuis cmd.exe (les .ps1 y sont associes a un editeur, pas a PowerShell).
rem Les arguments sont transmis tels quels : publish -SingleFile, publish -Runtime win-x64,linux-x64
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
exit /b %ERRORLEVEL%
