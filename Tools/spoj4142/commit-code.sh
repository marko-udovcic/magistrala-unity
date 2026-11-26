#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
[ "$(git branch --show-current)" = spoj4142 ] || { echo 'Expected branch spoj4142' >&2; exit 1; }
git diff --cached --quiet || { echo 'Index must be empty' >&2; exit 1; }
git add -- Tools/spoj4142/REPORT.md Tools/spoj4142/before-hashes.json Tools/spoj4142/verify-unchanged.py Tools/spoj4142/commit-code.sh
git commit -m 'Dokumentiraj popravak nagiba spoja Štafilić 41–42' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
