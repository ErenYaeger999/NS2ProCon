@echo off
cd /d "%~dp0"
title NS2 Pro - PC Wireless
"%~dp0.venv\Scripts\python.exe" "%~dp0tools\bluetooth_radio.py" --enable
if errorlevel 1 goto done
"%~dp0.venv\Scripts\python.exe" "%~dp0tools\pc_bridge.py" --address BC:89:A6:5C:7A:32
:done
pause
