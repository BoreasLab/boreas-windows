# Boreas Windows

Boreas Windows is the native Windows control surface and service packaging for
the shared Rust engine in `boreas-core`. Most of this repository is a
documentation handoff: the native service host, the control protocol and the
packaging decisions in [docs](docs/README.md) still gate their own
implementation.

The exception is the WinUI 3 control client in `src/Boreas.Ui`, built ahead of
its place in the work order. It talks to one interface, `IControlChannel`,
which the control protocol phase implements over the authenticated local pipe.
Until then, release builds report that they have no pipe client rather than
guessing at tunnel state, and debug builds run on clearly marked sample data.
Build inputs are not pinned yet. [UI Design](docs/ui-design.md) records what
was decided and what is still open.

The design is a WinUI 3 C# control client and a native Rust Windows service
host. The service owns Wintun and `boreas-core`; the UI sends authenticated
control commands over a local pipe and never receives packet bytes.

Start with [the documentation index](docs/README.md), then read
[AGENTS.md](AGENTS.md) before changing the repository.