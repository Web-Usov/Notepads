#!/usr/bin/env bash
# Guard helpers for GitHub release publication. Sourced by publish.sh and its tests.

release_guard_error() {
  printf 'Release publication blocked: %s\n' "$*" >&2
  return 1
}

release_guard_valid_version() {
  [[ "$1" =~ ^[0-9]{1,5}(\.[0-9]{1,5}){3}$ ]]
}

release_guard_validate_context() {
  release_guard_valid_version "$VERSION" || release_guard_error "Invalid four-part package version: $VERSION"
  [[ "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ]] || release_guard_error "GITHUB_SHA must be a full commit SHA."
  [[ "$GITHUB_REPOSITORY" =~ ^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$ ]] ||
    release_guard_error "Unexpected GitHub repository name."
}

# GitHub matching-refs returns an empty array for missing tags (not a 404).
# Filter exact names: v1.7.0.4 must not be confused with v1.7.0.40.
# Annotated tags refer to Git tag objects, which may themselves point to other tags.
release_guard_tag_commit() {
  local tag="$1" ref object_type object_sha depth=0
  ref=$(gh api --paginate "repos/$GITHUB_REPOSITORY/git/matching-refs/tags/$tag" \
    --jq ".[] | select(.ref == \"refs/tags/$tag\") | [.object.type, .object.sha] | @tsv") ||
    return 1
  if [[ -z "$ref" ]]; then
    return 0
  fi
  [[ "$ref" != *$'\n'* ]] || release_guard_error "Multiple exact references found for $tag." || return 1
  IFS=$'\t' read -r object_type object_sha <<< "$ref"
  while [[ "$object_type" == tag ]]; do
    depth=$((depth + 1))
    (( depth <= 8 )) || release_guard_error "Too many nested annotated tags for $tag." || return 1
    ref=$(gh api "repos/$GITHUB_REPOSITORY/git/tags/$object_sha" \
      --jq '[.object.type, .object.sha] | @tsv') || return 1
    IFS=$'\t' read -r object_type object_sha <<< "$ref"
  done
  [[ "$object_type" == commit && "$object_sha" =~ ^[0-9a-f]{40}$ ]] ||
    release_guard_error "Tag $tag does not resolve to a valid commit." || return 1
  printf '%s\n' "$object_sha"
}

release_guard_require_matching_tag() {
  local tag="$1" resolved
  resolved=$(release_guard_tag_commit "$tag") || return 1
  if [[ -n "$resolved" && "$resolved" != "$GITHUB_SHA" ]]; then
    release_guard_error "Git tag $tag resolves to $resolved, but this build is $GITHUB_SHA."
    return 1
  fi
}

# Compare four-part MSIX versions numerically, not lexicographically.
release_guard_version_greater() {
  local a="$1" b="$2" i
  release_guard_valid_version "$a" && release_guard_valid_version "$b" || return 1
  local -a first second
  IFS=. read -r -a first <<< "$a"
  IFS=. read -r -a second <<< "$b"
  for i in 0 1 2 3; do
    local left=$((10#${first[i]})) right=$((10#${second[i]}))
    if (( left > right )); then return 0; fi
    if (( left < right )); then return 1; fi
  done
  return 1
}

# Used inside the shared stable-publication concurrency group. An older or equal
# release must not become /releases/latest, which is the stable App Installer feed.
# GitHub returns 404 when the repository does not yet have any latest release.
release_guard_require_new_stable() {
  local latest error_file
  error_file=$(mktemp) || return 1
  if latest=$(gh api "repos/$GITHUB_REPOSITORY/releases/latest" --jq '.tag_name' 2>"$error_file"); then
    rm -f "$error_file"
  elif grep -q 'HTTP 404' "$error_file"; then
    rm -f "$error_file"
    return 0
  else
    cat "$error_file" >&2
    rm -f "$error_file"
    release_guard_error "Could not verify the current latest release."
    return 1
  fi

  if [[ ! "$latest" =~ ^v[0-9]{1,5}(\.[0-9]{1,5}){3}$ ]]; then
    release_guard_error "Latest release has an unexpected tag: $latest."
    return 1
  fi
  if ! release_guard_version_greater "$VERSION" "${latest#v}"; then
    release_guard_error "Version $VERSION is not newer than the current latest $latest."
    return 1
  fi
}
