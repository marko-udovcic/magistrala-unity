#!/bin/sh
# Drugi krug; pokrenuti iz korijena klona. Ne dodaje slike ni generirani sadržaj.
set -eu
[ "$(git branch --show-current)" = ceste ] || { echo 'Potrebna je grana ceste.' >&2; exit 1; }
git diff --cached --quiet || { echo 'Indeks već sadrži promjene; prvo ih pregledati.' >&2; exit 1; }
git add -- Assets/Shaders/Asphalt.shader Assets/Shaders/CesteStone.shader Assets/Editor/CesteMaterials.cs
git diff --cached --check
git commit -m 'Vrati tamniji asfalt i vapnenac te prorijedi zakrpe i brtvljenje' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
git add -- Assets/Editor/WorldBuilder.cs Assets/Editor/StreetsBuilder.cs Assets/Editor/StreetsSidewalks.cs Assets/Editor/CesteKerbs.cs Assets/Editor/CesteKerbs.cs.meta
git diff --cached --check
git commit -m 'Primijeni zaobljene svijetle rubnjake i vidljive bankine na modelske ceste' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
git add -- Assets/Editor/CESTE-README.md Tools/ceste/commit-code.sh
git diff --cached --check
git commit -m 'Opiši drugi krug popravaka cesta i redoslijed obnove' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
