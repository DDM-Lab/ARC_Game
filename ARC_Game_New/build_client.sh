#!/usr/bin/env bash
#
# Build the playable ARC_Game client for distribution.
#
# Usage:
#   ./build_client.sh            # builds macOS (default)
#   ./build_client.sh windows    # mac | windows | linux | webgl | all
#
# IMPORTANT: the Unity Editor must NOT be open on this project while building
# (Unity allows only one instance per project). Close it first.
#
# Each non-mac target needs its Build Support module installed in Unity Hub:
#   Hub -> Installs -> (gear on 2022.3.62f3) -> Add Modules.
#
set -euo pipefail
cd "$(dirname "$0")"

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"
TARGET="${1:-mac}"

case "$TARGET" in
  mac)     METHOD=PlayerBuildScript.BuildMac ;;
  windows) METHOD=PlayerBuildScript.BuildWindows ;;
  linux)   METHOD=PlayerBuildScript.BuildLinux ;;
  webgl)   METHOD=PlayerBuildScript.BuildWebGL ;;
  all)     METHOD=PlayerBuildScript.BuildAll ;;
  *) echo "[build_client] Unknown target '$TARGET' (use: mac|windows|linux|webgl|all)"; exit 1 ;;
esac

LOG="/tmp/arc_client_build_${TARGET}.log"
echo "[build_client] Target=$TARGET  Method=$METHOD"
echo "[build_client] Log: $LOG"
"$UNITY" -quit -batchmode -nographics \
  -projectPath "$PWD" \
  -executeMethod "$METHOD" \
  -logFile "$LOG"
echo "[build_client] Done — output under Build/Client/"
