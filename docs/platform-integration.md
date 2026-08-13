# Windows Platform Integration

## Component Shape

```text
WinUI 3 C# app -- authenticated named pipe --> Boreas native service
                                                   |
                                                   +-- Wintun adapter/session
                                                   |
                                                   +-- boreas-core runtime
```

The C# app is a control surface. The native service is the privileged long-lived
host because the existing core adapter accepts a Rust
`wintun_bindings::Session` through `WintunDevice::from_session`. This preserves
one direct packet path and avoids inventing an opaque-session FFI merely to put
Wintun in managed code.

## Service State

The host and UI share a closed status model equivalent to:

```text
Stopped
Starting
Running(session, status)
Stopping(session)
Failed(operation, recoverable)
```

Only the service drives transitions. The UI sends intent and renders returned
state; it does not infer status from button state or process liveness.
Concurrent Start, Stop, and configuration commands are serialized by the
service owner and return correlated responses instead of creating competing
native runtimes.

## Wintun and Route Ownership

The service:

1. validates trusted `PlatformConfig` and installation privileges;
2. creates or opens the named Wintun adapter and session;
3. installs interface addresses, routes, DNS configuration and MTU according
   to the approved platform configuration;
4. creates `WintunDevice::from_session(Arc<Session>, Mtu)` for the core;
5. starts the core and only then publishes `Running`;
6. stops the core before closing the session or removing adapter resources.

Wintun receives and sends raw IP directly through its packet rings. The host
must preserve the existing adapter's cancellation-safe receive law: cancelling
the outer async wait cannot discard a packet consumed by a blocking read.

## Egress Bypass

An upstream socket that follows the Wintun default route loops into Boreas. The
Windows `TunnelBypass` implementation must select or bind the physical network
interface before connection and return a typed error if it cannot do so. It may
not use the generic desktop `DirectSockets` implementation until that invariant
is demonstrably true for the selected route.

This is an OS integration responsibility, not egress policy. The core decides
which egress is needed; the Windows host makes the resulting socket capable of
reaching it without re-entering the TUN.

## Named-Pipe Security

Use a local named pipe with a fixed product-owned name, explicit restrictive
ACL, frame-size cap, protocol-version check, request correlation, timeout, and
per-client backpressure. The service authenticates the Windows principal before
processing a command and returns no sensitive configuration or key material to
an unauthorized client.

The pipe is for lifecycle and status only. It does not carry packet payloads,
native pointers, Wintun handles, arbitrary file paths, or arbitrary log streams.
The UI reconnects after service restart using an idempotent status query.

## UI Responsibilities

WinUI 3 and the Windows App SDK provide settings, status, error presentation,
installation guidance, and explicit Start/Stop/Retry commands. They do not own
service elevation, Wintun handles, egress sockets, or packet buffers.

If a later product requirement needs a managed Windows Service, it may remain a
control host only unless the core defines a tested opaque Wintun boundary. It
must not create a second datapath or move the hot packet path into C# by
default.

## Device Test Matrix

The first Windows device gate covers, at minimum:

| Scenario | Required observation |
|---|---|
| Install and first start | service gets only required privilege and creates one adapter/session |
| UI crash/restart | service session remains correct; reconnect receives authoritative status |
| Service stop during traffic | core stops before Wintun closes and no packet is silently discarded by cancellation |
| Unauthorized pipe client | command is rejected before payload handling |
| Oversized or unknown protocol frame | connection is rejected without allocation growth or service failure |
| Physical-network change | egress bypass remains outside the TUN or reports typed degradation |
| Core packet fixture | byte-for-byte result matches the same fixture in `boreas-core` |