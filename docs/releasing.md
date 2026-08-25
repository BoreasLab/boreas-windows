# Releasing

**Current.** Unlike the rest of `docs/`, this describes what the repository
does today.

## Two tag shapes, and only two

| | Tag | Cut by |
| --- | --- | --- |
| **Release** | `v0.4.2` | a human pushing the tag |
| **Pre-release** | `v0.4.3-dev.2026-08-25.11-30-00.g1a2b3c4` | every push to `main` |

Both are valid SemVer 2.0.0, and §11 sorts a pre-release *below* the release
sharing its core version. So `v0.4.2 < v0.4.3-dev.… < v0.4.3`, and anything that
sorts tags gets "newest" right without knowing the scheme. A pre-release is
therefore numbered for **the patch that has not happened yet**.

`v0.0.0` is on the first commit. It is not a release anybody shipped: it anchors
the build counter and makes the base version total, so the empty-repository case
does not exist.

## The tag is the version

Cutting a release is one act:

```sh
git tag -s v0.4.2 -m "..." && git push origin v0.4.2
```

There is no version to bump first. `Directory.Build.props` carries
`<Version>0.0.0</Version>` as a **fallback** for a build with no tag, and
nothing in the pipeline reads it: an earlier design declared a version there
*and* accepted a tag, then had a check policing the disagreement. A check exists
only where two sources can differ. Deleting the second source made the invariant
hold by construction, and stopped a release being two acts of which the one you
forget is the one that fails the build.

The only refusal left is at the parser: a ref that is not `vMAJOR.MINOR.PATCH`
never becomes a release. SemVer forbids leading zeros, so `v0.04.2` is refused
rather than being a second spelling of `v0.4.2`.

If the next release should be a minor, **tag a minor**.

## What the numbers mean

`dotnet run --project build -- resolve` produces all of them, once, and every
job downstream reads its output rather than deriving anything again.

| Field | Value | Why |
| --- | --- | --- |
| `AssemblyInformationalVersion` | the full SemVer, verbatim | The only field that can carry a pre-release identifier, and the only one the tag can be recovered from exactly. This is what the about window and a crash report show. |
| `FileVersion` | `major.minor.patch.n` | `n` counts commits since the last release tag; a release takes `65535`, the field's maximum, so it sorts above every pre-release sharing its triple. |
| `AssemblyVersion` | `major.minor.0.0` | A binding identity, not a build stamp. Moving it per build gives every reference a distinct target. |

`FileVersion` is order-preserving from the tag order, and that is a property test
over a simulated history rather than a claim.

## Distribution format, and what it costs

**Today: an unpackaged `.exe`.** `Boreas.Ui.csproj` sets
`WindowsPackageType=None` with `OutputType=WinExe`; there is no `.appxmanifest`
and no installer project. Nothing compares these numbers to decide an upgrade,
so the fields above are what a user sees in the file properties dialog and what
a crash dump carries.

**Both installer formats would refuse the encoding, and both were checked
against Microsoft's own documentation.**

- **MSI.** *"The first field is the major version and has a maximum value of
  255. The second field is the minor version and has a maximum value of 255…
  Note that Windows Installer uses only the first three fields of the product
  version. If you include a fourth field in your product version, the installer
  ignores the fourth field."* An MSI would discard `n` outright, so every
  pre-release of one patch would compare equal and the upgrade from one nightly
  to the next would simply not happen.
- **MSIX.** *"the last (fourth) section of the version number is reserved for
  Store use and must be left as 0… The other sections must be set to an integer
  between 0 and 65535 (except for the first section, which cannot be 0)."* That
  forbids both the field this scheme uses and this product's major version.

Choosing either is a change to `build/Rendering.cs` and nothing else — most
likely moving the counter into the third field. The algebra is deliberately
separate from the rendering so that costs one function and no retesting of the
laws.

## Code signing

**A separate axis from the release scheme.** Nothing about which tag was cut
says anything about who signed the result, and the two are not conflated
anywhere in the pipeline.

**Today there is no signing identity and every artefact is unsigned.** For a
tester that means:

- SmartScreen shows an unrecognised-publisher warning on first run, and there is
  no publisher name to check against.
- Windows reports no Authenticode signature on `Boreas.exe`.
- **Provenance is the substitute, and it is stronger than a certificate about
  where the file came from.** Every archive carries a SLSA build attestation
  signed at build time:

  ```sh
  gh attestation verify boreas-0.4.2-win-x64.zip --repo BoreasLab/boreas-windows
  ```

  That answers "was this built by that workflow, from that commit", which a
  code-signing certificate does not. It does **not** answer "will Windows trust
  it", which is what a certificate is for.

The two native libraries are in a different position and neither is affected by
this: `wintun.dll` is Authenticode-signed by WireGuard LLC and redistributed
exactly as published, and `boreas.dll` carries its own build provenance, checked
on every fetch.

When an identity exists it will be an Azure Trusted Signing account or a
certificate in a hardware token, the private key will never be in this
repository or in a workflow variable, and signing will happen in `publish`
between building and archiving. Until then, do not describe a build here as
signed.

## Branch protection

`n` is a commit count, and **a force-push to `main` changes it and can repeat
one** — two different artefacts would then wear one file version. The fix is not
in this code:

- forbid force-push on `main`;
- require signed commits.

`tag.gpgsign` is already on locally, so tags are annotated and signed.

## Verifying a release by hand

```sh
gh release download v0.4.2 --repo BoreasLab/boreas-windows
sha256sum --check --ignore-missing SHA256SUMS
gh attestation verify boreas-0.4.2-win-x64.zip --repo BoreasLab/boreas-windows
```

`gh release download` with no tag returns the newest **release** and never a
pre-release, because pre-releases are marked as such.
