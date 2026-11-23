@echo off
REM Starts the bridge: web pages + WebSocket relay + captions. Leave this window open.
cd /d "%~dp0bridge"
where node >nul 2>nul
if errorlevel 1 (
  echo Node.js is not installed. Download the LTS version from https://nodejs.org, install it, then run this again.
  pause
  exit /b
)
if not exist node_modules (
  echo First run: installing packages...
  call npm install
)
if not exist .env copy .env.example .env >nul
node server.js
pause
