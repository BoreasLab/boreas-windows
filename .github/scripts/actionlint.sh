#!/usr/bin/env bash
#
# Fetch one exact actionlint release, verify it against a checksum recorded by
# the caller, and lint every workflow.
#
# Assumes bash 4+ and GNU coreutils: the ubuntu-latest runner image, and a
# development machine of either architecture.
#
# The checksum is the point. Pinning a version says which artifact we asked
# for; verifying the digest says which artifact we got. Without the second, a
# replaced release asset runs as root in CI with the repository checked out.
set -euo pipefail

# The parse boundary. Every value is required and none is re-checked below.
readonly VERSION="${ACTIONLINT_VERSION:?ACTIONLINT_VERSION must be set}"

# ONE DIGEST PER ARCHITECTURE, because the digest is a property of a file and
# there is a different file per machine.
#
# Both are recorded so a laptop checks what a runner checks. Pinning only the
# runner's architecture did not weaken the check - it removed the linter from
# every machine that is not a runner, which is exactly where a finding is cheap
# to act on. It cost one shellcheck warning found in CI that could have been
# found before the push.
#
# Adding a platform means adding its digest. There is deliberately no fallback
# to an unverified download.
case "$(uname -s)/$(uname -m)" in
  Linux/x86_64)
    readonly ARCH="amd64"
    readonly SHA256="${ACTIONLINT_SHA256_AMD64:?ACTIONLINT_SHA256_AMD64 must be set}"
    ;;
  Linux/aarch64 | Linux/arm64)
    readonly ARCH="arm64"
    readonly SHA256="${ACTIONLINT_SHA256_ARM64:?ACTIONLINT_SHA256_ARM64 must be set}"
    ;;
  *)
    printf 'error: no actionlint digest is pinned for %s/%s.\n' "$(uname -s)" "$(uname -m)" >&2
    printf '       Add one beside the others rather than skipping the check.\n' >&2
    exit 1
    ;;
esac

readonly ARCHIVE="actionlint_${VERSION}_linux_${ARCH}.tar.gz"
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
