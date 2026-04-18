#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

BUILD_HASH="${BUILD_HASH:-local}"

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
fi

RID_ARGS=()
if [ -n "${BUILD_RID:-}" ]; then
  RID_ARGS=(--self-contained -r "$BUILD_RID")
fi

build() {
  dotnet build "$SOLUTION_DIR/Solution.slnx" -c Release --no-incremental "${VERSION_ARGS[@]}"
}

test() {
  rm -rf "$OUT_DIR/test-results"

  local test_plugins_dir="$OUT_DIR/test/plugins"
  rm -rf "$test_plugins_dir"
  mkdir -p "$test_plugins_dir"
  for proj in "$SOLUTION_DIR"/src/plugins/*/*/*.csproj; do
    local name
    name="$(basename "$(dirname "$proj")")"
    dotnet publish "$proj" -c Release --no-build -o "$test_plugins_dir/$name"
  done

  dotnet test "$SOLUTION_DIR/Solution.slnx" -c Release --no-build \
    --collect:"XPlat Code Coverage" \
    --settings "$SOLUTION_DIR/coverage.runsettings" \
    --logger "trx;LogFileName=results.trx" \
    --results-directory "$OUT_DIR/test-results"

  local reports
  reports=$(find "$OUT_DIR/test-results" -name "coverage.cobertura.xml" -printf "%p;" 2>/dev/null)
  if [ -n "$reports" ]; then
    dotnet tool restore
    dotnet tool run reportgenerator \
      -reports:"${reports%;}" \
      -targetdir:"$OUT_DIR/test-results/coverage" \
      -reporttypes:Cobertura\;TextSummary \
      > /dev/null

    cat "$OUT_DIR/test-results/coverage/Summary.txt"
  fi
}

publish() {
  rm -rf "$OUT_DIR/server"
  dotnet publish "$SOLUTION_DIR/src/Server/Server.csproj" -c Release -o "$OUT_DIR/server" "${RID_ARGS[@]}" "${VERSION_ARGS[@]}"

  for proj in "$SOLUTION_DIR"/src/plugins/*/*/*.csproj; do
    local name
    name="$(basename "$(dirname "$proj")")"
    dotnet publish "$proj" -c Release -o "$OUT_DIR/server/plugins/$name" "${RID_ARGS[@]}" "${VERSION_ARGS[@]}"
  done

  local rid="${BUILD_RID:-local}"
  echo "Creating tarball"
  tar czf "$OUT_DIR/simple-vms-${rid}.tar.gz" -C "$OUT_DIR" server
}

publish_desktop() {
  if [ -z "${BUILD_RID:-}" ]; then
    echo "Error: BUILD_RID required for publish-desktop"
    exit 1
  fi

  local stage="desktop-${BUILD_RID}"
  local stage_dir="$OUT_DIR/$stage"
  rm -rf "$stage_dir"
  dotnet publish "$SOLUTION_DIR/src/Client.Desktop/Client.Desktop.csproj" \
    -c Release -o "$stage_dir" --self-contained -r "$BUILD_RID" "${VERSION_ARGS[@]}"

  rm -f "$OUT_DIR/simple-vms-${stage}.tar.gz" "$OUT_DIR/simple-vms-${stage}.zip"
  case "$BUILD_RID" in
    win-*)
      (cd "$OUT_DIR" && zip -qr "simple-vms-${stage}.zip" "$stage")
      ;;
    *)
      tar czf "$OUT_DIR/simple-vms-${stage}.tar.gz" -C "$OUT_DIR" "$stage"
      ;;
  esac
}

docker_build() {
  if [ ! -d "$OUT_DIR/server" ]; then
    echo "Error: run './build.sh publish' first"
    exit 1
  fi

  local version="${BUILD_VERSION:-0.0.0}-${BUILD_HASH}"
  local tag="simple-vms:${version}"
  cp "$SOLUTION_DIR/src/docker/Dockerfile" "$OUT_DIR/Dockerfile"
  docker build -t "$tag" "$OUT_DIR"
}

if [ $# -eq 0 ]; then
  echo "Usage: $0 {build|test|publish|publish-desktop|docker} [...]"
  echo ""
  echo "Environment:"
  echo "  BUILD_VERSION    Version string (default: from Directory.Build.props)"
  echo "  BUILD_HASH       Short commit hash (default: local)"
  echo "  BUILD_RID        Runtime identifier, e.g. linux-x64 (default: build for this system)"
  exit 1
fi

declare -A requested=()
for cmd in "$@"; do
  case "$cmd" in
    build|test|publish|publish-desktop|docker) requested[$cmd]=1 ;;
    *)
      echo "Error: unknown command '$cmd'"
      echo "Usage: $0 {build|test|publish|publish-desktop|docker} [...]"
      exit 1
      ;;
  esac
done

for cmd in build test publish publish-desktop docker; do
  if [ -n "${requested[$cmd]:-}" ]; then
    case "$cmd" in
      build)           build ;;
      test)            test ;;
      publish)         publish ;;
      publish-desktop) publish_desktop ;;
      docker)          docker_build ;;
    esac
  fi
done
