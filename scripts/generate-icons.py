"""Generate the committed play + network-wave icon assets. Pillow is a development dependency only."""
from pathlib import Path
from PIL import Image, ImageDraw
out=Path(__file__).resolve().parents[1]/'src/Snapcast.Tray/Assets'
out.mkdir(parents=True,exist_ok=True)
colors={'Playing':('#12362C','#83F4B1'),'Connected':('#10323B','#71E4FA'),'Disconnected':('#252D37','#BBC5D2'),'Unavailable':('#42262B','#FFA0A0')}
for name,(back,front) in colors.items():
    im=Image.new('RGBA',(512,512),(0,0,0,0));d=ImageDraw.Draw(im)
    d.rounded_rectangle((16,16,496,496),radius=112,fill=back)
    d.arc((66,74,446,454),215,325,fill=front,width=30)
    d.arc((136,144,376,384),215,325,fill=front,width=28)
    triangle=[(205,275),(205,409),(326,342)]
    if name in ('Playing','Unavailable'):d.polygon(triangle,fill=front)
    else:d.line(triangle+[triangle[0]],fill=front,width=25,joint='curve')
    im=im.resize((256,256),Image.Resampling.LANCZOS)
    im.save(out/('Tray'+name+'.ico'),sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(256,256)])
    if name=='Playing':im.save(out/'Snapcast.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
