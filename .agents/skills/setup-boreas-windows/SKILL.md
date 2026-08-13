---
name: setup-boreas-windows
description: >-
  Sets up the boreas-windows development environment on Linux or macOS by
  installing a self-contained .NET SDK toolchain under a temporary directory,
  with no system-wide package, no sudo, and no writes to $HOME, then builds and
  runs the core law suite with it. Pins the SDK from the repository's own
  global.json, verifies the installer's GPG signature, and redirects the NuGet
  cache and CLI state out of the home directory. Use when a machine has no
  dotnet on PATH, when an existing install must not be disturbed, when a
  clean-room restore is needed to reproduce a CI result, or when the toolchain
  has to be discarded afterwards. Do not use for building the WinUI
  application, which requires Windows.
license: MIT
compatibility: >-
  Runs wherever dotnet-install.sh runs, on any architecture it supports.
  Requires bash, curl, git, and gpg, and outbound HTTPS to dot.net and
  builds.dotnet.microsoft.com. Needs about 750 MB of free space.
metadata:
  when_to_use: >-
    Invoke before any dotnet command in this repository when `command -v dotnet`
    finds nothing, or when the caller asks for an isolated or throwaway
    toolchain.
  verified_on: "linux-arm64, .NET SDK 10.0.100, 2026-08-13"
---

# Set up the boreas-windows dev environment

Toolchain lives in one directory. Delete that directory, nothing remains.

Runs on Linux and macOS, and builds the OS-independent core. The WinUI
application needs Windows and is not built by anything here; see Scope.

## Registry

| Name | Path |
| --- | --- |
| `setup` | [scripts/setup.sh](scripts/setup.sh) |

## Do this

Run it, then build. From anywhere in the repository:

<procedure>
bash .agents/skills/setup-boreas-windows/scripts/setup.sh
source /tmp/boreas-windows-dev/activate.sh
dotnet test tests/Boreas.Ui.Tests/Boreas.Ui.Tests.csproj --configuration Release
</procedure>

Expect `Passed!` with `Failed: 0`. `setup` prints those last two lines itself
when it finishes, so neither has to be remembered.

## Surface

One flag, because one is enough:

| Invocation | Effect |
| --- | --- |
| `setup.sh` | Provision or repair the toolchain. Safe to re-run. |
| `setup.sh --reinstall` | Delete the toolchain root first, then provision from scratch. |

The root is `$BOREAS_DEV_ROOT`, default `/tmp/boreas-windows-dev`. An
environment variable rather than a flag, so a mistyped path cannot reach
`rm -rf` through an argument nobody checked.

Re-running is safe from any state, because each step checks its own
postcondition rather than trusting a marker: a missing SDK is reinstalled, a
cached installer is re-verified rather than trusted, a stale `activate.sh` is
regenerated, and a complete toolchain is left alone. `--reinstall` exists for
the case none of that can reach, which is a root corrupted in a way its own
postconditions still accept.

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

## What it contains, and why each part

Four directories under the root, each because something would otherwise land in
`$HOME` and outlive the teardown. A fifth, `downloads/`, caches the installer so
a re-run fetches nothing:

| Path | Redirects | Without it |
| --- | --- | --- |
| `dotnet/` | `DOTNET_ROOT` | `dotnet` is not a command, and `dotnet test` fails even when it is. See Gotchas. |
| `nuget/` | `NUGET_PACKAGES` | Packages restore to `$HOME/.nuget/packages`. |
| `cli-home/` | `DOTNET_CLI_HOME` | First-run sentinels and workload state land in `$HOME/.dotnet`. |
| `gnupg/` | `GNUPGHOME` | Importing the signing key modifies the user's keyring. |

Confirm containment at any time. This prints nothing when the environment is
behaving:

<containment_check>
find "$HOME/.dotnet" "$HOME/.nuget" -newermt '-5 minutes'
</containment_check>

`-newermt` is GNU find. On macOS use `find "$HOME/.dotnet" "$HOME/.nuget" -newer
"$(mktemp)"` after touching a reference file, or skip the check.

## Design notes

Worth knowing before editing `setup`.

**No platform table.** `setup` names no architecture and no operating system.
`dotnet-install.sh` detects both, and its `--architecture` defaults to `<auto>`,
"the currently running OS architecture". A detector here would be a second
answer to a question upstream already answers on a wider table, since upstream
also knows musl, FreeBSD, s390x, ppc64le and riscv64. Whatever upstream
supports, this supports, and no platform is privileged because none is named.

**No version parsing.** `--jsonfile` hands `global.json` to the installer, and
the check for "is a good SDK already here" runs `dotnet --version` inside the
repository and reads its exit status. The SDK's own resolver reads the pin,
including `rollForward`. Parsing the JSON here would be a second implementation
of that rule.

**Generation over instruction.** `activate.sh` is written by `setup`, so it
cannot drift from the root it describes. It is POSIX shell, because the shell
sourcing it may be zsh, and it is written to a temporary name and renamed, so a
reader never sees a half-written file.

**Consistent with setup-boreas-android.** Same shape: `--reinstall` as the only
flag, `$BOREAS_DEV_ROOT` for the root, one `ensure_*` step per postcondition, a
`verify` that asserts rather than trusts, and a closing block naming the exact
commands to run next. The android script names its one host-dependent fact in
one function; this one has none to name, which is why the header says so.

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
  default location is invisible to it. Sourcing `activate.sh` is what sets it.

- **`DOTNET_ROOT_<ARCH>` outranks `DOTNET_ROOT`.** An arch-specific variable
  inherited from an earlier install silently wins, and the runtime is looked for
  somewhere else. `activate.sh` clears the whole family rather than setting the
  matching member, which is the fix that needs no architecture name.

- **The installer does not resolve native dependencies.** The SDK needs ICU and
  OpenSSL present. Absent them, `dotnet` fails at startup rather than during
  install. Installing those needs a package manager, which needs root, which is
  the one thing this procedure cannot do. Set
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` to run without ICU, accepting that
  culture-sensitive behaviour changes.

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
