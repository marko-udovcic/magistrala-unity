#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
if [[ "$(git branch --show-current)" != sporedne ]]; then
    echo 'Expected branch sporedne.' >&2
    exit 1
fi
if ! git diff --cached --quiet; then
    echo 'Index is not empty; commit or unstage its existing changes first.' >&2
    exit 1
fi
paths=(
    Assets/Scripts/Traffic/TrafficSystem.cs
    Assets/Scripts/Traffic/TrafficSystem.SideStreets.cs
    Tools/sporedne/check-round2.py
    Tools/sporedne/commit-code.sh
)
git diff --check -- "${paths[@]}"
git add -- "${paths[@]}"
git commit -m 'Promet: odblokiraj sporedne ulaze i skretanja, zaštiti stare semafore' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
