#!/usr/bin/env bash
# Runs build or publish tasks inside a Fedora container that mirrors the CI image.
# Usage:
#   ./build-ct.sh <build.sh-args>...            # forwards to ./build.sh
#   ./build-ct.sh publish <name> [args...]      # runs ./scripts/publish/<name>.sh
# Examples:
#   ./build-ct.sh build test
#   ./build-ct.sh publish server
#   ./build-ct.sh publish desktop-linux
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Bump tag when the inline Dockerfile below changes so contributors pick up the new base.
IMAGE="simple-vms-ci:fedora43-dotnet10-v1"

usage() {
  echo "Usage:" >&2
  echo "  $(basename "$0") <build.sh-args>...           forwards to ./build.sh" >&2
  echo "  $(basename "$0") publish <name> [args...]     runs ./scripts/publish/<name>.sh" >&2
  exit 1
}

if [ "$#" -eq 0 ]; then
  usage
fi

list_publish_targets() {
  for f in "$SOLUTION_DIR"/scripts/publish/*.sh; do
    [ -e "$f" ] && echo "  $(basename "$f" .sh)"
  done
}

cmd=(./build.sh "$@")
if [ "$1" = "publish" ]; then
  shift
  if [ "$#" -eq 0 ]; then
    echo "Error: 'publish' requires a target name" >&2
    echo "Available:" >&2
    list_publish_targets >&2
    exit 1
  fi
  target="$1"
  shift
  script_path="$SOLUTION_DIR/scripts/publish/${target}.sh"
  if [ ! -x "$script_path" ]; then
    echo "Error: ./scripts/publish/${target}.sh not found" >&2
    echo "Available:" >&2
    list_publish_targets >&2
    exit 1
  fi
  cmd=("./scripts/publish/${target}.sh" "$@")
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "docker not found on PATH (Rancher Desktop / Docker Desktop / colima required)" >&2
  exit 1
fi

if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  echo "==> Building $IMAGE (one-time, cached for future runs)"
  docker build -t "$IMAGE" - <<'DOCKERFILE'
FROM fedora:43
RUN dnf install -y dotnet-sdk-10.0 nodejs git squashfs-tools zip file desktop-file-utils mingw32-nsis && dnf clean all
RUN arch=$(uname -m) \
    && curl -Lo /usr/local/bin/appimagetool \
         "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-${arch}.AppImage" \
    && chmod +x /usr/local/bin/appimagetool \
    && mkdir -p /usr/local/share/appimage \
    && curl -Lo /usr/local/share/appimage/runtime-x86_64 \
         https://github.com/AppImage/AppImageKit/releases/download/continuous/runtime-x86_64 \
    && curl -Lo /usr/local/share/appimage/runtime-aarch64 \
         https://github.com/AppImage/AppImageKit/releases/download/continuous/runtime-aarch64
ENV APPIMAGE_EXTRACT_AND_RUN=1
ENV APPIMAGE_RUNTIME_DIR=/usr/local/share/appimage
DOCKERFILE
fi

# Named Docker volumes below persist package caches across runs.
# To reset: docker volume rm simple-vms-nuget simple-vms-npm simple-vms-node-modules simple-vms-dotnet

TTY_FLAG=()
if [ -t 0 ] && [ -t 1 ]; then
  TTY_FLAG=(-it)
fi

NUGET_VOLUME="simple-vms-nuget"
NPM_VOLUME="simple-vms-npm"
NODE_MODULES_VOLUME="simple-vms-node-modules"
DOTNET_VOLUME="simple-vms-dotnet"

HOST_NUGET_CONFIG_MOUNT=()
if [ -d "$HOME/.nuget/NuGet" ]; then
  HOST_NUGET_CONFIG_MOUNT=(-v "$HOME/.nuget/NuGet":/root/.nuget/NuGet:ro)
fi

exec docker run --rm "${TTY_FLAG[@]}" \
  -v "$SOLUTION_DIR":/w \
  -v "$NUGET_VOLUME":/root/.nuget/packages \
  -v "$NPM_VOLUME":/root/.npm \
  -v "$NODE_MODULES_VOLUME":/w/src/Client.Web/node_modules \
  -v "$DOTNET_VOLUME":/root/.dotnet \
  "${HOST_NUGET_CONFIG_MOUNT[@]}" \
  -w /w \
  -e BUILD_VERSION="${BUILD_VERSION:-}" \
  -e BUILD_HASH="${BUILD_HASH:-}" \
  -e BUILD_RID="${BUILD_RID:-}" \
  "$IMAGE" \
  bash -c 'git config --global --add safe.directory /w && exec "$@"' _ "${cmd[@]}"
