#!/bin/sh
# Samo treći krug (bankina); pokrenuti iz korijena klona.
set -eu
[ "$(git branch --show-current)" = ceste ] || { echo 'Potrebna je grana ceste.' >&2; exit 1; }
git diff --cached --quiet || { echo 'Indeks već sadrži promjene; prvo ih pregledati.' >&2; exit 1; }
git add -- Assets/Shaders/CesteShoulder.shader Assets/Shaders/CesteShoulder.shader.meta Assets/Editor/CesteMaterials.cs Assets/Editor/StreetsBuilder.cs Assets/Editor/StreetsSidewalks.cs
git diff --cached --check
git commit -m 'Suzi i zatamni bankine te zaštiti razdjelnike i postojeću vegetaciju' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
git add -- Assets/Editor/CESTE-README.md Tools/ceste/commit-code.sh
git diff --cached --check
git commit -m 'Opiši obnovu bankina i povrat vegetacije nakon drugog kruga' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
