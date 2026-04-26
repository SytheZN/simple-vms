#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

if [ -z "${BUILD_RID:-}" ]; then
  echo "Error: BUILD_RID required (win-x64 or win-arm64)"
  exit 1
fi
case "$BUILD_RID" in
  win-x64|win-arm64) ;;
  *) echo "Error: desktop-windows requires BUILD_RID=win-x64 or win-arm64"; exit 1 ;;
esac

for tool in zip makensis; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Error: $tool not found on PATH"
    echo "  In build-ct.sh it comes with the container image."
    echo "  In CI the desktop-windows matrix step installs mingw32-nsis."
    exit 1
  fi
done

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
fi

stage="desktop-${BUILD_RID}"
stage_dir="$OUT_DIR/$stage"
rm -rf "$stage_dir"
dotnet publish "$SOLUTION_DIR/src/Client.Desktop/Client.Desktop.csproj" \
  -c Release -o "$stage_dir" --self-contained -r "$BUILD_RID" "${VERSION_ARGS[@]}"

rm -f "$OUT_DIR/simple-vms-${stage}.zip"
(cd "$OUT_DIR" && zip -qr "simple-vms-${stage}.zip" "$stage")

case "$BUILD_RID" in
  win-x64)   vcrt_arch=X64   ; vcrt_url=https://aka.ms/vs/17/release/vc_redist.x64.exe ;;
  win-arm64) vcrt_arch=ARM64 ; vcrt_url=https://aka.ms/vs/17/release/vc_redist.arm64.exe ;;
esac

out_installer="$OUT_DIR/simple-vms-${stage}.exe"
rm -f "$out_installer"
numeric_version="${BUILD_VERSION:-0.0.0}"
numeric_version="${numeric_version%%-*}"
numeric_version="${numeric_version%%+*}"
makensis \
  -DVERSION="${numeric_version}" \
  -DSTAGE_DIR="$stage_dir" \
  -DOUT_FILE="$out_installer" \
  -DICON_PATH="$SOLUTION_DIR/src/Client.Theme/logo/app.ico" \
  -DVCRT_ARCH="$vcrt_arch" \
  -DVCRT_URL="$vcrt_url" \
  "$SOLUTION_DIR/scripts/publish/windows-installer.nsi"

echo "Wrote $OUT_DIR/simple-vms-${stage}.zip"
echo "Wrote $out_installer"
