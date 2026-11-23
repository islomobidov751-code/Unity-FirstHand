@echo off
cd /d "%~dp0dashboard"
echo Dashboard: http://localhost:8090/
py -m http.server 8090
