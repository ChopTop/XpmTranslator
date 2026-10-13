"""Redraw the 'don't remind me' checkbox images and write the translated popup XML.

usage: skin_render.py <skin dir> <out skin dir>
"""
import glob, os, re, sys
from PIL import Image, ImageDraw, ImageFont

skin, out = sys.argv[1:3]
os.makedirs(os.path.join(out, 'image'), exist_ok=True)
os.makedirs(os.path.join(out, 'xml'), exist_ok=True)
FONT = r'C:\Windows\Fonts\segoeui.ttf'
LABEL = "Don't remind me again"

for p in glob.glob(os.path.join(skin, 'image', 'NoLongerRemind*.png')):
    im = Image.open(p).convert('RGBA')
    w, h = im.size
    px = im.load()
    # text starts right of the radio circle: first fully transparent column after the circle
    col_has = [any(px[x, y][3] > 20 for y in range(h)) for x in range(w)]
    x = 0
    while x < w and not col_has[x]:
        x += 1
    while x < w and col_has[x]:
        x += 1  # end of circle
    gap_end = x
    while gap_end < w and not col_has[gap_end]:
        gap_end += 1
    text_x = gap_end
    ys = [y for y in range(h) for xx in range(text_x, w) if px[xx, y][3] > 20]
    colour = max((px[xx, y] for y in range(h) for xx in range(text_x, w)), key=lambda c: c[3])
    top, bottom = min(ys), max(ys)
    for yy in range(h):
        for xx in range(text_x, w):
            px[xx, yy] = (0, 0, 0, 0)
    # Chinese glyph box ~ 1.25x Latin cap height; size so cap height ~ 0.72 of the old glyph box
    target_cap = (bottom - top + 1) * 0.72
    size = 8
    while True:
        f = ImageFont.truetype(FONT, size + 1)
        b = f.getbbox('D')
        if b[3] - b[1] > target_cap:
            break
        size += 1
    f = ImageFont.truetype(FONT, size)
    d = ImageDraw.Draw(im)
    d.text((text_x, (top + bottom) / 2), LABEL, font=f, fill=colour, anchor='lm')
    im.save(os.path.join(out, 'image', os.path.basename(p)), optimize=True)

xml = open(os.path.join(skin, 'xml', 'LowPowerAlertWnd.xml'), encoding='utf-8').read()
repl = {
    '正在使用非标配充电器充电': 'Non-standard charger in use',
    '您的充电器不是官方充电器，可能会造成充电速度慢、充电口过热、断充等问题。':
        'This is not an official charger. It may charge slowly, overheat the port or stop charging.',
    '我知道了': 'Got it',
    '<!-- 基本构成-通知标题和关闭按钮 -->': '<!-- title and close button -->',
    '<!-- 内容构成 -->': '<!-- content -->',
    '<!-- 只有内容描述 -->': '<!-- description only -->',
    '<!-- 基本构成-按钮 -->': '<!-- buttons -->',
}
for k, v in repl.items():
    assert k in xml, k
    xml = xml.replace(k, v)
assert not re.search('[\u4e00-\u9fff]', xml)
open(os.path.join(out, 'xml', 'LowPowerAlertWnd.xml'), 'w', encoding='utf-8', newline='').write(xml)
print('ok')
