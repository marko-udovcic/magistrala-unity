import sys
sys.path.insert(0,'/private/tmp/claude-501/-Users-macair-Documents-BalkanDriver/eff135b3-602f-4849-9129-cf1d21309610/scratchpad/pylib')
from PIL import Image, ImageDraw, ImageFont
F='/Users/macair/Documents/MagistralaUnity-galeb/Assets/Resources/Fonts/'
W,H=2048,384; S=2
im=Image.new('RGBA',(W,H),(0,0,0,0)); d=ImageDraw.Draw(im)
blue=(28,74,150,255); white=(250,250,247,255)
d.rounded_rectangle((0,0,W-1,H-1),radius=46,fill=blue)
d.rectangle((0,H-34,W,H-22),fill=(255,255,255,70))     # thin light pin-stripe
# a simple gull (own drawing): two wing arcs meeting low in the middle, drawn as a chain of discs
import math
cx,cy=200,200
for sgn in (-1,1):
    for t in range(0,241):
        u=t/240; x=cx+sgn*u*130; y=cy-math.sin(u*math.pi*0.95)*70+u*u*25
        w=13*(1-0.55*u)
        d.ellipse((x-w,y-w,x+w,y+w),fill=white)
big=ImageFont.truetype(F+'Inter-SemiBold.ttf',150); small=ImageFont.truetype(F+'Inter-Regular.ttf',58)
d.text((360,46),'GALEB TRANSPORT',font=big,fill=white)
d.text((366,236),'obrt za prijevoz · Kaštel Lukšić',font=small,fill=(225,232,245,255))
im.save(sys.argv[1]); print(im.size)
