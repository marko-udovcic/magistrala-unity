#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
[ "$(git branch --show-current)" = parking ] || { echo 'Expected branch parking' >&2; exit 1; }
git diff --cached --quiet || { echo 'Index must be empty' >&2; exit 1; }
# Cars commit includes debug/stats and the player instancing-variant retention fix/guard.
git add -- Assets/Scripts/Traffic/ParkedCars.cs Assets/Scripts/Traffic/ParkedCars.Far.cs Assets/Scripts/Traffic/ParkedCars.Far.cs.meta Assets/Resources/ParkedFar.shader Assets/Resources/ParkedFar.shader.meta Assets/Resources/ParkedFar.mat Assets/Resources/ParkedFar.mat.meta Assets/Editor/ParkedFarShaderVariants.cs Assets/Editor/ParkedFarShaderVariants.cs.meta
git commit -m 'Dodaj jeftini udaljeni prikaz parkiranih auta' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
git diff --cached --quiet || { echo 'Index must be empty before pole commit' >&2; exit 1; }
git add -- Assets/Editor/SignsBuilder.Asphalt.cs Assets/Editor/SignsBuilder.Extra.cs Assets/Editor/SignsBuilder.cs Tools/parking/REPORT.txt Tools/parking/commit-code.sh
git commit -m 'Makni zaostale stupove semafora s kolnika' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
