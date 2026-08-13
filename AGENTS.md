# Boreas Windows Agent Guide

## Start Here

Read [docs/README.md](docs/README.md). Read the document that owns the boundary
you will change before introducing code or dependencies.

## Non-Negotiable Invariants

- C# and WinUI 3 own presentation and user-initiated control only. They never
  parse, filter, route, buffer, or forward raw IP packets.
- The native service host owns Windows service lifecycle, Wintun adapter and
  session creation, route and interface setup, the Rust runtime, and all
  interaction with `boreas-core`.
- Wintun packets pass directly into `WintunDevice::from_session`. Do not create
  a managed packet relay, a second datapath, or a per-packet named-pipe bridge.
- The UI-to-service pipe carries bounded versioned control messages, immutable
  status, and typed errors only. It is authenticated and authorized before any
  command executes.
- The core owns L3 through L7 semantics and egress policy. Windows code owns
  OS integration, including the physical-interface bypass needed to prevent an
  upstream tunnel loop.
- Do not implement a WFP callout driver in v1. Wintun is the accepted raw-IP
  device boundary until a measured product requirement justifies another
  datapath and driver review.
- C# bindings are not built into UniFFI. Use an explicitly reviewed C ABI or
  the separately maintained `uniffi-bindgen-cs` only if a managed component
  genuinely needs direct native calls. Do not treat it as a core dependency by
  default.
- Keep service states a closed domain model and preserve structured shutdown:
  stop native work before closing the Wintun session or removing the adapter.

## Files This Repository Does Not Own

- `.github/workflows/sync-skills.yml` is vendored verbatim from the upstream
  `BTreeMap/SKILLs` repository. Never edit it here, including to satisfy a
  linter: fix the upstream workflow and let the vendored copy follow. The
  pinning policy in `.github/zizmor.yml` already exempts `BTreeMap/*` from the
  hash-pin rule so the file passes CI unmodified.
- `.github/skills` is a submodule. Its gitlink moves by the scheduled sync
  workflow; do not bump it by hand.
- `.claude/skills` is a symlink to `.github/skills`, so it is the same upstream
  content under another name. A skill written for this repository does not go
  there; see the section below for where it does go.

## Skills This Repository Owns

Repository-owned skills live in `.agents/skills/<name>/SKILL.md`.

| Skill | Read before |
| --- | --- |
| [bootstrap-windows-dev](.agents/skills/bootstrap-windows-dev/SKILL.md) | running any `dotnet` command when `command -v dotnet` finds nothing. Installs the SDK per-user under a temporary root, pinned from `global.json`, without sudo and without writing to `$HOME`. |

`.agents/skills/` is where OpenAI Codex looks: it scans that path in every
directory from the working directory up to the repository root. Claude Code
does not scan it, and reads `.claude/skills/` and `~/.claude/skills/` instead.
That split is why this table exists rather than a bare directory: a Claude Code
agent finds these skills by reading AGENTS.md, which it already loads, and then
opening the file.

`.agents/skills/` is chosen over `.claude/skills/` because the latter is a
symlink into a submodule this repository does not own. One location that half
the agents discover automatically beats a location no agent may write to.

## Boundary Rules

- [docs/core-contract.md](docs/core-contract.md) is a logical handoff contract,
  not an implemented ABI. Add exports only together with the matching
  `boreas-core` change and cross-boundary tests.
- The WinUI process is not a privileged service host. It may fail, restart, or
  update without taking packet handling down by accident.
- Never put credentials, packet payloads, keys, or unrestricted diagnostic
  logs on the local control pipe.

## Change Process

1. Read the owning Windows document and its linked core specification.
2. State the resource/lifecycle invariant and the cheapest check that could
   falsify it.
3. Keep Windows API effects in the host and pure decisions in the core.
4. Add unit and integration tests with each control, security, or ownership
   transition.
5. Run the narrowest .NET and Rust checks after projects exist, then the full
   device gate before merging.

For the present documentation-only repository, run `git diff --check` after
edits. Do not add an empty solution merely to make a build command exist.