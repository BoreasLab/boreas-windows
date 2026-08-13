---
name: setup-dotnet
description: >-
  Installs a self-contained .NET SDK toolchain for this repository under a
  temporary directory, with no system-wide package, no sudo, and no writes to
  $HOME, then builds and runs the core law suite with it. Pins the SDK from the
  repository's own global.json, verifies the installer's GPG signature, and
  redirects the NuGet cache and CLI state out of the home directory. Use when a
  machine has no dotnet on PATH, when an existing install must not be disturbed,
  when a clean-room restore is needed to reproduce a CI result, or when the
  toolchain has to be discarded afterwards. Do not use for building the WinUI
  application, which requires Windows.
license: MIT
compatibility: >-
  Linux or macOS, x64 or arm64. Requires curl, gpg, tar, and outbound HTTPS to
  dot.net and builds.dotnet.microsoft.com. Needs about 750 MB of free space.
metadata:
  when_to_use: >-
    Invoke before any dotnet command in this repository when `command -v dotnet`
    finds nothing, or when the caller asks for an isolated or throwaway
    toolchain.
  verified_on: "linux-arm64, .NET SDK 10.0.100, 2026-08-13"
---

# Set up .NET in userspace

Toolchain lives in one directory. Delete that directory, nothing remains.

## Scope

This repository is a Windows control client with an OS-independent functional
core. Two build targets, only one of which works here:

| Project | Target framework | Builds on Linux or macOS |
| --- | --- | --- |
| `tests/Boreas.Ui.Tests` | `net10.0` | Yes. This is the whole point. |
| `src/Boreas.Ui` | `net10.0-windows...` | No. WinUI 3, Windows only. |

The test project compiles the functional core from source rather than
referencing the app, so it exercises `Contracts`, `Presentation`, and the
channel abstraction without a Windows SDK. A `Microsoft.UI` reference reaching
that core fails the build here, which is the intended boundary check.

## Procedure

### 1. Choose a root and derive the repository path

Every later step reads these two variables. Set both in one shell.

<environment_setup>
REPO="$(git rev-parse --show-toplevel)"
ROOT="${TMPDIR:-/tmp}/boreas-windows-dev"
mkdir -p "$ROOT/download"
</environment_setup>

`ROOT` may be any writable path. Under `/tmp` it is volatile by design: a
reboot discards it and this procedure is re-run.

### 2. Fetch the installer and verify its signature

The installer is a shell script fetched over the network, so verify it before
running it.

<fetch_and_verify>
cd "$ROOT/download"
curl -fsSL -o dotnet-install.sh  https://dot.net/v1/dotnet-install.sh
curl -fsSL -o dotnet-install.sig https://dot.net/v1/dotnet-install.sig
curl -fsSL -o dotnet-install.asc https://dot.net/v1/dotnet-install.asc

export GNUPGHOME="$ROOT/gnupg"
mkdir -p "$GNUPGHOME" && chmod 700 "$GNUPGHOME"
gpg --quiet --import dotnet-install.asc
gpg --verify dotnet-install.sig dotnet-install.sh
</fetch_and_verify>

Expect `Good signature from "Microsoft DevUXTeamPrague <devuxteamprague@microsoft.com>"`
and this primary key fingerprint:

<expected_fingerprint>
2B93 0AB1 228D 11D5 D7F6  B6AC B9CF 1A51 FC7D 3ACF
</expected_fingerprint>

`GNUPGHOME` is set inside `ROOT` so the import does not create or modify
`$HOME/.gnupg`. The `[unknown]` trust marker in the output is expected and does
not weaken the check: the signature is still verified, and the command exits 0.

If the fingerprint differs, stop. Do not run the script.

### 3. Install, taking the version from the repository pin

<install>
bash "$ROOT/download/dotnet-install.sh" \
  --jsonfile "$REPO/global.json" \
  --install-dir "$ROOT/dotnet" \
  --no-path
</install>

`--jsonfile` reads `sdk.version` from `global.json`, so the installed SDK is the
one the repository pins and cannot drift from it. Do not substitute
`--channel`: a channel installs whatever is newest in that band, which is a
second, competing statement of the version.

`--no-path` suppresses the script's edit to `PATH` for its own process. The
script never edits a shell profile and never sets `DOTNET_ROOT`, so step 4 is
mandatory rather than a convenience.

Expect `Installed version is 10.0.100` and `Installation finished successfully`.

### 4. Export four variables

<environment_variables>
export DOTNET_ROOT="$ROOT/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export NUGET_PACKAGES="$ROOT/nuget"
export DOTNET_CLI_HOME="$ROOT/cli-home"

export DOTNET_NOLOGO=true
export DOTNET_CLI_TELEMETRY_OPTOUT=true
</environment_variables>

Each earns its place:

| Variable | Without it |
| --- | --- |
| `DOTNET_ROOT` | `dotnet build` still works. `dotnet test` fails: see Gotchas. |
| `PATH` | `dotnet` is not a command. |
| `NUGET_PACKAGES` | Packages restore to `$HOME/.nuget/packages`, contaminating the home directory this procedure exists to leave alone. |
| `DOTNET_CLI_HOME` | First-run sentinels and workload state land in `$HOME/.dotnet`. |

Set these per shell. Do not append them to a shell profile: a profile export
outlives the directory it points at, and a stale `DOTNET_ROOT` is harder to
diagnose than an unset one.

### 5. Verify

<verification>
dotnet --version                     # expect the version pinned in global.json
dotnet --list-sdks                   # expect exactly one, under $ROOT/dotnet
dotnet test "$REPO/tests/Boreas.Ui.Tests/Boreas.Ui.Tests.csproj" -c Release
</verification>

Expect `Passed!` with `Failed: 0`. A restore into an empty `NUGET_PACKAGES` is a
clean-room reproduction of what CI does, so a green result here is meaningful
evidence and not just a cache replay.

Confirm containment, which should print nothing:

<containment_check>
find "$HOME/.dotnet" "$HOME/.nuget" -newermt '-5 minutes' 2>/dev/null
</containment_check>

### 6. Tear down

<teardown>
rm -rf "$ROOT"
</teardown>

Removes the SDK, the NuGet cache, the CLI state, and the GPG keyring together.
Build outputs stay in the repository and are removed with
`git clean -xdf -- '*/bin' '*/obj'` if they are also unwanted.

## Gotchas

- **`dotnet build` succeeds without `DOTNET_ROOT`; `dotnet test` does not.**
  The failure is not a build error and does not name the missing variable:

  <symptom>
  You must install .NET to run this application.
  App: .../Boreas.Ui.Tests
  .NET location: Not found
  </symptom>

  Cause: this repository runs xunit v3 on Microsoft.Testing.Platform, so the
  test project is its own native executable. A generated executable resolves the
  runtime through `DOTNET_ROOT`, never through `PATH`, so an SDK outside the
  default location is invisible to it.

- **`DOTNET_ROOT_<ARCH>` overrides `DOTNET_ROOT`.** On arm64,
  `DOTNET_ROOT_ARM64` wins; on x64, `DOTNET_ROOT_X64`. An inherited
  arch-specific variable silently defeats step 4. Check with `env | grep
  DOTNET_ROOT` when a correct-looking `DOTNET_ROOT` is ignored.

- **The installer does not resolve native dependencies.** The SDK needs ICU and
  OpenSSL present. Absent them, `dotnet` fails at startup rather than during
  install. Installing those needs a package manager, which needs root, which is
  the one thing this procedure cannot do. Set
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` to run without ICU, accepting that
  culture-sensitive behaviour changes.

- **`--jsonfile` reads `sdk.version` only.** It ignores `rollForward`, which is
  what makes it deterministic. `global.json` still governs which installed SDK a
  build selects.

- **`actionlint` cannot run on arm64 here.** `.github/scripts/actionlint.sh`
  pins the `linux_amd64` archive with a matching checksum, so on arm64 it fails
  with `cannot execute binary file: Exec format error`. This is not a workflow
  defect; CI runs on x64. `.github/scripts/zizmor.sh` works on both.

- **Repository-owned skills live in `.agents/skills/`, not `.claude/skills/`.**
  The latter is a symlink into the `.github/skills` submodule, vendored from an
  upstream repository this one does not own, so a skill written here would be a
  change to somebody else's repository. Claude Code does not scan
  `.agents/skills/`, so AGENTS.md points at it by hand; read the skill directly
  when working in this repository under an agent that has not loaded it.

## Cost

Roughly 700 MB: about 650 MB of SDK, plus the restored NuGet cache, which is
about 45 MB for this repository's dependency set.
