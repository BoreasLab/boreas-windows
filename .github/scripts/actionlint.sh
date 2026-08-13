#!/usr/bin/env bash
#
# Fetch one exact actionlint release, verify it against a checksum recorded by
# the caller, and lint every workflow.
#
# Assumes bash 4+ and GNU coreutils (the ubuntu-latest runner image).
#
# The checksum is the point. Pinning a version says which artifact we asked
# for; verifying the digest says which artifact we got. Without the second, a
# replaced release asset runs as root in CI with the repository checked out.
set -euo pipefail

# The parse boundary. Both are required and neither is re-checked below.
readonly VERSION="${ACTIONLINT_VERSION:?ACTIONLINT_VERSION must be set}"
readonly SHA256="${ACTIONLINT_SHA256:?ACTIONLINT_SHA256 must be set}"

readonly ARCHIVE="actionlint_${VERSION}_linux_amd64.tar.gz"
readonly URL="https://github.com/rhysd/actionlint/releases/download/v${VERSION}/${ARCHIVE}"

# Registered before the resource is acquired, so an early failure still cleans
# up. This is the shell's RAII.
workspace="$(mktemp -d)"
readonly workspace
cleanup() { rm -rf "$workspace"; }
trap cleanup EXIT

main() {
  curl --fail --silent --show-error --location \
    --retry 3 --retry-delay 2 --retry-connrefused \
    --output "$workspace/$ARCHIVE" "$URL"

  # Verify before extracting: an archive that fails the check is never unpacked
  # and never executed.
  printf '%s  %s\n' "$SHA256" "$workspace/$ARCHIVE" | sha256sum --check --status

  tar --extract --gzip --file "$workspace/$ARCHIVE" --directory "$workspace" actionlint

  # -shellcheck= leaves the runner's ShellCheck in play for the run: blocks.
  "$workspace/actionlint" -color
}

main "$@"
