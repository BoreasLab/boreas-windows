# Windows Implementation Plan

This plan starts after the documentation handoff. Each phase has a narrow
outcome and gate; no later phase may assume the service, packet, or security
boundary already works on a real Windows machine.

## W0: Pin Build, Packaging and Privilege Inputs

Choose and record the supported Windows versions and architectures, .NET SDK,
Windows App SDK and WinUI 3 versions, Rust target, installer, service account,
elevation model, Wintun package source/version/hash, update path, and control
pipe authorization policy. Review every new dependency for license,
maintenance, release, and transitive graph before admission.

**Gate:** reproducible signed development package on a supported Windows test
machine, with Wintun redistribution obligations recorded.

## W1: Native Service Host

Create the Rust host executable that owns SCM lifecycle integration, Wintun
adapter/session setup, `boreas-core`, typed state, and structured shutdown.
There is no WinUI dependency and no packet path through managed code.

**Gate:** unit tests cover every host state and failed-start unwind; Windows CI
builds the target and a test machine proves service install/start/stop.

## W2: Control Protocol

Implement the versioned, framed, authenticated local pipe with explicit
maximum size, timeouts, authorization, request IDs, typed commands, and bounded
status subscriptions. Start with status and lifecycle commands only.

**Gate:** integration tests reject unauthorized, malformed, oversized, stale,
and unsupported-version requests without leaking resources or blocking service
shutdown.

## W3: WinUI 3 Control Client

Create the C# WinUI 3 app as a typed pipe client. It renders service status and
sends explicit commands; it neither links Wintun nor receives native packet
data. Reconnection is driven by idempotent status requests.

**Gate:** UI process termination and restart leave the service session correct,
and the client shows authoritative service state after reconnect.

## W4: Wintun and Bypass Glue

Create the Wintun adapter/session, configure interface state from trusted
`PlatformConfig`, and pass the session directly to `WintunDevice`. Implement
the physical-interface `TunnelBypass` path and test it against a real default
route.

**Gate:** real-device loopback ping and DNS fixture match the core simulator;
upstream traffic is demonstrated to remain outside the tunnel.

## W5: Lifecycle and Release Hardening

Cover service recovery, installer upgrade/uninstall, route cleanup, network
changes, UI/service version mismatch, logs, metrics, CA UX, and permission
failure. Bound every queue and command subscription; measure rather than infer
the operational cost of the service.

**Gate:** start/stop, UI restart, service restart, and route-change soak shows
no orphaned adapter, route, packet ring, child task, or unbounded memory path.

## W6: Release Evidence

Run the agreed Windows-version and network matrix. Measure throughput, memory,
wakeups, reconnect time, pipe authorization behavior, and shutdown latency
under real traffic. Put remaining platform uncertainty in the core verification
ledger rather than presenting it as settled behavior.

**Gate:** evidence is attached to the release candidate and all
platform-specific failures have a typed, user-visible outcome.

## Definition of Done

The Windows shell is complete only when one native service owns one Wintun
session, the C# app is a secured control client, no second packet path exists,
upstream sockets bypass the tunnel, core fixtures match byte-for-byte, and the
device matrix has recorded evidence.