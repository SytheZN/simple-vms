#!/usr/bin/env bash
set -euo pipefail

LOGO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOGO_SVG="$LOGO_DIR/logo.svg"
LOGO_VIEW_BOX_SIZE=240
SVG_USER_UNITS_PER_INCH=96
NOTIFICATION_SIZE=96

if ! command -v magick >/dev/null 2>&1; then
  echo "Error: magick not found on PATH (ImageMagick 7 with librsvg)"
  exit 1
fi

rasterise() {
  local size="$1" png="$2"
  shift 2
  local density
  density="$(awk "BEGIN { print $SVG_USER_UNITS_PER_INCH * $size / $LOGO_VIEW_BOX_SIZE }")"
  magick -background none -density "$density" "$LOGO_SVG" -resize "${size}x${size}!" "$@" "PNG32:$png"
}

multiply_opacity() {
  echo -channel A -evaluate multiply "$1" +channel
}

rasterise 16 "$LOGO_DIR/16.png" $(multiply_opacity 2)
rasterise 32 "$LOGO_DIR/32.png" $(multiply_opacity 1.3)

for size in 48 64 128 256 512 1024; do
  rasterise "$size" "$LOGO_DIR/$size.png"
done

rasterise "$NOTIFICATION_SIZE" "$LOGO_DIR/notification.png" -fill white -colorize 100

magick "$LOGO_DIR/16.png" "$LOGO_DIR/32.png" "$LOGO_DIR/48.png" "$LOGO_DIR/256.png" "$LOGO_DIR/app.ico"
