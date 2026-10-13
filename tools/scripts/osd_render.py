"""Replace the Chinese caption on OSD images with English text.

usage: osd_render.py <srcdir> <outdir> <font.ttf>
For every image whose base name is in LABELS (all themes/scales, including *_En),
the caption band is located, erased with the tile background colour, and the English
label is drawn centred at the same vertical position. Font size is derived from the
cap height of the stock English "Silent Mode" image at the same scale.
"""
import glob, os, re, sys
from PIL import Image, ImageDraw, ImageFont

LABELS = {
    'KeyboardLightAuto': 'Auto',
    'MuteOn': 'Mic off',
    'MuteOff': 'Mic on',
    'NewIntelligentMode': 'Smart mode',
    'NewLongBatteryMode': 'Battery saver',
    'WorkloadSilent': 'Silent mode',
    'WorkloadBalance': 'Balanced mode',
    'WorkloadSpeed': 'Performance',
    'WorkloadDeception': 'Beast mode',
    'WorkloadTurbo': 'Full speed',
}
src, out, fontp = sys.argv[1:4]
os.makedirs(out, exist_ok=True)
NAME = re.compile(r'^(?P<base>[A-Za-z]+?)(?:_En)?_(?P<theme>Dark|Light)(?:@(?P<scale>\d+))?\.png$')


def band_bbox(im, y0):
    """bbox of the caption: the lowest block of non-background rows, scanning up to y0.

    Background is sampled at the tile's left margin, so icons reaching into the caption
    band (e.g. the microphone stand) are not mistaken for it.
    """
    w, h = im.size
    px = im.load()
    x_lo, x_hi = w // 12, w - w // 12

    def diff(p, bg):
        return abs(p[3] - bg[3]) > 30 or (p[3] > 0 and sum(abs(p[i] - bg[i]) for i in range(3)) > 90)

    rows = {}
    for y in range(y0, h - max(2, h // 40)):
        bg = px[w // 16, y]
        xs = [x for x in range(x_lo, x_hi) if diff(px[x, y], bg)]
        if xs:
            rows[y] = (min(xs), max(xs) + 1, bg)
    if not rows:
        return None, None
    ys = sorted(rows)
    # walk up from the bottom-most content row while rows are contiguous (allow tiny gaps)
    block = [ys[-1]]
    for y in reversed(ys[:-1]):
        if block[-1] - y <= max(2, h // 80):
            block.append(y)
        else:
            break
    top, bottom = min(block), max(block) + 1
    x0 = min(rows[y][0] for y in block)
    x1 = max(rows[y][1] for y in block)
    bg = rows[bottom - 1][2]
    return (x0, top, x1, bottom), bg


def text_colour(im, box):
    best = None
    px = im.load()
    for y in range(box[1], box[3]):
        for x in range(box[0], box[2]):
            p = px[x, y]
            if best is None or p[3] > best[3] or (p[3] == best[3] and abs(p[0] - 128) > abs(best[0] - 128)):
                best = p
    return best


def cap_height_ref(scale_tag, theme):
    ref = os.path.join(src, f'WorkloadSilent_En_{theme}{scale_tag}.png')
    im = Image.open(ref).convert('RGBA')
    box, _ = band_bbox(im, int(im.height * 0.66))
    return box[3] - box[1], (box[1] + box[3]) / 2 / im.height


done = 0
for p in sorted(glob.glob(os.path.join(src, '*.png'))):
    fn = os.path.basename(p)
    m = NAME.match(fn)
    if not m or m.group('base') not in LABELS:
        continue
    theme = m.group('theme')
    scale_tag = f"@{m.group('scale')}" if m.group('scale') else ''
    im = Image.open(p).convert('RGBA')
    w, h = im.size
    box, bg = band_bbox(im, int(h * 0.66))
    if box is None:
        print('no caption found', fn)
        continue
    col = text_colour(im, box)
    capref, cy_ratio = cap_height_ref(scale_tag, theme)
    # erase caption: repaint band with exact background pixel
    pad = max(2, h // 64)
    erase = Image.new('RGBA', (box[2] - box[0] + 2 * pad, box[3] - box[1] + 2 * pad), bg)
    im.paste(erase, (box[0] - pad, box[1] - pad))
    label = LABELS[m.group('base')]
    # font size so that cap height ('S') equals reference cap height
    size = max(6, capref)
    for _ in range(40):
        f = ImageFont.truetype(fontp, size)
        b = f.getbbox('S')
        ch = b[3] - b[1]
        if ch >= capref:
            break
        size += 1
    f = ImageFont.truetype(fontp, size)
    while f.getlength(label) > w * 0.88 and size > 6:
        size -= 1
        f = ImageFont.truetype(fontp, size)
    layer = Image.new('RGBA', im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cy = (box[1] + box[3]) / 2  # keep original caption centre
    d.text((w / 2, cy), label, font=f, fill=col, anchor='mm')
    im.alpha_composite(layer)
    im.save(os.path.join(out, fn), optimize=True)
    done += 1
print('rendered', done)
