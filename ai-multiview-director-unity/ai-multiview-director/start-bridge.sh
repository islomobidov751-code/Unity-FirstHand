#!/usr/bin/env bash
# Starts the bridge: web pages + WebSocket relay + captions. Leave this terminal open.
cd "$(dirname "$0")/bridge" || exit 1
command -v node >/dev/null || { echo "Node.js is not installed. Get the LTS version from https://nodejs.org"; exit 1; }
[ -d node_modules ] || { echo "First run: installing packages..."; npm install; }
[ -f .env ] || cp .env.example .env
exec node server.js
