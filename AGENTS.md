# Boreas Windows Agent Guide

## Start Here

Read `boreas-core/api/` before this file, in the order its README gives. Then
read the section below that owns the boundary you are changing.

## Where The Contract Lives

`boreas-core/api/` is the contract and it is meant to be sufficient. Read it
before this file: where the two disagree, `api/` wins, and a gap in `api/` is a
defect to report there rather than work around here.

`docs/` in this repository predates the C ABI. It describes a design in which a
Rust service host consumed the core through `WintunDevice::from_session` and C#
never touched a packet. That is not what is built: the C boundary is the
interface, `api/windows.md` is written for a C# host, and `src/Boreas.Interop`
is that host. Treat `docs/` as history until it is rewritten - except
[docs/releasing.md](docs/releasing.md), which is current and describes the
release scheme this repository actually runs.

## Non-Negotiable Invariants

- **The three marshalling traps are held by law tests, not by care.** Every
  `bool` crossing the ABI is `[MarshalAs(UnmanagedType.U1)]`, every `size_t` is
  `nuint`, every string is UTF-8. `tests/Boreas.Interop.Tests` asserts the
  offsets *and* the widths, because the `size_t` trap moves no offset at all -
  the bytes it loses fall into padding the next field already needed.
- **Never block in an `[UnmanagedCallersOnly]` callback.** It runs in the CLR's
  cooperative GC mode, so a thread parked there stops garbage collection
  process-wide. `recv` waits on `[readWaitEvent, quitEvent]` with a bounded
  timeout and returns `0` for "nothing yet". `TunDevice.ReceiveTimeout` is
  asserted to stay under the documented ceiling.
- **Never let an exception escape a callback.** An unhandled managed exception
  crossing into native code crashes the host process. Every body is wrapped and
  every failure becomes the contract's own negative value.
- **Teardown is shutdown, join, free, and the Wintun session ends after all
  three.** A thread blocked in `next_event` holds a borrow of the handle. The
  ring is owned by the device vtable, so its disposal happens in the release
  callback, which is what makes the ordering structural rather than remembered.
- **The MTU is one value.** It reaches `BoreasDevice.mtu` and `BoreasConfig.mtu`
  from the same `Mtu`. Two fields that must agree are a bug waiting; a sustained
  non-zero `paths_reported` is the only symptom when they do not.
- **`protect` never returns success without doing something.** An unprotected
  socket works perfectly until the tunnel comes up, then re-enters it. IPv4
  takes the interface index in network byte order and IPv6 in host byte order,
  and `BypassLaws` asserts that against the wire bytes.
- **Configuration is parsed, not validated.** Six of the ten `BOREAS_CONFIG`
  causes have no spelling in `Boreas.Interop.Tunnel`. Add a field by making the
  invalid combination unrepresentable, not by adding a check.
- The core owns L3 through L7 semantics and egress policy. Windows code owns OS
  integration: the adapter, the interface configuration, the bypass, and the
  trust store.
- Do not implement a WFP callout driver in v1. Wintun is the accepted raw-IP
  device boundary until a measured product requirement justifies another
  datapath and driver review.
- **The native libraries are pinned and verified, never committed.**
  `scripts/fetch-native.sh` downloads `boreas.dll` at an exact tag and the
  official signed `wintun.dll`, checks both digests and the build provenance on
  every run, and the build refuses without them. Never pin "latest" and never
  add a way to skip the provenance check.
- **This process must be elevated.** Creating a Wintun adapter and configuring
  an interface both require it. Whether the elevated process is the WinUI app or
  a service beside it is a packaging decision; `ITunnelHost` is the seam it
  moves at, and nothing in front of that seam touches the operating system.
- Keep service states a closed domain model. Adding a case must fail the build
  at every site that eliminates it, which is what `CS8509` as an error buys.

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
| [setup-boreas-windows](.agents/skills/setup-boreas-windows/SKILL.md) | running any `dotnet` command when `command -v dotnet` finds nothing. Installs the SDK per-user under a temporary root, pinned from `global.json`, without sudo and without writing to `$HOME`. |

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

- **Every P/Invoke declaration is written against `ffi/include/boreas.h`, never
  against prose.** `api/windows.md` is the guide; the header is the source of
  truth, and the two have already disagreed. Add a declaration together with the
  law in `AbiSignatures` that pins its signature.
- **Compare `BOREAS_ABI_VERSION` against `boreas_abi_version()` at startup,
  before anything else.** A stale library beside a newer header reads every
  field at the wrong offset. There is no later moment at which that is cheap to
  notice, and `fetch-native.sh` asserts the pinned archive still ships ABI 1.
- **The functional core stays free of `Microsoft.UI`.** `Boreas.Ui.Tests`
  targets plain `net10.0` and compiles it from source, so a `DispatcherQueue`
  creeping into `Contracts`, `Presentation` or either channel stops compiling on
  the Linux job. That is the enforcement; the rule alone is not.
- **The control event window is a control-plane record, not a log.** Two hundred
  slots for transitions, commands, channel changes and failures. Never put a
  per-DNS-question event in it; it would evict everything that explains what the
  tunnel is doing.
- Never put credentials, packet payloads, keys, or unrestricted diagnostic logs
  anywhere a user or a support bundle can read them.

## Change Process

1. Read the `api/` page that owns the boundary you are changing.
2. State the invariant and the cheapest check that could falsify it. Prefer a
   type that makes the invalid state unwritable over a check that catches it.
3. Keep Windows API effects behind `ITunnelHost` and `IPacketRing`, and pure
   decisions in front of them. That line is why most of this is testable here.
4. Add the law with the change, and **prove it fails without the change.** The
   layout laws were each verified by mutation; a law that has never been red is
   a claim.
5. Run both suites before pushing:

       source /tmp/boreas-windows-dev/activate.sh
       dotnet test tests/Boreas.Interop.Tests/Boreas.Interop.Tests.csproj -c Release
       dotnet test tests/Boreas.Ui.Tests/Boreas.Ui.Tests.csproj -c Release

   Neither loads `boreas.dll`, so neither needs Windows or a fetched artifact.
   `src/Boreas.Ui` builds only on Windows; CI is what proves it links.
6. Run `git diff --check` after edits.

**What still needs a device.** Nothing in `Wintun`, `Bypass.UnicastInterface`,
`AdapterSetup`, `Authority.AuthorityStore` or `WintunTunnelHost` has been run
against real hardware. It compiles and its pure parts are asserted; the effects
are not. Treat the first device run as the first test of those files.