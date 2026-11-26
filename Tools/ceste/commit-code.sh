#!/bin/sh
# Pokrenuti iz korijena klona nakon pregleda. Slike i generirani sadržaj nisu uključeni.
set -eu
[ "$(git branch --show-current)" = ceste ] || { echo 'Potrebna je grana ceste.' >&2; exit 1; }
git diff --cached --quiet || { echo 'Indeks već sadrži promjene; prvo ih pregledati.' >&2; exit 1; }
git add -- Assets/Shaders/Asphalt.shader Assets/Shaders/CesteStone.shader Assets/Shaders/CesteStone.shader.meta Assets/Editor/CesteMaterials.cs Assets/Editor/CesteMaterials.cs.meta Assets/Editor/GraphicsPass.cs Assets/Scripts/RenderQuality.cs
git diff --cached --check
git commit -m 'Dodaj teksturne maske asfalta i zajednički shader kamena' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
git add -- Assets/Editor/WorldBuilder.cs Assets/Editor/StreetsBuilder.cs Assets/Editor/StreetsSidewalks.cs Assets/Editor/CesteCrossings.cs Assets/Editor/CesteCrossings.cs.meta
git diff --cached --check
git commit -m 'Oblikuj rubnjake, rampe na zebrama i kamenu ispunu otoka' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
git add -- Assets/Editor/RivaBuilder.cs Assets/Editor/CESTE-README.md Assets/Editor/CESTE-README.md.meta Tools/ceste/commit-code.sh
git diff --cached --check
git commit -m 'Uredi kamenu rivu, morski rub i spojene bitve te opiši obnovu' -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
