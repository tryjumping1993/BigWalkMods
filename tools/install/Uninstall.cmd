@echo off
rem Big Christmas Walk: double-click to remove the mod and put Big Walk back as it was.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
