@echo off
rem Big Christmas Walk: double-click after a problem to put the logs in one zip on your Desktop, to send along.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Send-Logs.ps1" %*
