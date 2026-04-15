#!/usr/bin/env bash
# Shared FFmpeg configure flags for all targets.
# Consumers pass additional per-target flags (cross-compile triplet, hwaccel).
# Emits the flag list to stdout, space-separated.

set -euo pipefail

cat <<'EOF'
--disable-everything
--disable-programs
--disable-doc
--disable-avdevice
--disable-avformat
--disable-avfilter
--disable-swresample
--disable-network
--disable-protocols
--disable-demuxers
--disable-muxers
--disable-parsers
--disable-bsfs
--enable-avcodec
--enable-avutil
--enable-swscale
--enable-decoder=h264,hevc,mjpeg,mjpegb
--enable-pic
--enable-small
--disable-debug
EOF
