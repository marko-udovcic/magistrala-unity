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
    Assets/Editor/TrafficBuilder.cs
    Assets/Editor/TrafficSideBuilder.cs
    Assets/Editor/TrafficSideBuilder.cs.meta
    Assets/Editor/sporedne-routes.json
    Assets/Editor/sporedne-routes.json.meta
    Assets/Scripts/Traffic/TrafficData.cs
    Assets/Scripts/Traffic/TrafficSystem.cs
    Assets/Scripts/Traffic/TrafficSideRoute.cs
    Assets/Scripts/Traffic/TrafficSideRoute.cs.meta
    Assets/Scripts/Traffic/TrafficSystem.SideStreets.cs
    Assets/Scripts/Traffic/TrafficSystem.SideStreets.cs.meta
    Tools/sporedne/extract.py
    Tools/sporedne/check.py
    Tools/sporedne/commit-code.sh
    docs/sporedne-report.txt
)
git diff --check -- "${paths[@]}"
git add -- "${paths[@]}"
git commit -m 'Promet: ulazi, izlazi i propuštanje na 20 sporednih ulica' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
