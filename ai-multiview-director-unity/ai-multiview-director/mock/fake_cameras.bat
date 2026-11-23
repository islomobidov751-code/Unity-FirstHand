@echo off
REM Windows version of fake_cameras.sh — double-click it (MediaMTX must already be running).
REM Opens 4 small windows, one per fake camera. Close those windows to stop the cameras.

where ffmpeg >nul 2>nul
if errorlevel 1 (
  echo FFmpeg was not found. Install it first ^(see the guide^), then try again.
  pause
  exit /b
)

call :cam 1 0x8B0000
call :cam 2 0x00008B
call :cam 3 0x006400
call :cam 4 0x4B0082
echo.
echo 4 fake cameras are now sending video to MediaMTX.
echo Check one in your browser: http://localhost:8889/cam1
pause
exit /b

:cam
start "cam%1" /min ffmpeg -hide_banner -loglevel warning -re -f lavfi -i testsrc2=size=1280x720:rate=30 -vf "drawbox=c=%2@0.35:t=fill,settb=1/1000,setpts=RTCTIME/1000,drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='CAM %1  %%{pts\:localtime\:0\:%%H\\\:%%M\\\:%%S}.%%{eif\:mod(t*1000\,1000)\:d\:3}':fontsize=56:fontcolor=white:box=1:boxcolor=black@0.6:x=40:y=40,setpts=PTS-STARTPTS" -c:v libx264 -preset ultrafast -tune zerolatency -profile:v baseline -pix_fmt yuv420p -g 30 -keyint_min 30 -bf 0 -b:v 2500k -maxrate 2500k -bufsize 1250k -f rtsp -rtsp_transport tcp rtsp://localhost:8554/cam%1
exit /b
