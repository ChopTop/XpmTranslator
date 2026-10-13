"""Collect everything the translator needs into its Data folder.

usage: make_data.py <scratchpad> <orig dir> <data dir>
"""
import hashlib, json, os, shutil, sys

sp, orig, data = sys.argv[1:4]
tr = os.path.join(sp, 'tr')
os.makedirs(data, exist_ok=True)


def load_lines(path):
    d = {}
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if not line.strip():
            continue
        k, _, v = line.partition(' ')
        if v.endswith('§'):
            v = v[:-1] + ' '
        d[int(k)] = v
    return d


# web UI: zh text -> en (JS source form: may contain \n, \xd7 escapes)
todo = json.load(open(os.path.join(tr, 'js_todo.json'), encoding='utf-8'))
en = load_lines(os.path.join(tr, 'js_en.txt'))
js_map = {t['zh']: en[i] for i, t in enumerate(todo)}
json.dump(js_map, open(os.path.join(data, 'web.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

shutil.copy(os.path.join(tr, 'pri_map.json'), os.path.join(data, 'pri.json'))
shutil.copy(os.path.join(tr, 'cs_pcc.json'), os.path.join(data, 'asm_PcControlCenter.json'))
shutil.copy(os.path.join(tr, 'cs_sub.json'), os.path.join(data, 'asm_SubtitleTranscriptor.json'))
shutil.copy(os.path.join(tr, 'cs_main.json'), os.path.join(data, 'asm_XiaomiPcManager.json'))
shutil.copy(os.path.join(sp, 'out', 'dist', 'static', 'css', 'custom.css'), os.path.join(data, 'custom.css'))
json.dump({
    '正在使用非标配充电器充电': 'Non-standard charger in use',
    '您的充电器不是官方充电器，可能会造成充电速度慢、充电口过热、断充等问题。':
        'This is not an official charger. It may charge slowly, overheat the port or stop charging.',
    '我知道了': 'Got it',
    '<!-- 基本构成-通知标题和关闭按钮 -->': '<!-- title and close button -->',
    '<!-- 内容构成 -->': '<!-- content -->',
    '<!-- 只有内容描述 -->': '<!-- description only -->',
    '<!-- 基本构成-按钮 -->': '<!-- buttons -->',
}, open(os.path.join(data, 'skin.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

# images: pre-rendered English versions, stored under their app-relative path
img = os.path.join(data, 'images')
shutil.rmtree(img, ignore_errors=True)
os.makedirs(os.path.join(img, 'res', 'Image'))
os.makedirs(os.path.join(img, 'skin', 'image'))
for f in os.listdir(os.path.join(sp, 'out', 'osd')):
    shutil.copy(os.path.join(sp, 'out', 'osd', f), os.path.join(img, 'res', 'Image', f))
for f in os.listdir(os.path.join(sp, 'out', 'skin', 'image')):
    shutil.copy(os.path.join(sp, 'out', 'skin', 'image', f), os.path.join(img, 'skin', 'image', f))

# sha256 of the pristine 5.3.2.407 files we touch
hashes = {}
for dp, _, fs in os.walk(orig):
    for f in fs:
        p = os.path.join(dp, f)
        rel = os.path.relpath(p, orig).replace('/', '\\')
        hashes[rel] = hashlib.sha256(open(p, 'rb').read()).hexdigest()
json.dump({'5.3.2.407': dict(sorted(hashes.items()))}, open(os.path.join(data, 'originals.json'), 'w'), indent=1)
print('web', len(js_map), 'pri', len(json.load(open(os.path.join(data, 'pri.json'), encoding='utf-8'))),
      'images', sum(len(fs) for _, _, fs in os.walk(img)), 'originals', len(hashes))
