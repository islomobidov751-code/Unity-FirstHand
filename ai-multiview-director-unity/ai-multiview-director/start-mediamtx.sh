#!/usr/bin/env bash
# Starts MediaMTX (video server). Leave this terminal open.
cd "$(dirname "$0")/mediamtx" || exit 1
[ -x ./mediamtx ] || { echo "Download MediaMTX for your system from https://github.com/bluenviron/mediamtx/releases and unzip it into: $(pwd)"; exit 1; }
exec ./mediamtx
