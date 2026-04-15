#!/usr/bin/env bash
set -euo pipefail

INCLUDE_DIR="${1:?Usage: $0 <include-dir> <output-dir>}"
OUTPUT_DIR="${2:?Usage: $0 <include-dir> <output-dir>}"

if ! command -v ClangSharpPInvokeGenerator &>/dev/null; then
  dotnet tool install --global ClangSharpPInvokeGenerator
fi

RESOURCE_DIR="$(clang -print-resource-dir)"

AVUTIL_HEADERS="
  libavutil/avutil.h
  libavutil/buffer.h
  libavutil/channel_layout.h
  libavutil/dict.h
  libavutil/frame.h
  libavutil/hwcontext.h
  libavutil/imgutils.h
  libavutil/log.h
  libavutil/mathematics.h
  libavutil/mem.h
  libavutil/opt.h
  libavutil/pixdesc.h
  libavutil/pixfmt.h
  libavutil/rational.h
  libavutil/samplefmt.h
"

AVCODEC_HEADERS="
  libavcodec/avcodec.h
  libavcodec/bsf.h
  libavcodec/codec.h
  libavcodec/codec_desc.h
  libavcodec/codec_id.h
  libavcodec/codec_par.h
  libavcodec/defs.h
  libavcodec/packet.h
"

SWSCALE_HEADERS="
  libswscale/swscale.h
"

COMMON_EXCLUDES=(
  -e av_vlog
  -e av_log_set_callback
  -e av_log_default_callback
  -e av_log_format_line
  -e av_log_format_line2
  -e av_cmp_q
  -e av_x_if_null
)

TMPDIR_ROOT=$(mktemp -d)
trap 'rm -rf "$TMPDIR_ROOT"' EXIT

mkdir -p "$OUTPUT_DIR"

generate_lib() {
  local lib_name="$1"
  local native_lib="$2"
  local method_class="$3"
  shift 3
  local headers=("$@")

  local umbrella_dir="$TMPDIR_ROOT/$lib_name"
  mkdir -p "$umbrella_dir"
  local umbrella="$umbrella_dir/ffmpeg.h"

  local traverse_args=()
  for header in "${headers[@]}"; do
    local path="$INCLUDE_DIR/$header"
    if [ -f "$path" ]; then
      echo "#include <$header>" >> "$umbrella"
      traverse_args+=(-t "$path")
    else
      echo "ERROR: $header not found"
      exit 1
    fi
  done

  echo "Generating $lib_name bindings (${#traverse_args[@]} / 2 headers) -> $native_lib"
  ClangSharpPInvokeGenerator \
    -f "$umbrella" \
    "${traverse_args[@]}" \
    -I "$INCLUDE_DIR" \
    -l "$native_lib" \
    -n SimpleVms.FFmpeg.Native \
    -m "$method_class" \
    -o "$OUTPUT_DIR" \
    -x c \
    -c unix-types \
    -c latest-codegen \
    -c multi-file \
    -c generate-helper-types \
    "${COMMON_EXCLUDES[@]}" \
    -a "-resource-dir=$RESOURCE_DIR" || true
}

# Generate in dependency order: avutil first (defines shared types),
# then avcodec, then swscale. ClangSharp multi-file mode skips types
# that already exist in the output dir.

read -ra avutil_arr <<< "$(echo $AVUTIL_HEADERS)"
generate_lib avutil avutil FFAvUtil "${avutil_arr[@]}"

read -ra avcodec_arr <<< "$(echo $AVCODEC_HEADERS)"
generate_lib avcodec avcodec FFAvCodec "${avcodec_arr[@]}"

read -ra swscale_arr <<< "$(echo $SWSCALE_HEADERS)"
generate_lib swscale swscale FFSwScale "${swscale_arr[@]}"

if [ -z "$(ls "$OUTPUT_DIR"/*.cs 2>/dev/null)" ]; then
  echo "ERROR: No output generated"
  exit 1
fi

echo "Generated $(find "$OUTPUT_DIR" -name '*.cs' | wc -l) files"

echo "Validating bindings compile..."
VERIFY_DIR="$TMPDIR_ROOT/verify"
mkdir -p "$VERIFY_DIR"
cp -r "$OUTPUT_DIR"/* "$VERIFY_DIR/"
cat > "$VERIFY_DIR/verify.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
CSPROJ
dotnet build "$VERIFY_DIR/verify.csproj" -nologo -consoleLoggerParameters:NoSummary
echo "Bindings validated"
