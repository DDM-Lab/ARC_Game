#!/usr/bin/env bash
# Upload the Linux headless CORA build to the Auton cluster.
# DO NOT run until the login host is confirmed. Outward-facing.
#
# Usage:  ./upload_linux_build.sh <login-host>
# Example: ./upload_linux_build.sh lop-login.autonlab.org
set -euo pipefail

HOST="${1:?pass the cluster login host as arg1}"
SRC="$(cd "$(dirname "$0")" && pwd)/Build/Headless/Linux/"
# NOTE: /zfsauton/scratch is NOT mounted on the lop2 login node (autofs, GPU nodes only).
# Land in home from the login node; relocate/symlink into scratch from a GPU node.
DEST="/zfsauton2/home/cpulling/CORA/ARC_Game/ARC_Game_New/Build/Headless/Linux/"

ssh "cpulling@${HOST}" "mkdir -p '${DEST}'"
rsync -avz --delete "${SRC}" "cpulling@${HOST}:${DEST}"

echo "Uploaded $(du -sh "${SRC}" | cut -f1) to cpulling@${HOST}:${DEST}"
echo "Make the executable runnable on the cluster:"
echo "  ssh cpulling@${HOST} 'chmod +x ${DEST}ARC_Headless.x86_64'"
