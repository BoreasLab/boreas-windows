# Windows UI Design

This document owns the appearance and behaviour of the WinUI 3 control client
in `src/Boreas.Ui`. Read it with
[platform-integration.md](platform-integration.md), which owns what the client
is allowed to do, and [core-contract.md](core-contract.md), which owns the
state model it renders.

## Sequencing

The implementation plan puts the control client at W3, after the native host
and the control protocol. The interface was built ahead of that order on
purpose, and the consequences are recorded rather than hidden:

- `IControlChannel` is the whole surface between the interface and the
  service. W2 implements it over the authenticated named pipe. Nothing above
  that interface changes when it does.
- Release builds ship `UnimplementedControlChannel`, which reports that this
  build has no pipe client. It does not report a stopped tunnel, because it
  does not know.
- Debug builds ship `SampleControlChannel`, whose every value is invented. The
  window carries a permanent "Sample data" marker whenever it is in use, and
  the class is compiled out of release builds.
- The W0 inputs are not pinned. `Directory.Packages.props` uses floating
  version ranges and says so. No release candidate may build against them.

## The read

A privileged-service control surface for people who need to know whether their
traffic is going through Boreas, and to start and stop it deliberately.
Optimised for unambiguous state and a one-press start. Built on WinUI 3 and
the Windows App SDK, with the supplied design document's structure and the
supplied palette's colours.

Dials: `VARIANCE 4`, `MOTION 3`, `DENSITY 6`.

- Variance is low because this is a control surface someone opens daily and
  Windows conventions carry most of the layout. The one deliberate asymmetry
  is the status band, which is weighted left and dark against a cream page.
- Motion is low because there are exactly two things worth animating: a
  transition that is genuinely under way, and a press acknowledgement. Both
  are gated on the system animation setting.
- Density is moderate. The status screen answers one question and then shows
  its evidence; the network form is a single column; diagnostics is a dense
  list with alignment doing the work instead of rules.

## What the interface may claim

The client renders what the service returned. It never infers state from
button state, from process liveness, or from how long a command has been
outstanding.

Two rules follow, and they are the reason the code is shaped the way it is.

**The channel is not the tunnel.** `ControlChannelState` and `ServiceState`
are separate closed sets, shown separately. While the channel is not
connected, the status screen makes no claim about the tunnel at all. Reporting
"Off" because the pipe stopped answering would be a false statement about
someone's network.

**"Protected" requires a bound bypass.** A running session whose
`TunnelBypass` could not bind the physical interface may be sending its own
upstream traffic back into the tunnel. That state reads "Running" with a
separate warning, never "Protected".

## Palette

Supplied ramps mapped to roles once, in `Design/Tokens.xaml`. Every
measurement below is of a composited pair, computed rather than assumed.

| Role | Light | Dark | Source |
|---|---|---|---|
| Canvas | `#f8f9ec` | `#090d1b` | beige-50, space-indigo-950 |
| Card | `#f2f3d8` | `#0d1226` | beige-100, space-indigo-900 |
| Band | `#090d1b` | `#0d1226` | space-indigo-950, 900 |
| Primary text | `#090d1b` | `#f8f9ec` | space-indigo-950, beige-50 |
| Secondary text | `#263773` | `#b3bee6` | space-indigo-700, 200 |
| Accent fill | `#9f512d` | `#d28460` | light-bronze-600, 400 |
| Control border | `#667ccc` | `#667ccc` | space-indigo-400 |
| Danger | `#8f3d3f` | `#d19495` | dusty-rose-600, 300 |

Status tones are named for the tone, not the state, because two states share
one. They are never the accent: "the tunnel is up" must not look like "press
this".

| Tone | Light | Dark | Used by |
|---|---|---|---|
| Idle | `#263773` | `#8c9dd9` | Stopped, Connecting |
| Active | `#0d0099` | `#7366ff` | Running with a bound bypass |
| Caution | `#4c4e18` | `#d8da8b` | Starting, Stopping, degraded bypass, no service |
| Fault | `#6b2e2f` | `#d19495` | Failed, Unauthorized, version mismatch |

One conflict with the accessibility floor was found and resolved by taking the
darker ramp step: light-bronze-500 as a filled button with a cream label
measures 3.69:1. The light accent fill is light-bronze-600 at 5.35:1;
light-bronze-500 remains available for large non-text fills.

Under forced colours every role defers to the system pair. Status colour
carries no information anywhere in the interface, so collapsing the tones
under high contrast loses nothing: the state is always also a word and a
glyph.

## Type

The supplied document's steps, with the two display steps in a serif and
everything else in the platform sans.

| Step | Size | Face | Used by |
|---|---|---|---|
| Display md | 36 | Sitka | the state word on the band |
| Display sm | 28 | Sitka | page titles |
| Title lg / md / sm | 22 / 18 / 16 | Segoe UI Variable | counters, sections, field labels |
| Body md / sm | 16 / 14 | Segoe UI Variable | running text |
| Caption | 13 / 12 | Segoe UI Variable | labels above values |
| Code | 14 | Cascadia Mono | identifiers, addresses, counters |

Sitka is the honest substitute for the document's licensed Copernicus: it
ships with Windows, it is an optical-size serif designed for screen reading,
and nothing has to be downloaded. Cascadia Mono ships with Windows 11 and
falls back to Consolas. Negative tracking on the display steps is applied as
the document requires.

The document's 64px and 48px steps and its 96px section rhythm are a marketing
rhythm and are unused here.

## Structure

```text
title bar    wordmark, sample marker when present, control-channel chip
banner       only when the channel needs something done about it
pane         Status, Network, Diagnostics; Settings and About in the footer
page         one view, page title matching its pane entry exactly
```

The title bar is the drag region and holds nothing interactive. The channel
chip lives there because it is true of the whole application and because a
strip that is always present never makes the layout below it move.

Mica is deliberately not used. It tints from the desktop behind the window,
which would make the canvas indeterminate, and the canvas is what every
contrast pairing above is measured against.

## Screens

**Status.** A dark band carrying the state word, one sentence, the single
action that applies, and the session underneath a divider. Session identity
and counters are inside the band so the answer and its evidence read as one
object. A degraded bypass appears as a separate banner below, because it is a
separate claim.

**Network.** One column, labels above fields, sections with headings. Input is
parsed rather than rejected: an MTU typed as "1,420", an address pasted with
whitespace, DNS servers separated however someone happens to separate them.
Validation runs on blur, then on every keystroke once a field has errored.
Nothing is disabled while the tunnel runs; a change that needs a restart is
accepted and then says so. A rejection from the service keeps every typed
value and places each message beside the field that caused it.

**Diagnostics.** The bounded control-plane event record: transitions,
commands, channel changes, failures. Not a log viewer, because the pipe
carries no log stream. Six container states are six regions with one visible;
"nothing recorded yet" and "nothing matches this filter" are different screens
with different actions.

**Settings.** Theme only, and it says the tunnel is unaffected.

**About.** Versions read at runtime, and a plain statement of what the window
can and cannot do, including that closing it does not stop the tunnel.

## Interaction

- Every interactive element ships rest, pointer, focus, active, disabled,
  loading and error. One button template carries all of them, so a new variant
  supplies colours and cannot invent its own behaviour.
- Pending state is laid out whether or not it is spinning, so a press never
  resizes a control or replaces its label.
- Commands guard themselves while in flight; a double press is not two start
  commands for the service to serialise.
- Stop is not confirmed and is not painted as danger. It is undone by pressing
  start, and it is the expected action while the tunnel is up. Spending a
  confirmation on the most frequent press is how people learn to dismiss the
  one confirmation that matters.
- Results appear where the action was taken. The only transient confirmation
  is the copy button's own label.
- State changes are announced through polite live regions.
- Motion is checked at the point of use, so turning system animation off takes
  effect on the next state change rather than the next launch. Nothing is
  gated behind an animation.

## What CI checks

`.github/workflows/ci.yml` runs three jobs. Two of them bear on this document.

`core` runs on Linux and builds `tests/Boreas.Ui.Tests`, which compiles the
functional core from source into a plain `net10.0` project. That target is the
enforcement: `Contracts`, `Presentation` and the channel abstraction have to
stay free of `Microsoft.UI`, because a reference to it stops compiling there.
The suite then checks, against the source rather than against this document:

- every state pair of the channel and the service produces readable text, an
  announcement carrying everything the tone carries, and a legal action;
- a disconnected channel makes no claim about the tunnel, and "Protected"
  appears exactly when the channel is connected, the session is running, and
  the bypass is bound;
- the form accepts every unambiguous spelling of a value, stays quiet until a
  field is finished, clears an error the moment it stops being true, and
  preserves entry through a rejection;
- parsing a configuration is idempotent through its canonical rendering, and
  each refined value rejects text that fails its own rule;
- the six container states are distinguishable and carry items only where they
  should;
- every contrast pairing in `Design/Tokens.xaml`, recomputed from the hex,
  against the threshold for text or for a non-text mark;
- danger and every status tone are perceptually distant from the accent;
- the three theme dictionaries define the same roles, high contrast defines no
  colour of its own, every resource reference resolves, no colour is named
  outside the token file, and every spacing and radius value is on its scale.

`app` runs on Windows and is the only place WinUI 3 builds. It produces an
unsigned artifact for inspection, not a release candidate.

## Types that carry their own proof

The configuration boundary is typed rather than checked. `AdapterName`,
`TunnelAddress`, `PacketSize` and `DnsServers` each have a private constructor
and one `TryParse`, so holding one is evidence that the text passed. They
compose into `ValidatedConfiguration`, which is what `IControlChannel` accepts:
there is no overload that takes the raw strings, so no caller can send
something unchecked and no implementation has to re-check.

Each refined type also owns the sentence shown when its parse fails, so the
rule and its explanation cannot drift apart. The view model no longer has
validators of its own; it asks the parser.

The same discipline runs through the rest of the model. Service state, channel
state, collection state, configuration outcome and parse result are closed
sums eliminated by a `Match` that takes one delegate per case, so adding a case
breaks every site that has to handle it. `ConfigurationOutcome.Rejected` is
keyed by `ConfigField` rather than by a wire string, which puts the translation
from wire names at the channel edge instead of leaving every consumer to cope
with a field name that names no field.

## Still open

- Real brand artwork. `Controls/Wordmark.xaml` draws a provisional mark and
  says so; only that file depends on its shape.
- An application icon. No `.ico` is present.
- Localisation. Every string is inline English.
- Whether the client should surface CA trust and installation guidance, which
  W5 lists and which has no screen here yet.
- Whether the status band should show a live throughput reading. It does not
  today, because a counter that ticks without a pushed update would be
  inventing motion the service did not report.
- Nothing verifies the interface as rendered. Keyboard order, focus visuals,
  screen-reader output and both themes on a display are checked by reading, not
  by CI, and belong in the device gate.
