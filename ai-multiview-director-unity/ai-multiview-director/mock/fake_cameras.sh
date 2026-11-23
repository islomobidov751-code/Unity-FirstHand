#!/usr/bin/env bash
# Publishes N fake "virtual camera" feeds into MediaMTX so you can build the dashboard
# before the VR game exists. Each feed shows its camera name + a wall-clock with
# milliseconds, so you can measure glass-to-glass latency by photographing your screen
# (or comparing against the dashboard clock).
#
# Usage:  ./fake_cameras.sh [count=4] [host=localhost]
# Stop:   Ctrl+C (kills all encoders)
#
# Encoder settings are the important part for sub-second latency:
#   -tune zerolatency, no B-frames, short GOP (1s), baseline profile (every browser decodes it).
COUNT=${1:-4}
HOST=${2:-localhost}
PIDS=()
trap 'kill ${PIDS[@]} 2>/dev/null' EXIT INT TERM

COLORS=(0x8B0000 0x00008B 0x006400 0x4B0082)
for i in $(seq 1 "$COUNT"); do
  ffmpeg -hide_banner -loglevel warning -re \
    -f lavfi -i "testsrc2=size=1280x720:rate=30" \
    -vf "drawbox=c=${COLORS[$((i-1))]}@0.35:t=fill,settb=1/1000,setpts='RTCTIME/1000',drawtext=text='CAM ${i}  %{pts\:localtime\:0\:%H\\\\\:%M\\\\\:%S}.%{eif\:mod(t*1000\,1000)\:d\:3}':fontsize=56:fontcolor=white:box=1:boxcolor=black@0.6:x=40:y=40,setpts=PTS-STARTPTS" \
    -c:v libx264 -preset ultrafast -tune zerolatency -profile:v baseline -pix_fmt yuv420p \
    -g 30 -keyint_min 30 -bf 0 -b:v 2500k -maxrate 2500k -bufsize 1250k \
    -f rtsp -rtsp_transport tcp "rtsp://${HOST}:8554/cam${i}" &
  PIDS+=($!)
  echo "publishing cam${i} -> rtsp://${HOST}:8554/cam${i}  (browser: http://${HOST}:8889/cam${i})"
done
wait
