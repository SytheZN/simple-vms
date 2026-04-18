#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

if [ -z "${BUILD_RID:-}" ]; then
  echo "Error: BUILD_RID required (osx-x64 or osx-arm64)"
  exit 1
fi
case "$BUILD_RID" in
  osx-x64|osx-arm64) ;;
  *) echo "Error: desktop-macos requires BUILD_RID=osx-x64 or osx-arm64"; exit 1 ;;
esac

for tool in create-dmg iconutil; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Error: $tool not found — requires macOS + \`brew install create-dmg\`"
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

rm -f "$OUT_DIR/simple-vms-${stage}.tar.gz"
tar czf "$OUT_DIR/simple-vms-${stage}.tar.gz" -C "$OUT_DIR" "$stage"

app_name="SimpleVMS"
bundle_id="com.simplevms.desktop"
version="${BUILD_VERSION:-0.0.0}"
dmg_src="$OUT_DIR/dmg-src-${BUILD_RID}"
app_path="$dmg_src/${app_name}.app"

rm -rf "$dmg_src"
mkdir -p "$app_path/Contents/MacOS" "$app_path/Contents/Resources"

cp -R "$stage_dir/." "$app_path/Contents/MacOS/"
chmod +x "$app_path/Contents/MacOS/Client.Desktop"

logo_dir="$SOLUTION_DIR/src/Client.Theme/logo"
iconset="$OUT_DIR/${app_name}-${BUILD_RID}.iconset"
rm -rf "$iconset"
mkdir -p "$iconset"
cp "$logo_dir/16.png"   "$iconset/icon_16x16.png"
cp "$logo_dir/32.png"   "$iconset/icon_16x16@2x.png"
cp "$logo_dir/32.png"   "$iconset/icon_32x32.png"
cp "$logo_dir/64.png"   "$iconset/icon_32x32@2x.png"
cp "$logo_dir/128.png"  "$iconset/icon_128x128.png"
cp "$logo_dir/256.png"  "$iconset/icon_128x128@2x.png"
cp "$logo_dir/256.png"  "$iconset/icon_256x256.png"
cp "$logo_dir/512.png"  "$iconset/icon_256x256@2x.png"
cp "$logo_dir/512.png"  "$iconset/icon_512x512.png"
cp "$logo_dir/1024.png" "$iconset/icon_512x512@2x.png"
iconutil -c icns -o "$app_path/Contents/Resources/AppIcon.icns" "$iconset"
rm -rf "$iconset"

cat > "$app_path/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>${app_name}</string>
  <key>CFBundleDisplayName</key>
  <string>${app_name}</string>
  <key>CFBundleExecutable</key>
  <string>Client.Desktop</string>
  <key>CFBundleIdentifier</key>
  <string>${bundle_id}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleVersion</key>
  <string>${version}</string>
  <key>CFBundleShortVersionString</key>
  <string>${version}</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>LSMinimumSystemVersion</key>
  <string>10.15</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

dmg_out="$OUT_DIR/simple-vms-${stage}.dmg"
rm -f "$dmg_out"
create-dmg \
  --volname "${app_name}" \
  --window-pos 200 120 \
  --window-size 540 360 \
  --icon-size 100 \
  --icon "${app_name}.app" 140 170 \
  --hide-extension "${app_name}.app" \
  --app-drop-link 400 170 \
  "$dmg_out" "$dmg_src" >/dev/null

rm -rf "$dmg_src"
echo "Wrote $dmg_out"
