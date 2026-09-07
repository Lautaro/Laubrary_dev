from PIL import Image, ImageDraw, ImageFont
import os
font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 26)
RED=(230,40,40,255)
def annotate(src, out, points, boxes=()):
    im = Image.open(src).convert("RGBA")
    d = ImageDraw.Draw(im, "RGBA")
    for (x0,y0,x1,y1) in boxes:
        d.rectangle([x0,y0,x1,y1], outline=RED, width=4)
    for i,(x,y) in enumerate(points, 1):
        r=20
        d.ellipse([x-r,y-r,x+r,y+r], fill=(230,40,40,235), outline=(255,255,255,255), width=3)
        t=str(i); bb=d.textbbox((0,0),t,font=font); tw,th=bb[2]-bb[0],bb[3]-bb[1]
        d.text((x-tw/2, y-th/2-4), t, font=font, fill=(255,255,255,255))
    im.convert("RGB").save(out); print("wrote", out, im.size)

# All coordinates are in the half-res "view-*.png" space.
annotate("view-builder-protoguy-meta.png", "annot-builder-split.png",
    [(600,100),(270,157),(1550,190),(190,282),(640,360),(560,450),(120,510),(600,557),(1200,760),(1330,866),(1480,573),(1250,1040),(1665,330)],
    boxes=[(952,497,1290,800)])
annotate("view-builder-protoguy-collapsed-bottom.png", "annot-builder-bottom.png",
    [(300,300),(760,590),(470,705),(450,780),(1100,830),(650,912),(80,935),(1420,993),(300,1070),(250,746)],
    boxes=[(90,283,395,395)])
annotate("view-builder-walk-sheet.png", "annot-builder-walk.png",
    [(1240,1055),(1220,520),(230,510),(1470,650),(690,481),(1500,440),(760,358),(950,225)],
    boxes=[(1060,1005,1270,1075)])
annotate("view-browser-protoguy.png", "annot-browser.png",
    [(150,180),(1200,67),(480,143),(900,238),(680,391),(870,520),(150,320),(80,67)])
annotate("view-catalog-grid.png", "annot-catalog.png",
    [(60,108),(230,252),(200,308),(22,520),(900,250),(190,921),(420,67)],
    boxes=[(378,212,1452,294)])
annotate("view-builder-empty.png", "annot-builder-empty.png",
    [(620,100),(280,157),(480,190),(700,225),(840,700)])
