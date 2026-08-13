# Shared Core Contract

## Status

This is the implementation contract between this repository and `boreas-core`.
It is not an exported ABI and does not authorize placeholder FFI symbols. The
first host interface must implement this contract in lockstep with a versioned
core interface and tests on both sides.

## Boundary

```text
WinUI 3 C# client -> authenticated local control pipe -> native service host
                                                        |
                                                        v
                                                Wintun -> boreas-core
```

The device boundary is an ordered sequence of raw IPv4 or IPv6 packets. The
service creates and owns Windows resources; the core owns packet and network
semantics. The control pipe is a separate, low-rate control plane.

| Concern | Windows host owns | Rust core owns |
|---|---|---|
| User interaction | WinUI 3 views, settings, command intent | no UI state |
| Service and device | SCM lifecycle, Wintun adapter/session, interface routes and MTU | reads and writes the admitted session |
| Packet processing | direct session transfer only | parsing, reassembly, MTU, ECN, ICMP, TCP, UDP, policy and egress |
| Upstream bypass | select and bind the physical-network route/interface | request bypass and fail when it cannot be established |
| Observability | authorize clients and present typed status | produce typed status, counters, errors and logs at effect boundaries |

The UI, pipe, and service may not introduce a Windows-specific parser, filter,
route decision, or packet queue. Packet work remains in the core's $O(p)$
per-packet path for a packet of $p$ bytes; Wintun data must not be copied into a
managed buffer or serialized onto the control pipe.

## Logical Interface v1

These are semantic operations. Names, serialization, and exported function
signatures are deliberately deferred to the core host crate.

| Operation | Input | Success result | Failure rule |
|---|---|---|---|
| `start` | validated engine configuration and platform configuration | running session identity and initial status | leaves no live session, route or child work on failure |
| `stop` | session identity and typed reason | terminal stopped status | idempotent after the first accepted stop |
| `configuration_changed` | validated replacement or explicit restart requirement | applied status or restart-required status | no partial silent application |
| `network_changed` | typed Windows network event | replan or typed degradation status | preserves cancellation and existing flow invariants |
| `status_snapshot` | none | immutable status and bounded counters | never returns packet payloads |
| `control_request` | authenticated, size-bounded versioned envelope | correlated response | rejects unknown version, command and principal |

`EngineConfig` owns policy and egress choices. `PlatformConfig` owns adapter
name, address, routes, MTU, service account, packaging and local-control
settings. Both are parsed once at the trust boundary and become immutable
trusted values before the service starts.

## Control Plane

The future pipe protocol uses a maximum frame size, protocol version, request
identifier, command discriminator, typed payload, and typed response. Its first
commands are `status_snapshot`, `start`, `stop`, `configuration_changed`, and
`subscribe_status`.

Authorization happens before decoding a command payload beyond the fixed-size
envelope. The service grants only the local user or administrator identities
defined by installation policy. It must reject remote pipe access, oversized
frames, unknown versions, replayed session identifiers, and commands from an
unapproved principal. There is no packet command.

## Resource and Cancellation Law

Startup is linear:

```text
validate -> create service resources -> create Wintun session -> start core -> publish running
```

Shutdown is the reverse:

```text
stop new control work -> cancel core -> await native completion -> close session -> release adapter resources
```

At most one transition owns a session. The service must join or otherwise prove
the native core stopped before closing Wintun, so no task can retain a packet
ring after the session becomes invalid. A failed start unwinds each acquired
resource before returning its typed error.

## Binding Choice

The service links the Rust core directly, so v1 requires no UI-to-core FFI.
WinUI communicates with the service over the authenticated local pipe. If a
future managed component needs direct native calls, use a reviewed C ABI or the
separately maintained `uniffi-bindgen-cs`; Mozilla UniFFI itself provides Kotlin
support and lists C# as third-party. That decision requires a dependency,
license, maintenance and transitive-graph review before admission.