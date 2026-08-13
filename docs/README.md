# Windows Documentation

This is the Windows handoff pack for the shared-core, native-shell design. It
records decisions ready for implementation and labels interfaces that still
need a coordinated `boreas-core` change.

| Document | Read when working on |
|---|---|
| [Core Contract](core-contract.md) | shared responsibilities, service/control interface, packet boundary and lifecycle ordering |
| [Platform Integration](platform-integration.md) | WinUI 3, native service host, Wintun, named-pipe security and bypass glue |
| [Implementation Plan](implementation-plan.md) | work order, acceptance gates and Windows test matrix |
| [Verified Inputs](verified-inputs.md) | fact-checked platform inputs, source links, and unresolved decisions |

The related core specifications are [platforms](https://github.com/BoreasLab/boreas-core/blob/main/docs/platforms.md), [architecture](https://github.com/BoreasLab/boreas-core/blob/main/docs/architecture.md), and the [verification ledger](https://github.com/BoreasLab/boreas-core/blob/main/docs/verification.md).