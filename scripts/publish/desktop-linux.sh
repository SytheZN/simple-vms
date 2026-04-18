#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

if [ -z "${BUILD_RID:-}" ]; then
  echo "Error: BUILD_RID required (linux-x64 or linux-arm64)"
  exit 1
fi
case "$BUILD_RID" in
  linux-x64|linux-arm64) ;;
  *) echo "Error: desktop-linux requires BUILD_RID=linux-x64 or linux-arm64"; exit 1 ;;
esac

if ! command -v appimagetool >/dev/null 2>&1; then
  echo "Error: appimagetool not found on PATH"
  echo "  In build-ct.sh it comes with the container image."
  echo "  In CI the desktop-linux matrix step installs it."
  exit 1
fi

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
fi

stage="desktop-${BUILD_RID}"
stage_dir="$OUT_DIR/$stage"
rm -rf "$stage_dir"
dotnet publish "$SOLUTION_DIR/src/Client.Desktop/Client.Desktop.csproj" \
  -c Release -o "$stage_dir" --self-contained -r "$BUILD_RID" "${VERSION_ARGS[@]}"

rm -f "$OUT_DIR/simple-vms-${stage}.tar.gz"
tar czf "$OUT_DIR/simple-vms-${stage}.tar.gz" -C "$OUT_DIR" "$stage"

app_name="SimpleVMS"
appdir="$OUT_DIR/appdir-${BUILD_RID}"
rm -rf "$appdir"
mkdir -p "$appdir"

cp -R "$stage_dir/." "$appdir/"
chmod +x "$appdir/Client.Desktop"

cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/Client.Desktop" "$@"
EOF
chmod +x "$appdir/AppRun"

cat > "$appdir/${app_name}.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=${app_name}
Comment=SimpleVMS desktop client
Exec=Client.Desktop
Icon=${app_name}
Categories=AudioVideo;Network;
Terminal=false
EOF

cp "$SOLUTION_DIR/src/Client.Theme/logo/256.png" "$appdir/${app_name}.png"

case "$BUILD_RID" in
  linux-x64)   arch=x86_64 ;;
  linux-arm64) arch=aarch64 ;;
esac

runtime_args=()
if [ -n "${APPIMAGE_RUNTIME_DIR:-}" ] && [ -f "$APPIMAGE_RUNTIME_DIR/runtime-${arch}" ]; then
  runtime_args=(--runtime-file "$APPIMAGE_RUNTIME_DIR/runtime-${arch}")
fi

out_file="$OUT_DIR/simple-vms-${stage}.AppImage"
rm -f "$out_file"
ARCH="$arch" appimagetool --no-appstream "${runtime_args[@]}" "$appdir" "$out_file"

rm -rf "$appdir"
echo "Wrote $out_file"
