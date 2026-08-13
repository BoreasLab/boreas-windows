#!/usr/bin/env bash
#
# Provision the Boreas Windows development toolchain in userspace, installing
# nothing globally and using no sudo.
#
# Run it, then build. Re-running is safe: every step checks its own postcondition
# and skips work that is already done.
#
#   bash .agents/skills/setup-boreas-windows/scripts/setup.sh
#   bash .agents/skills/setup-boreas-windows/scripts/setup.sh --reinstall
#
# Assumes bash 3.2 or later and POSIX coreutils, plus: git, curl, tar, gpg.
#
#
# THE ARCHITECTURE FACT: THERE ISN'T ONE
#
# The android toolchain has exactly one host-dependent fact, and names it once.
# This one has none, and that is worth stating so nobody adds it back.
#
# No architecture and no operating system is named anywhere below.
# dotnet-install.sh detects both: its --architecture defaults to <auto>, "the
# currently running OS architecture", and --os "should only be used when it's
# required to override the operating system that is detected by the script".
# A case statement here would be a second answer to a settled question, on a
# narrower table than upstream's, which also covers musl, FreeBSD, s390x,
# ppc64le and riscv64. Whatever upstream supports, this supports, and no
# platform is privileged because none is named.
#
#
# WHERE THINGS COME FROM
#
# Two sources, each the only sensible one for what it provides:
#
#   Microsoft    The SDK, through dotnet-install.sh, which also owns platform
#                detection and the global.json version rules.
#
#   This repo    Which SDK, through the global.json it already pins. Never
#                restated here; --jsonfile hands the file over, and the SDK's
#                own resolver reads it, rollForward included.
#
# So this script pins no SDK version. The only constants below are the three
# URLs of the installer and its signature, and those are the published stable
# ones rather than a build this script chose.
set -euo pipefail

# --- Pinned inputs -----------------------------------------------------------

readonly INSTALLER_URL=https://dot.net/v1/dotnet-install.sh
readonly SIGNATURE_URL=https://dot.net/v1/dotnet-install.sig
readonly PUBLIC_KEY_URL=https://dot.net/v1/dotnet-install.asc

# --- Boundary ----------------------------------------------------------------

log() { printf '%s\n' "$*" >&2; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<'USAGE'
usage: setup.sh [--reinstall] [--help]

  (no flags)   Provision or repair the toolchain. Safe to re-run.
  --reinstall  Delete the toolchain root first, then provision from scratch.

Everything is installed under $BOREAS_DEV_ROOT, default /tmp/boreas-windows-dev.
Nothing is installed globally and no command uses sudo.
USAGE
}

# --- Layout ------------------------------------------------------------------
#
# One name per path, derived once, so no step invents its own spelling.

ROOT="${BOREAS_DEV_ROOT:-/tmp/boreas-windows-dev}"
readonly ROOT
readonly DOWNLOADS="$ROOT/downloads"
readonly DOTNET_DIR="$ROOT/dotnet"
readonly DOTNET_BIN="$DOTNET_DIR/dotnet"
readonly NUGET_DIR="$ROOT/nuget"
readonly CLI_HOME="$ROOT/cli-home"
readonly GNUPG_DIR="$ROOT/gnupg"
readonly INSTALLER="$DOWNLOADS/dotnet-install.sh"
readonly ACTIVATE="$ROOT/activate.sh"

# The repository this script belongs to.
#
# Asked of git, from the script's own directory, rather than counted in `..`
# segments. A relative climb encodes where the file happens to sit today, so
# moving it changes which directory it calls the root, and it does so silently:
# the wrong answer is still a valid path.
#
# `readlink -f` would also resolve a symlinked script, and the android setup
# uses it, but it is GNU-only on the macOS this script claims to support. The
# directory is resolved with cd/pwd instead, which costs symlink resolution of
# the script file itself and buys portability.
repo_root() {
  local here
  command -v git >/dev/null ||
    { printf 'git is required to locate the repository\n' >&2; return 1; }
  here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
  git -C "$here" rev-parse --show-toplevel 2>/dev/null ||
    { printf 'not inside a git repository: %s\n' "$here" >&2; return 1; }
}

REPO="$(repo_root)"
readonly REPO
readonly GLOBAL_JSON="$REPO/global.json"
readonly TEST_PROJECT="$REPO/tests/Boreas.Ui.Tests/Boreas.Ui.Tests.csproj"

# --- Shared effects ----------------------------------------------------------

# Fetch to a temporary name and rename on success, so an interrupted download
# never leaves a short file that a later run would trust.
fetch() {
  local url="$1" target="$2"
  [ -f "$target" ] && return 0
  curl --disable --fail --silent --show-error --location \
    --retry 3 --retry-delay 2 --retry-connrefused \
    --output "$target.partial" "$url"
  mv -- "$target.partial" "$target"
}

# --- Steps -------------------------------------------------------------------

ensure_prerequisites() {
  local tool
  for tool in git curl tar gpg; do
    command -v "$tool" >/dev/null || die "missing required command: $tool"
  done
  mkdir -p "$DOWNLOADS" "$NUGET_DIR" "$CLI_HOME"
}

# The installer is a shell script this script then runs, so it is checked
# against Microsoft's signature before it is trusted. GNUPGHOME points inside
# the root, so importing the key neither reads nor writes the user's keyring.
ensure_installer() {
  # Downloads are cached, but the signature is checked on every run rather than
  # once at download time. Verifying only what was just fetched would leave a
  # cached installer trusted forever on the strength of a check that happened
  # in some earlier run, which is the same as not checking it.
  fetch "$INSTALLER_URL" "$INSTALLER"
  fetch "$SIGNATURE_URL" "$DOWNLOADS/dotnet-install.sig"
  fetch "$PUBLIC_KEY_URL" "$DOWNLOADS/dotnet-install.asc"

  mkdir -p "$GNUPG_DIR"
  chmod 700 "$GNUPG_DIR"
  GNUPGHOME="$GNUPG_DIR" gpg --quiet --import "$DOWNLOADS/dotnet-install.asc"
  GNUPGHOME="$GNUPG_DIR" gpg --verify \
    "$DOWNLOADS/dotnet-install.sig" "$INSTALLER" 2>/dev/null ||
    die "dotnet-install.sh does not match its signature; refusing to run it.
  If the cached copy is merely stale, --reinstall re-fetches both."
}

# The postcondition, and the only definition of "already installed" worth
# having: the SDK's own resolver reads global.json, applies rollForward, and
# fails when nothing installed satisfies it. Parsing the pin here would be a
# second implementation of that rule.
satisfies_pin() {
  [ -x "$DOTNET_BIN" ] || return 1
  (cd -- "$REPO" && "$DOTNET_BIN" --version) >/dev/null 2>&1
}

ensure_dotnet() {
  satisfies_pin && return 0

  ensure_installer
  log "installing the SDK pinned by global.json"
  # --jsonfile takes the version from the pin the repository already states.
  # No --architecture and no --os: both default to detecting this machine.
  bash "$INSTALLER" \
    --jsonfile "$GLOBAL_JSON" \
    --install-dir "$DOTNET_DIR" \
    --no-path >&2
}

# Generated rather than documented, so it cannot drift from the root it
# describes. POSIX shell, because the shell sourcing it may be zsh.
write_activation() {
  cat >"$ACTIVATE.partial" <<ACTIVATION
# Generated by setup.sh. Source it: source $ACTIVATE
export DOTNET_ROOT="$DOTNET_DIR"
export NUGET_PACKAGES="$NUGET_DIR"
export DOTNET_CLI_HOME="$CLI_HOME"
export DOTNET_NOLOGO=true
export DOTNET_CLI_TELEMETRY_OPTOUT=true

# A generated executable prefers DOTNET_ROOT_<ARCH> over DOTNET_ROOT, so one
# inherited from an earlier install silently wins over the line above and the
# runtime is looked for in the wrong place. Clearing the family is the fix that
# needs no architecture name: whichever member was set, it is gone.
unset DOTNET_ROOT_X64 DOTNET_ROOT_X86 DOTNET_ROOT_ARM64 DOTNET_ROOT_ARM

# Prepended rather than replacing PATH: this toolchain is one compiler, not a
# hermetic build environment, and the surrounding shell keeps its own tools.
# Guarded so that sourcing twice does not stack two copies.
case ":\$PATH:" in
  *":$DOTNET_DIR:"*) ;;
  *) PATH="$DOTNET_DIR:\$PATH" ;;
esac
export PATH
ACTIVATION
  mv -- "$ACTIVATE.partial" "$ACTIVATE"
}

# Assert the postconditions rather than trusting that the steps above ran.
verify() {
  [ -x "$DOTNET_BIN" ] || die "no dotnet at $DOTNET_BIN"
  [ -f "$ACTIVATE" ] || die "no activation script at $ACTIVATE"
  satisfies_pin || die "the installed SDK does not satisfy $GLOBAL_JSON"
}

# --- Entry point -------------------------------------------------------------

main() {
  local reinstall=0

  while [ "$#" -gt 0 ]; do
    case "$1" in
      --reinstall) reinstall=1 ;;
      --help | -h) usage; return 0 ;;
      *) usage >&2; die "unknown argument: $1" ;;
    esac
    shift
  done

  [ "$reinstall" -eq 0 ] || { log "removing $ROOT"; rm -rf -- "$ROOT"; }

  [ -f "$GLOBAL_JSON" ] || die "not a Boreas Windows checkout: $GLOBAL_JSON is missing"

  log "root $ROOT"
  ensure_prerequisites
  ensure_dotnet
  write_activation
  verify

  cat <<READY

Toolchain ready under $ROOT ($(cd -- "$REPO" && "$DOTNET_BIN" --version), $("$DOTNET_BIN" --info | awk -F': *' '/^ *RID:/ { print $2; exit }')).

Run the core law suite:

  source $ACTIVATE
  dotnet test $TEST_PROJECT --configuration Release
READY
}

main "$@"
