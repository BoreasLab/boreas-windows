#!/usr/bin/env bash
#
# Audit the workflows for the failure modes actionlint does not model:
# over-broad permissions, unpinned actions, persisted credentials, and
# untrusted context interpolated into a shell.
#
# Assumes bash 4+ and a Python 3 with pip (the ubuntu-latest runner image).
set -euo pipefail

readonly VERSION="${ZIZMOR_VERSION:?ZIZMOR_VERSION must be set}"

# Isolated so the audit tool cannot disturb whatever Python the rest of the
# job might use, and torn down whichever way the script exits.
venv="$(mktemp -d)"
readonly venv
cleanup() { rm -rf "$venv"; }
trap cleanup EXIT

main() {
  python3 -m venv "$venv"
  "$venv/bin/pip" install --quiet --disable-pip-version-check "zizmor==${VERSION}"

  # --persona=regular reports what is actionable rather than every
  # theoretical finding; --min-severity=low still fails the job on anything
  # it does report, because a finding nobody has to act on should not be
  # reported at all.
  "$venv/bin/zizmor" \
    --persona=regular \
    --min-severity=low \
    --format=plain \
    .github/workflows
}

main "$@"
