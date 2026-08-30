#!/usr/bin/env bash
#
# Fetch the two native libraries this application links against, verify them,
# and lay them out where the build expects.
#
# THE PINS ARE THE POINT. api/artifacts.md: pin the exact tag, never "latest",
# "so your build is reproducible and an ABI change is something you adopt
# rather than something that happens to you". Both pins live at the top of this
# file, and changing one is a commit somebody reviews.
#
# WHERE THINGS COME FROM
#   boreas.dll  BoreasLab/boreas-core, at the tag Directory.Build.props pins,
#               built by GitHub Actions from a commit on main and signed with
#               build provenance. Never built here: it links BoringSSL and
#               compiles C in ring, so it can only be built on Windows against
#               MSVC.
#   wintun.dll  wintun.net, Jason A. Donenfeld's signed redistributable. "The
#               below signed DLLs are the only supported way of distributing
#               Wintun", so this is the only correct source and a self-build is
#               not an option.
#
# usage: fetch-native.sh [--help]
set -euo pipefail

# --- Pinned inputs -----------------------------------------------------------

# THE CORE TAG IS NOT HERE. It lives in Directory.Build.props, where MSBuild
# reads it too, and this reads it from there. One pin: the version stamped into
# the application's about window and the archive downloaded here cannot name
# different releases, because there is only one place either could read.
#
# The digests do live here. They are properties of an archive rather than of the
# product, and the file that declares what the product is has no business
# carrying them.
readonly BOREAS_SHA256="5633cfe69c7d8757f8ae93fa608b67a654c98f7e9d84bda77506dfede8bcf2d2"
readonly BOREAS_REPO="BoreasLab/boreas-core"

readonly WINTUN_VERSION="0.14.1"
readonly WINTUN_SHA256="07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51"

# --- Boundary ----------------------------------------------------------------

log() { printf '%s\n' "$*" >&2; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

usage() {
  cat >&2 <<'USAGE'
usage: fetch-native.sh [--help]

Downloads boreas.dll and wintun.dll at the versions pinned in this file,
verifies both, and extracts them under native/.

Requires gh, and that is deliberate: a checksum only proves the file matches a
list that came from the same place the file did. Build provenance is the check
that means something, and it is the one api/artifacts.md says to run in CI and
not just once by hand.
USAGE
}

# --- Layout ------------------------------------------------------------------

repo_root() {
  git -C "$(dirname -- "${BASH_SOURCE[0]}")" rev-parse --show-toplevel 2>/dev/null \
    || die "fetch-native.sh must run inside the repository."
}

REPO="$(repo_root)"
readonly REPO

# The one pin, read from the file that declares it.
BOREAS_TAG="$(sed -n 's|.*<BoreasCoreTag>\(.*\)</BoreasCoreTag>.*|\1|p' "$REPO/Directory.Build.props")"
readonly BOREAS_TAG
[ -n "$BOREAS_TAG" ] || die "Directory.Build.props declares no <BoreasCoreTag>."

# Parsed, not merely present: the tag names a URL and a cache directory below,
# and the version stripped out of it just under here assumes this shape anyway.
[[ "$BOREAS_TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
  || die "<BoreasCoreTag>$BOREAS_TAG</BoreasCoreTag> is not a boreas-core release tag."

# The archive inside a release is named for the base version, not the tag: a
# pre-release tag carries the build stamp and the archive does not. Derived
# rather than pinned separately, so the two cannot come to name different
# releases.
BOREAS_VERSION="${BOREAS_TAG#v}"
BOREAS_VERSION="${BOREAS_VERSION%%-*}"
readonly BOREAS_VERSION

readonly NATIVE="$REPO/native"
readonly DOWNLOADS="$NATIVE/downloads"
readonly BOREAS_DIR="$NATIVE/boreas"
readonly WINTUN_DIR="$NATIVE/wintun"

# Cached under the tag. The archive is named for the base version, so every
# pre-release of one base shares a file name, and `fetch` keeps whatever is
# already there -- moving the pin would otherwise check yesterday's download
# against today's digest.
readonly BOREAS_ARCHIVE="$DOWNLOADS/$BOREAS_TAG/boreas-$BOREAS_VERSION-windows.zip"
readonly WINTUN_ARCHIVE="$DOWNLOADS/wintun-$WINTUN_VERSION.zip"

# --- Shared effects ----------------------------------------------------------

fetch() {
  local url="$1" into="$2"

  [ -f "$into" ] && return 0

  mkdir -p -- "$(dirname -- "$into")"
  log "fetching $(basename -- "$into")"

  # Downloaded beside its destination and renamed, so an interrupted transfer
  # leaves a partial file that is never mistaken for a complete one.
  curl --fail --silent --show-error --location \
    --retry 3 --retry-delay 2 --retry-connrefused \
    --output "$into.partial" "$url"

  mv -- "$into.partial" "$into"
}

# Checked on every run rather than once at download time. Verifying only what
# was just fetched would leave a cached archive trusted forever on the strength
# of a check that happened in some earlier run, which is the same as not
# checking it.
verify_sha256() {
  local file="$1" expected="$2"

  printf '%s  %s\n' "$expected" "$file" | sha256sum --check --status \
    || die "$(basename -- "$file") does not match its pinned checksum.
  Delete native/downloads and run this again, or the pin in this file is stale."
}

# --- Steps -------------------------------------------------------------------

ensure_prerequisites() {
  for tool in curl sha256sum unzip git; do
    command -v "$tool" >/dev/null 2>&1 || die "$tool is required and was not found."
  done

  command -v gh >/dev/null 2>&1 || die "gh is required to verify build provenance.
  Install it from https://cli.github.com, then run this again.
  A checksum alone only proves the archive matches a list that came from the
  same place the archive did; provenance proves which workflow built it."

  # Separated from the verification below on purpose. "You are not signed in"
  # and "this archive is not what it claims to be" are entirely different
  # problems, and a script that reports the first as the second sends its
  # reader to look for a supply-chain compromise that is not there.
  gh auth status >/dev/null 2>&1 || die "gh is installed but not signed in.
  Run: gh auth login
  In CI, set GH_TOKEN instead."
}

ensure_boreas() {
  fetch \
    "https://github.com/$BOREAS_REPO/releases/download/$BOREAS_TAG/boreas-$BOREAS_VERSION-windows.zip" \
    "$BOREAS_ARCHIVE"

  verify_sha256 "$BOREAS_ARCHIVE" "$BOREAS_SHA256"

  # Provenance answers "was this built by that workflow, from that commit",
  # which the checksum cannot. Also on every run, for the same reason.
  gh attestation verify "$BOREAS_ARCHIVE" --repo "$BOREAS_REPO" >/dev/null \
    || die "boreas-$BOREAS_VERSION-windows.zip has no valid build provenance for $BOREAS_REPO.
  Refusing to unpack it."

  [ -f "$BOREAS_DIR/runtimes/win-x64/native/boreas.dll" ] \
    && [ -f "$BOREAS_DIR/runtimes/win-arm64/native/boreas.dll" ] && return 0

  rm -rf -- "$BOREAS_DIR"
  mkdir -p -- "$BOREAS_DIR"
  unzip -q -o "$BOREAS_ARCHIVE" -d "$BOREAS_DIR"
}

ensure_wintun() {
  fetch "https://www.wintun.net/builds/wintun-$WINTUN_VERSION.zip" "$WINTUN_ARCHIVE"

  # No provenance to check: wintun.net publishes no attestation. What vouches
  # for these DLLs is the Authenticode signature on each one, which Windows
  # checks when the driver loads and which nothing here can check from Linux.
  # The pin plus this digest is what is available before that point.
  verify_sha256 "$WINTUN_ARCHIVE" "$WINTUN_SHA256"

  [ -f "$WINTUN_DIR/bin/amd64/wintun.dll" ] \
    && [ -f "$WINTUN_DIR/bin/arm64/wintun.dll" ] && return 0

  rm -rf -- "$WINTUN_DIR"
  mkdir -p -- "$NATIVE"
  unzip -q -o "$WINTUN_ARCHIVE" -d "$NATIVE"
}

# Asserts rather than trusts. Every path below is one the build reads by name,
# so a layout change upstream fails here instead of at link time on a machine
# nobody is watching.
verify() {
  local missing=0 path

  for path in \
    "$BOREAS_DIR/runtimes/win-x64/native/boreas.dll" \
    "$BOREAS_DIR/runtimes/win-arm64/native/boreas.dll" \
    "$BOREAS_DIR/include/boreas.h" \
    "$WINTUN_DIR/bin/amd64/wintun.dll" \
    "$WINTUN_DIR/bin/arm64/wintun.dll"
  do
    [ -f "$path" ] || { log "missing: ${path#"$REPO"/}"; missing=1; }
  done

  [ "$missing" -eq 0 ] || die "the archives did not contain what this build expects."

  # BOREAS_ABI_VERSION is the number the startup check compares against
  # boreas_abi_version(). If the header ships a different one, the C#
  # declarations were written against a different ABI and the check would
  # compare the wrong number and pass.
  local shipped
  shipped="$(sed -n 's/^#define BOREAS_ABI_VERSION \([0-9]*\)u\?$/\1/p' "$BOREAS_DIR/include/boreas.h")"

  [ "$shipped" = "1" ] || die "the pinned archive ships BOREAS_ABI_VERSION $shipped, but
  Boreas.Interop was written against 1. Update Boreas.CompiledAbiVersion and the
  declarations together, or pin an archive that matches."
}

# --- Entry point -------------------------------------------------------------

main() {
  case "${1-}" in
    --help | -h) usage; exit 0 ;;
    "") ;;
    *) usage; die "unknown argument: $1" ;;
  esac

  ensure_prerequisites
  ensure_boreas
  ensure_wintun
  verify

  cat >&2 <<READY

Native libraries ready under native/.

  boreas   $BOREAS_TAG (ABI 1)
  wintun   $WINTUN_VERSION

The build picks the right architecture from -p:Platform; nothing is copied by
hand.
READY
}

main "$@"
