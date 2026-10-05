@echo off
setlocal
title PoseKart - Python Tracking Server
cd /d "%~dp0"
echo Starting PoseKart tracking server...
echo The webcam and moving steering wheel will appear in a separate Python window.
echo Keep this window open while using hand controls in Unity.
echo Press Esc in the camera window or Ctrl+C here to stop the server and webcam.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\PoseServer\start.ps1"
echo.
echo Server stopped. If an error occurred, read the message above.
pause
endlocal
