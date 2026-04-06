#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHANGELOG="$REPO_DIR/CHANGELOG.md"

main() {
  cd "$REPO_DIR"

  echo "Recent releases:"
  echo ""

  local tags
  mapfile -t tags < <(git tag -l 'v*' --sort=-v:refname | head -10)

  if [ ${#tags[@]} -eq 0 ]; then
    echo "No tags found."
    exit 1
  fi

  local i
  for i in "${!tags[@]}"; do
    echo "  $((i + 1))) ${tags[$i]}"
  done

  echo ""
  read -rp "Select version to patch [1]: " selection
  selection="${selection:-1}"

  local tag="${tags[$((selection - 1))]}"
  local ver="${tag#v}"
  local major minor
  IFS='.' read -r major minor _ <<< "$ver"

  local branch="release/${major}.${minor}"

  if git show-ref --verify --quiet "refs/heads/${branch}" 2>/dev/null; then
    echo "Branch ${branch} exists locally, checking out..."
    git checkout "$branch"
  elif git show-ref --verify --quiet "refs/remotes/github/${branch}" 2>/dev/null; then
    echo "Branch ${branch} exists on remote, checking out..."
    git checkout -b "$branch" "github/${branch}"
  else
    echo "Creating ${branch} from ${tag}..."
    git checkout -b "$branch" "$tag"
  fi

  if ! grep -q '## \[Unreleased\]' "$CHANGELOG"; then
    local header
    header="$(head -5 "$CHANGELOG")"
    {
      echo "$header"
      echo ""
      echo "## [Unreleased]"
      echo ""
      tail -n +6 "$CHANGELOG"
    } > "${CHANGELOG}.tmp"
    mv "${CHANGELOG}.tmp" "$CHANGELOG"
    echo "Added [Unreleased] section to CHANGELOG.md"
  fi

  echo ""
  echo "Ready. Cherry-pick your fixes, then run:"
  echo "  ./scripts/release.sh"
}

main "$@"
