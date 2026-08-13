# Boreas Windows

Boreas Windows is the native Windows control surface and service packaging for
the shared Rust engine in `boreas-core`. This repository is deliberately a
documentation handoff, not an application scaffold: implementation starts only
after the decisions and acceptance gates in [docs](docs/README.md) are met.

The design is a WinUI 3 C# control client and a native Rust Windows service
host. The service owns Wintun and `boreas-core`; the UI sends authenticated
control commands over a local pipe and never receives packet bytes.

Start with [the documentation index](docs/README.md), then read
[AGENTS.md](AGENTS.md) before changing the repository.