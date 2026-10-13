"""Does a font have Cyrillic glyphs? A missing glyph renders as the font's .notdef box."""
import sys
from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding='utf-8')


def render(f, ch):
    im = Image.new('L', (80, 80), 0)
    ImageDraw.Draw(im).text((10, 10), ch, font=f, fill=255)
    return im.tobytes()


for path in sys.argv[1:]:
    f = ImageFont.truetype(path, 48)
    notdef = render(f, '￿')
    res = {ch: render(f, ch) != notdef for ch in 'ЖЯфыБ'}
    print(path.split('\\')[-1], 'Cyrillic:', 'yes' if all(res.values()) else 'NO', res)
