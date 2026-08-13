# Verified Inputs

Checked on 2026-08-12. These sources justify the platform choices; they do not
replace Boreas-specific device and security testing.

| Input | Evidence | Implementation consequence |
|---|---|---|
| Windows App SDK is recommended for new desktop applications | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/): "The Windows App SDK is the recommended development platform for building new Windows desktop applications." | Use Windows App SDK for the C# control surface. |
| WinUI 3 is recommended and supports C# | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/): "WinUI 3 is the recommended native UI framework... to both C# and C++ developers." | Use C# and WinUI 3 for presentation. |
| .NET can host a long-running Windows Service | [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service): "you can create Worker Services that run as a Windows Service." | A future managed host is possible, but v1 remains a native host to preserve the direct Rust Wintun seam. |
| Wintun exposes a userspace packet adapter | [Wintun](https://www.wintun.net/): "provides userspace programs with a simple network adapter for reading and writing packets." | Use Wintun as the v1 raw-IP device boundary. |
| Wintun can ship as one DLL | [Wintun](https://www.wintun.net/): integration requires "a single `wintun.dll` file." | Pin, hash, package, and review the authorized binary. |
| C# UniFFI generators are third-party | [Mozilla UniFFI](https://github.com/mozilla/uniffi-rs/blob/main/README.md) lists C# under third-party bindings; [Nord Security](https://github.com/NordSecurity/uniffi-bindgen-cs) says its generator is separate from `uniffi-rs`. | Do not state that C# generation is built into UniFFI; use a reviewed C ABI or separately reviewed generator. |

## Explicitly Unverified Until Windows Device Work

- Exact Wintun package, license obligations, installer behavior, and update
  path selected for Boreas.
- Service account and named-pipe ACL behavior under the final installer and
  Windows-version support matrix.
- Physical-interface binding behavior for every supported network type.
- Route cleanup, sleep/resume, network changes, UI/service upgrade mismatch,
  packet throughput, wakeups and memory under real traffic.

Record each result with Windows version/build, hardware, installer build, test
procedure, and observed outcome. Do not convert a passing CI build into a
device-performance or security claim.