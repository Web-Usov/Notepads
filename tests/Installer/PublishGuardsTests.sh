#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
source "$root/tools/installer/release-guards.sh"

GITHUB_REPOSITORY="Web-Usov/Plainix"
GITHUB_SHA="1111111111111111111111111111111111111111"
VERSION="1.7.0.4"
TAG_MODE="matching"
TAG_OBJECT_SHA="3333333333333333333333333333333333333333"
NESTED_TAG_SHA="4444444444444444444444444444444444444444"
TAG_TARGET_MODE="commit"
LATEST_MODE="present"
LATEST_TAG="v1.7.0.3"
passed=0

# Only mocked GitHub reads are permitted: no release, tag or asset writes.
gh() {
  [[ "$1" == api ]] || { echo "Unexpected gh command: $*" >&2; return 1; }
  local path="" arg tag
  for arg in "$@"; do
    case "$arg" in repos/*) path="$arg" ;; esac
  done
  case "$path" in
    "repos/$GITHUB_REPOSITORY/git/matching-refs/tags/"*)
      tag="${path##*/}"
      [[ "$*" == *"select(.ref == \"refs/tags/$tag\")"* ]] ||
        { echo "Tag lookup must filter the exact ref." >&2; return 1; }
      case "$TAG_MODE" in
        missing|prefix_only) return 0 ;;
        matching) printf 'commit\t%s\n' "$GITHUB_SHA" ;;
        mismatched) printf 'commit\t%s\n' "2222222222222222222222222222222222222222" ;;
        annotated|nested) printf 'tag\t%s\n' "$TAG_OBJECT_SHA" ;;
        invalid_target) printf 'blob\t%s\n' "$GITHUB_SHA" ;;
        api_failure) echo 'gh: Forbidden (HTTP 403)' >&2; return 1 ;;
        *) echo "Unknown mock tag mode" >&2; return 1 ;;
      esac
      ;;
    "repos/$GITHUB_REPOSITORY/git/tags/"*)
      case "${path##*/}" in
        "$TAG_OBJECT_SHA")
          if [[ "$TAG_MODE" == nested ]]; then
            printf 'tag\t%s\n' "$NESTED_TAG_SHA"
          else
            printf '%s\t%s\n' "$TAG_TARGET_MODE" "$GITHUB_SHA"
          fi
          ;;
        "$NESTED_TAG_SHA") printf 'commit\t%s\n' "$GITHUB_SHA" ;;
        *) echo "Unknown annotated tag object" >&2; return 1 ;;
      esac
      ;;
    "repos/$GITHUB_REPOSITORY/releases/latest")
      case "$LATEST_MODE" in
        present) printf '%s\n' "$LATEST_TAG" ;;
        absent) echo 'gh: Not Found (HTTP 404)' >&2; return 1 ;;
        api_failure) echo 'gh: Forbidden (HTTP 403)' >&2; return 1 ;;
        *) echo "Unknown latest mock mode" >&2; return 1 ;;
      esac
      ;;
    *) echo "Unexpected GitHub API request: $path" >&2; return 1 ;;
  esac
}

expect_ok() {
  if "$@" >/dev/null 2>&1; then
    passed=$((passed + 1))
  else
    printf 'Expected success: %s\n' "$*" >&2
    exit 1
  fi
}

expect_rejected() {
  if "$@" >/dev/null 2>&1; then
    printf 'Expected rejection: %s\n' "$*" >&2
    exit 1
  else
    passed=$((passed + 1))
  fi
}

expect_ok release_guard_validate_context
expect_ok release_guard_require_matching_tag "v$VERSION"

TAG_MODE=missing
expect_ok release_guard_require_matching_tag "v$VERSION"
TAG_MODE=prefix_only
expect_ok release_guard_require_matching_tag "v$VERSION"
TAG_MODE=mismatched
expect_rejected release_guard_require_matching_tag "v$VERSION"
TAG_MODE=annotated
expect_ok release_guard_require_matching_tag "v$VERSION"
TAG_TARGET_MODE=blob
expect_rejected release_guard_require_matching_tag "v$VERSION"
TAG_TARGET_MODE=commit
TAG_MODE=nested
expect_ok release_guard_require_matching_tag "v$VERSION"
TAG_MODE=invalid_target
expect_rejected release_guard_require_matching_tag "v$VERSION"
TAG_MODE=api_failure
expect_rejected release_guard_require_matching_tag "v$VERSION"
TAG_MODE=matching

expect_ok release_guard_version_greater "1.7.0.10" "1.7.0.9"
expect_ok release_guard_version_greater "1.8.0.0" "1.7.99.99"
expect_rejected release_guard_version_greater "1.7.0.4" "1.7.0.4"
expect_rejected release_guard_version_greater "1.7.0.4" "1.7.0.04"
expect_rejected release_guard_version_greater "1.7.0.3" "1.7.0.4"
expect_rejected release_guard_version_greater "1.7.0" "1.7.0.3"

expect_ok release_guard_require_new_stable
LATEST_TAG="v1.7.0.4"
expect_rejected release_guard_require_new_stable
LATEST_TAG="v1.7.0.5"
expect_rejected release_guard_require_new_stable
LATEST_TAG="v1.7.0.04"
expect_rejected release_guard_require_new_stable
LATEST_TAG="v1.6.99.99"
expect_ok release_guard_require_new_stable
LATEST_TAG="vUnrecognized"
expect_rejected release_guard_require_new_stable
LATEST_MODE=absent
expect_ok release_guard_require_new_stable
LATEST_MODE=api_failure
expect_rejected release_guard_require_new_stable

VERSION="1.7.0"
expect_rejected release_guard_validate_context
VERSION="1.7.0.4"
GITHUB_SHA="not-a-sha"
expect_rejected release_guard_validate_context
GITHUB_SHA="1111111111111111111111111111111111111111"
GITHUB_REPOSITORY="unknown"
expect_rejected release_guard_validate_context

printf 'Passed %d offline release publication guard checks.\n' "$passed"
