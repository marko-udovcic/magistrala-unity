#!/bin/bash
# Izmišljene splitske registarske tablice za promet (Magistrala/TripoCar shader, _PlateTex): atlas 1024 x 1024,
# 2 stupca x 8 redova, ćelija 512 x 128, tablica 500 x 108 px (omjer 520 x 112 mm) u sredini ćelije.
#   stupac 0 (u 0 .. 0.5)  = nova tablica s plavom EU trakom (krug zvjezdica + "HR")
#   stupac 1 (u 0.5 .. 1)  = starija tablica bez EU trake (stariji auti: Zastava, Yugo, Golf 2, W124)
#   red r (v odozdo)       = tablica r (isti broj u oba stupca); Unity v = 1 - (r + 1) / 8 .. 1 - r / 8
# Format: ST · logo igre (Magistrala disk prebojan u crveno-bijelu šahovnicu s plavim, NE državni grb) · 3–4 znamenke - 1–2 slova.
# Brojevi su izmišljeni. Font: D-DIN Condensed Bold (Datto, SIL OFL 1.1, Tools/vehicles/fonts) — besplatni DIN 1451 klon.
# Uporaba: bash Tools/vehicles/tablice.sh [put/do/magistrala-logo.png]  (zadano: ../BalkanDriver/marketing/logo/magistrala-logo-c-plavo.png)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOGO="${1:-$ROOT/../BalkanDriver/marketing/logo/magistrala-logo-c-plavo.png}"
FONT="$ROOT/Tools/vehicles/fonts/D-DINCondensed-Bold.otf"
OUT="$ROOT/Assets/Textures/Vehicles/Plates/tablice.png"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
NUMS=("ST 1974-KA" "ST 512-BG" "ST 2377-PL" "ST 808-MD" "ST 4125-ZT" "ST 371-C" "ST 6290-JR" "ST 1101-S")

# logo: narančasto (kopno, prsten, natpis) -> crveno-bijela šahovnica, plavo more -> tamnoplavo, krem rub -> bijelo
ffmpeg -loglevel error -y -i "$LOGO" -vf "scale=400:400,format=rgba,geq=\
r='if(gt(r(X,Y),140)*gt(r(X,Y),b(X,Y)+60), if(mod(floor(X/34)+floor(Y/34),2), 255, 205), if(gt(b(X,Y),r(X,Y)+25), 24, r(X,Y)))':\
g='if(gt(r(X,Y),140)*gt(r(X,Y),b(X,Y)+60), if(mod(floor(X/34)+floor(Y/34),2), 255, 22), if(gt(b(X,Y),r(X,Y)+25), 60, g(X,Y)))':\
b='if(gt(r(X,Y),140)*gt(r(X,Y),b(X,Y)+60), if(mod(floor(X/34)+floor(Y/34),2), 255, 32), if(gt(b(X,Y),r(X,Y)+25), 150, b(X,Y)))':\
a='alpha(X,Y)'" "$TMP/logo.png"

# one plate at 2x (1000 x 216), $1 = text, $2 = eu (1/0), $3 = output
plate() {
  local txt="$1" eu="$2" out="$3"
  local city="${txt%% *}" rest="${txt#* }"
  local x0=26; [ "$eu" = 1 ] && x0=104
  local f="drawbox=x=0:y=0:w=1000:h=216:color=0x1a1a1a:t=fill,drawbox=x=4:y=4:w=992:h=208:color=0xf7f7f4:t=fill"
  f="$f,drawbox=x=4:y=12:w=992:h=5:color=0xd21f26:t=fill,drawbox=x=4:y=19:w=992:h=5:color=0x1f4fa8:t=fill"
  f="$f,drawbox=x=4:y=192:w=992:h=5:color=0x1f4fa8:t=fill,drawbox=x=4:y=199:w=992:h=5:color=0xd21f26:t=fill"
  if [ "$eu" = 1 ]; then
    f="$f,drawbox=x=4:y=4:w=86:h=208:color=0x1f45a8:t=fill"
    for k in 0 1 2 3 4 5 6 7 8 9 10 11; do
      local sx sy; sx=$(awk "BEGIN{printf \"%d\", 47 + 26*cos($k*3.14159/6) - 4}"); sy=$(awk "BEGIN{printf \"%d\", 72 + 26*sin($k*3.14159/6) - 4}")
      f="$f,drawbox=x=$sx:y=$sy:w=8:h=8:color=0xffd400:t=fill"
    done
    f="$f,drawtext=fontfile=$FONT:text=HR:fontsize=56:fontcolor=white:x=47-text_w/2:y=128"
  fi
  # city code, logo gap, number
  f="$f,drawtext=fontfile=$FONT:text=$city:fontsize=178:fontcolor=0x111111:x=$x0:y=(h-text_h)/2+2"
  ffmpeg -loglevel error -y -f lavfi -i "color=c=white:s=1000x216" -i "$TMP/logo.png" -filter_complex \
    "[0]$f[p];[1]scale=118:118[l];[p][l]overlay=x=$((x0+150)):y=49,drawtext=fontfile=$FONT:text='$rest':fontsize=178:fontcolor=0x111111:x=$((x0+290)):y=(h-text_h)/2+2" \
    -frames:v 1 "$out"
}

# ageing (22. 11. 2026.: tablice su bile prečiste): požutjela bijela podloga, sitno zrno, mrlje prašine (različite po tablici, $3 = sjeme)
# i tamniji donji / gornji rub (prskanje s ceste)
# zajedničko polje mrlja (zamućeni šum), svaka tablica uzima drugi isječak
ffmpeg -loglevel error -y -f lavfi -i "color=c=black:s=1500x700" -vf "format=gray,geq=lum='255*random(1)',gblur=sigma=9,eq=contrast=6:brightness=-0.05,gblur=sigma=3" -frames:v 1 "$TMP/dirt.png"
age() {
  local in="$1" out="$2" k="$3"
  local ox=$(( (k * 173) % 500 )) oy=$(( (k * 97) % 484 ))
  ffmpeg -loglevel error -y -i "$in" -i "$TMP/dirt.png" -f lavfi -i "color=c=0x55493a:s=1000x216" -filter_complex \
    "[0]format=rgb24,colorchannelmixer=rr=.965:gg=.955:bb=.90,noise=alls=7:allf=u[p];\
[1]crop=1000:216:$ox:$oy,format=gray,geq=lum='clip(lum(X,Y)*0.35+if(gt(Y,184),(Y-184)*2.4,0)+if(lt(Y,12),(12-Y)*2.5,0)+if(lt(X,10),(10-X)*3,0)+if(gt(X,990),(X-990)*3,0),0,140)'[m];\
[2]format=rgba[c];[c][m]alphamerge[d];[p][d]overlay=format=auto" -frames:v 1 "$out"
}

for i in "${!NUMS[@]}"; do
  plate "${NUMS[$i]}" 1 "$TMP/eu$i.png"; age "$TMP/eu$i.png" "$TMP/eu$i.a.png" "$i"; mv "$TMP/eu$i.a.png" "$TMP/eu$i.png"
  plate "${NUMS[$i]}" 0 "$TMP/old$i.png"; age "$TMP/old$i.png" "$TMP/old$i.a.png" "$((i + 11))"; mv "$TMP/old$i.a.png" "$TMP/old$i.png"
  ffmpeg -loglevel error -y -i "$TMP/eu$i.png" -i "$TMP/old$i.png" -filter_complex \
    "[0]scale=500:108:flags=area,pad=512:128:6:10:color=0xf7f7f4[a];[1]scale=500:108:flags=area,pad=512:128:6:10:color=0xf7f7f4[b];[a][b]hstack" \
    -frames:v 1 "$TMP/row$i.png"
done
ffmpeg -loglevel error -y $(for i in 0 1 2 3 4 5 6 7; do echo -n "-i $TMP/row$i.png "; done) -filter_complex "vstack=inputs=8" -frames:v 1 "$OUT"
echo "tablice: $OUT"
