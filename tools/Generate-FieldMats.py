# Regenerates LogPose/Assets/mat-*.png — the design-language playmats LogPose swaps onto
# the in-game field (see LogPose/UI/FieldMat.cs). The zone geometry MUST match the runtime
# re-zoning in LogPose/UI/BoardLayoutPatches.cs: cards live in a 1920x1080 center-origin
# canvas, each 860x500 mat Image displays this 1720x1000 texture at half scale, and a zone
# whose card center sits at canvas (x, y) lands on texel (860 + 2*lx, 2*|y| - 500 + 500).
# Layout follows the official OPTCG mat: deck on the leader row (far right), trash directly
# below it, and the DON!! strip spanning the outer band from the DON!! deck to the trash.
# The top half in-game is the player texture rotated 180 (point mirror); the -opp textures
# are legacy and regenerated only for completeness.
# Run from the repo root:  python tools/Generate-FieldMats.py
from PIL import Image, ImageDraw, ImageFont

W, H = 1720, 1000
CX = W // 2                     # texel x of the field center
CARD_W, CARD_H = 200, 276       # a 100x138 card at texture scale

# Zone card-centers in canvas coords, shared with BoardLayoutPatches.Rezone (player side;
# y is the magnitude — the player half uses -y, the opponent half +y).
DEPLOY = [(-240 + 140 * i, 90) for i in range(5)]
LEADER_P, STAGE_P = (48, 250), (167, 250)      # player: leader left of stage
LEADER_O, STAGE_O = (-48, 250), (-167, 250)    # opponent: point-mirrored (stage left)
LIFE_X, LIFE_Y0, LIFE_STEP = -375, 238, 25
DON_DECK = (-375, 408)
DON_COST_X0, DON_COST_STEP, DON_COST_N = -280, 58, 10
DECK = (380, 250)               # leader row, far right — official mat structure
TRASH = (380, 408)
DOCK = (140, 252)               # opponent hand cluster (art hint only)


def hexc(s):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16))


def blend(bg, fg, a):
    return tuple(int(b * (1 - a) + f * a) for b, f in zip(bg, fg))


font_big = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 52)
font_small = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 26)
font_logo = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 34)
font_logo2 = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 20)


def spaced(text):
    return " ".join(text)


def tex(x, y, opponent):
    """Canvas zone center -> texel center for the given half."""
    ly = (-y if not opponent else y) - (-250 if not opponent else 250)
    # player: card sits at canvas -y; opponent at +y. Fold the sign in here so callers
    # pass the magnitude-style tuples above.
    return CX + 2 * x, 500 - 2 * ly


def centered_text(d, cx, cy, text, font, fill):
    bb = d.textbbox((0, 0), text, font=font)
    d.text((cx - (bb[2] - bb[0]) // 2, cy - (bb[3] - bb[1]) // 2 - bb[1]), text, font=font, fill=fill)


class Half:
    def __init__(self, name, opponent, pal):
        self.opponent = opponent
        self.pal = pal
        self.img = Image.new('RGB', (W, H), pal['base'])
        self.d = ImageDraw.Draw(self.img)
        self.name = name

    def t(self, x, y):
        return tex(x, y, self.opponent)

    def cell(self, cx, cy, w, h, label=None, accent=False, edge_boost=0.0, label_dy=None):
        p = self.pal
        fill = p['accentfill'] if accent else p['slot']
        edge = blend(fill, p['accent'] if accent else p['white'], (0.35 if accent else 0.14) + edge_boost)
        x0, y0 = max(2, cx - w // 2), max(2, cy - h // 2)
        x1, y1 = min(W - 3, cx + w // 2), min(H - 3, cy + h // 2)
        self.d.rounded_rectangle((x0, y0, x1, y1), radius=12, fill=fill, outline=edge, width=2)
        if label:
            ly = cy + (label_dy if label_dy is not None else h // 2 - 22)
            centered_text(self.d, cx, ly, spaced(label), font_small, blend(fill, p['white'], 0.16))
        return (x0, y0, x1, y1)

    def band_placard(self, y0, y1, accent=False):
        p = self.pal
        fill = blend(p['base'], p['accentfill'] if accent else p['slot'], 0.55)
        edge = blend(fill, p['accent'] if accent else p['white'], 0.18 if accent else 0.08)
        self.d.rounded_rectangle((240, y0, 1640, y1), radius=16, fill=fill, outline=edge, width=2)
        return fill


def draw_half(h):
    p = h.pal
    d = h.d
    opp = h.opponent

    # subtle vignette on the outer edge (screen edge side)
    outer_top = opp  # opponent's outer band is at texture TOP
    if outer_top:
        d.rectangle((0, 0, W, 16), fill=blend(p['base'], (0, 0, 0), 0.12))
    else:
        d.rectangle((0, H - 16, W, H), fill=blend(p['base'], (0, 0, 0), 0.12))

    # ---- character band ----
    cx0, cy0 = h.t(*DEPLOY[0])
    band = h.band_placard(cy0 - 152, cy0 + 152)
    band_cx = (h.t(*DEPLOY[0])[0] + h.t(*DEPLOY[-1])[0]) // 2
    centered_text(d, band_cx, cy0, spaced("CHARACTER AREA"), font_big, blend(band, p['white'], 0.05))
    for zx, zy in DEPLOY:
        tx, ty = h.t(zx, zy)
        h.cell(tx, ty, 208, 284)

    # ---- middle band: life column, leader + stage + deck ----
    # The life rect's TOP edge lines up with the character slots' top edge; the bottom
    # reaches just past the lowest life card.
    char_ty = h.t(*DEPLOY[0])[1]
    life_ys = [h.t(LIFE_X, LIFE_Y0 - j * LIFE_STEP)[1] for j in range(5)]
    life_x = h.t(LIFE_X, LIFE_Y0)[0]
    if not opp:
        top = char_ty - 284 // 2                    # character slot top edge
        bot = max(life_ys) + CARD_H // 2 + 26
    else:
        top = min(life_ys) - CARD_H // 2 - 26
        bot = char_ty + 284 // 2
    colfill = p['slot']
    d.rounded_rectangle((life_x - 100, top, life_x + 100, bot), radius=14, fill=colfill,
                        outline=blend(colfill, p['white'], 0.12), width=2)
    centered_text(d, life_x, (bot - 14) if not opp else (top + 14), spaced("LIFE"),
                  font_small, blend(colfill, p['white'], 0.16))

    leader = LEADER_P if not opp else LEADER_O
    stage = STAGE_P if not opp else STAGE_O
    deck = DECK if not opp else (-DECK[0], DECK[1])
    ltx, lty = h.t(*leader)
    stx, sty = h.t(*stage)
    h.cell(ltx, lty, 192, 264, "LEADER", edge_boost=0.0 if opp else 0.28, label_dy=110)
    h.cell(stx, sty, 192, 264, "STAGE", label_dy=110)
    ktx, kty = h.t(*deck)
    h.cell(ktx, kty, 200, 280, "DECK", label_dy=0)

    if opp:
        dtx, dty = h.t(*DOCK)
        centered_text(d, dtx, dty, spaced("HAND"), font_small, blend(p['base'], p['white'], 0.07))

    # ---- outer band: don pile, cost strip spanning to the trash pile ----
    don_deck = DON_DECK if not opp else (-DON_DECK[0], DON_DECK[1])
    trash = TRASH if not opp else (-TRASH[0], TRASH[1])
    dtx2, dty2 = h.t(*don_deck)
    ttx, tty = h.t(*trash)
    strip_x0 = min(dtx2, ttx) + CARD_W // 2 + 22
    strip_x1 = max(dtx2, ttx) - CARD_W // 2 - 22
    sy0 = dty2
    strip_fill = blend(p['base'], p['accentfill'], 0.85)
    d.rounded_rectangle((strip_x0, sy0 - 162, strip_x1, sy0 + 162), radius=14, fill=strip_fill,
                        outline=blend(strip_fill, p['accent'], 0.35), width=2)
    wm = spaced("YOUR DON!!" if not opp else "DON!!")
    centered_text(d, (strip_x0 + strip_x1) // 2, sy0, wm, font_big, blend(strip_fill, p['white'], 0.06))

    h.cell(dtx2, dty2, 200, 280, "DON!!", accent=True, label_dy=0)
    h.cell(ttx, tty, 196, 280, "TRASH", label_dy=0)


PALETTES = {
    'nocturne': dict(base=hexc('#1b1d2c'), slot=hexc('#14161f'), white=hexc('#e9e9ed'),
                     accentfill=hexc('#232032'), accent=hexc('#9184d9')),
    'batsu': dict(base=hexc('#1d1626'), slot=hexc('#151020'), white=hexc('#f0e9f2'),
                  accentfill=hexc('#2a1430'), accent=hexc('#d81fb4')),
}

for cw, pal in PALETTES.items():
    for opp in (False, True):
        name = r"LogPose/Assets/mat-%s%s.png" % (cw, "-opp" if opp else "")
        half = Half(name, opp, pal)
        draw_half(half)
        half.img.save(name, 'PNG')
        print("wrote", name)
