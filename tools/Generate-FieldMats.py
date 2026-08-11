# Regenerates LogPose/Assets/mat-*.png — the design-language playmats the mod swaps onto
# the in-game field (see LogPose/UI/FieldMat.cs). Zone rects were traced from the vanilla
# StreamingAssets\Playmats\Red.png (1414x1000; all color sheets share the geometry) via
# design/redesign/vanilla-mat-grid.png. Run from the repo root:  python tools/Generate-FieldMats.py
from PIL import Image, ImageDraw, ImageFont

W, H = 1414, 1000

ZONES = {
    'char':   (176, 30, 1386, 326),
    'leader': (700, 356, 890, 624),
    'stage':  (932, 356, 1134, 624),
    'deck':   (1174, 356, 1376, 624),
    'don':    (36, 676, 216, 970),
    'cost':   (248, 676, 1148, 970),
    'trash':  (1190, 700, 1380, 950),
}


def hexc(s):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16))


def blend(bg, fg, a):
    return tuple(int(b * (1 - a) + f * a) for b, f in zip(bg, fg))


font_big = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 52)
font_small = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 30)
font_logo = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 40)
font_logo2 = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 24)


def spaced(text):
    return " ".join(text)


def make(base, slotfill, white, accentfill, accentcol, name):
    img = Image.new('RGB', (W, H), base)
    d = ImageDraw.Draw(img)
    shade = blend(base, (0, 0, 0), 0.10)
    d.rectangle((0, 0, W, H // 6), fill=shade)
    d.rectangle((0, H - H // 6, W, H), fill=shade)

    for key, box in ZONES.items():
        if key in ('don', 'cost'):
            d.rounded_rectangle(box, radius=16, fill=accentfill,
                                outline=blend(accentfill, accentcol, 0.35), width=2)
        else:
            d.rounded_rectangle(box, radius=16, fill=slotfill,
                                outline=blend(slotfill, white, 0.14), width=2)

    def watermark(box, label, font, bg):
        bx = (box[0] + box[2]) // 2, (box[1] + box[3]) // 2
        t = spaced(label)
        bb = d.textbbox((0, 0), t, font=font)
        d.text((bx[0] - (bb[2] - bb[0]) // 2, bx[1] - (bb[3] - bb[1]) // 2 - bb[1]),
               t, font=font, fill=blend(bg, white, 0.15))

    watermark(ZONES['char'], "CHARACTER AREA", font_big, slotfill)
    watermark(ZONES['cost'], "COST AREA", font_big, accentfill)
    watermark(ZONES['leader'], "LEADER", font_small, slotfill)
    watermark(ZONES['stage'], "STAGE", font_small, slotfill)
    watermark(ZONES['deck'], "DECK", font_small, slotfill)
    watermark(ZONES['trash'], "TRASH", font_small, slotfill)
    watermark(ZONES['don'], "DON!!", font_small, accentfill)

    cx, cy = 300, 480
    t1 = spaced("ONE PIECE")
    bb = d.textbbox((0, 0), t1, font=font_logo)
    d.text((cx - (bb[2] - bb[0]) // 2, cy - 40), t1, font=font_logo, fill=blend(base, white, 0.12))
    t2 = spaced("CARD GAME")
    bb2 = d.textbbox((0, 0), t2, font=font_logo2)
    d.text((cx - (bb2[2] - bb2[0]) // 2, cy + 14), t2, font=font_logo2, fill=blend(base, white, 0.10))

    img.save(name, 'PNG')
    print("wrote", name)


make(hexc('#1b1d2c'), hexc('#14161f'), hexc('#e9e9ed'), hexc('#232032'), hexc('#9184d9'),
     r"LogPose/Assets/mat-nocturne.png")
make(hexc('#1d1626'), hexc('#151020'), hexc('#f0e9f2'), hexc('#2a1430'), hexc('#d81fb4'),
     r"LogPose/Assets/mat-batsu.png")
