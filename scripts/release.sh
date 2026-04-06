#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHANGELOG="$REPO_DIR/CHANGELOG.md"

suggest_version() {
  local branch
  branch="$(git rev-parse --abbrev-ref HEAD)"

  local latest_tag
  latest_tag="$(git tag -l 'v*' --sort=-v:refname | head -1 || true)"

  if [ -z "$latest_tag" ]; then
    echo "0.1.0"
    return
  fi

  local ver="${latest_tag#v}"
  local major minor patch
  IFS='.' read -r major minor patch <<< "$ver"

  if [[ "$branch" == release/* ]]; then
    echo "${major}.${minor}.$((patch + 1))"
  else
    echo "${major}.$((minor + 1)).0"
  fi
}

promote_changelog() {
  local version="$1"
  local date
  date="$(date +%Y-%m-%d)"

  if ! grep -q '## \[Unreleased\]' "$CHANGELOG"; then
    echo "Error: no [Unreleased] section found in CHANGELOG.md"
    exit 1
  fi

  local unreleased_content
  unreleased_content="$(awk '/^## \[Unreleased\]/{found=1; next} /^## \[/{exit} found{print}' "$CHANGELOG")"

  if [ -z "$(echo "$unreleased_content" | tr -d '[:space:]')" ]; then
    echo "Error: [Unreleased] section is empty"
    exit 1
  fi

  local prev_tag
  prev_tag="$(git tag -l 'v*' --sort=-v:refname | head -1 || true)"
  local diff_from="${prev_tag:-$(git rev-list --max-parents=0 HEAD | head -1)}"

  local repo_url
  repo_url="$(git remote get-url github 2>/dev/null | sed 's/\.git$//' || true)"

  awk -v version="$version" -v date="$date" \
      -v diff_from="$diff_from" -v diff_to="v${version}" -v repo_url="$repo_url" '
    /^## \[Unreleased\]/ {
      print "## [Unreleased]"
      print ""
      print "## [" version "] - " date
      next
    }
    /^\[Unreleased\]:/ {
      if (repo_url != "") {
        print "[Unreleased]: " repo_url "/compare/" diff_to "...HEAD"
        print "[" version "]: " repo_url "/compare/" diff_from "..." diff_to
      } else {
        print
      }
      next
    }
    { print }
  ' "$CHANGELOG" > "${CHANGELOG}.tmp"
  mv "${CHANGELOG}.tmp" "$CHANGELOG"
}

main() {
  cd "$REPO_DIR"

  local suggested
  suggested="$(suggest_version)"

  local version="${1:-}"
  if [ -z "$version" ]; then
    read -rp "Version [$suggested]: " version
    version="${version:-$suggested}"
  fi

  if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: version must be in X.Y.Z format"
    exit 1
  fi

  if git tag -l "v${version}" | grep -q .; then
    echo "Error: tag v${version} already exists"
    exit 1
  fi

  echo "Releasing ${version}..."

  promote_changelog "$version"
  git add "$CHANGELOG"
  git commit -m "release: ${version}"
  git tag "v${version}"

  local branch
  branch="$(git rev-parse --abbrev-ref HEAD)"
  echo ""
  echo "Done. Review the commit, then push:"
  echo "  git push github ${branch} v${version}"
}

main "$@"
