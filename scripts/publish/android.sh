#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

if [ -z "${BUILD_RID:-}" ]; then
  echo "Error: BUILD_RID required (android-arm64 or android-x64)"
  exit 1
fi
case "$BUILD_RID" in
  android-arm64|android-x64) ;;
  *) echo "Error: android publish requires BUILD_RID=android-arm64 or android-x64"; exit 1 ;;
esac

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
  version_code="${BUILD_ANDROID_VERSION_CODE:-$(git -C "$SOLUTION_DIR" rev-list --count HEAD)}"
  VERSION_ARGS+=(-p:AndroidVersionCode="$version_code")
fi

stage_dir="$OUT_DIR/$BUILD_RID"
rm -rf "$stage_dir"

dotnet publish "$SOLUTION_DIR/src/Client.Android/Client.Android.csproj" \
  -c Release -r "$BUILD_RID" -o "$stage_dir" "${VERSION_ARGS[@]}"

apk_src="$stage_dir/com.sythezn.simplevms-Signed.apk"
if [ ! -f "$apk_src" ]; then
  apk_src="$(find "$stage_dir" -maxdepth 1 -name 'com.sythezn.simplevms*.apk' | head -n1)"
fi
if [ -z "${apk_src:-}" ] || [ ! -f "$apk_src" ]; then
  echo "Error: no APK produced under $stage_dir"
  ls -la "$stage_dir" || true
  exit 1
fi

out_apk="$OUT_DIR/simple-vms-${BUILD_RID}.apk"
rm -f "$out_apk"
cp "$apk_src" "$out_apk"

echo "Wrote $out_apk"
