@echo off
REM Starts MediaMTX (video server). Leave this window open.
cd /d "%~dp0mediamtx"
if not exist mediamtx.exe (
  echo MediaMTX is not here yet.
  echo Download the Windows zip from https://github.com/bluenviron/mediamtx/releases
  echo and unzip it into this folder: %cd%
  pause
  exit /b
)
mediamtx.exe
pause
