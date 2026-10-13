"""Build translated main.js from the original bundle.

usage: apply_js.py <orig main.js> <js_all.json> <js_todo.json> <js_en.txt> <out main.js>
"""
import json, re, sys

sys.stdout.reconfigure(encoding='utf-8')
src = open(sys.argv[1], encoding='utf-8').read()
lits = json.load(open(sys.argv[2], encoding='utf-8'))
todo = json.load(open(sys.argv[3], encoding='utf-8'))
en = {}
for line in open(sys.argv[4], encoding='utf-8'):
    line = line.rstrip('\n')
    if not line.strip():
        continue
    k, _, v = line.partition(' ')
    if v.endswith('§'):  # trailing-space marker
        v = v[:-1] + ' '
    en[int(k)] = v
zh2en = {}
TOK = re.compile(r'\$\{[^}]*\}|%[sd]')
bad = 0
for i, t in enumerate(todo):
    if i not in en:
        print('MISSING', i, t['zh'])
        bad += 1
        continue
    a = sorted(TOK.findall(t['zh']))
    b = sorted(TOK.findall(en[i]))
    if a != b:
        print('TOKEN MISMATCH', i, a, b)
        bad += 1
    zh2en[t['zh']] = en[i]
if bad:
    sys.exit(f'{bad} problems')


def encode(v, q):
    out = []
    k = 0
    while k < len(v):
        ch = v[k]
        if ch == '\\':  # keep source escapes written in the dictionary (\n, \xd7, \\)
            out.append(v[k:k + 2])
            k += 2
            continue
        if ch == q:
            out.append('\\' + ch)
        elif ord(ch) > 0x7e:
            out.append('\\u%04x' % ord(ch))
        else:
            out.append(ch)
        k += 1
    return ''.join(out)


parts, last, n = [], 0, 0
for o in sorted(lits, key=lambda o: o['start']):
    if o['kind'] != 'ui':
        continue
    if o['start'] < last:
        continue
    v = zh2en.get(o['text'])
    if v is None:
        continue
    assert src[o['start']:o['end']] == o['raw']
    parts.append(src[last:o['start']])
    parts.append(encode(v, o['q']))
    last = o['end']
    n += 1
parts.append(src[last:])
res = ''.join(parts)
open(sys.argv[5], 'w', encoding='utf-8', newline='').write(res)
left = len(re.findall(r'\\u[4-9][0-9a-fA-F]{3}', res))
print(f'replaced {n} literals; escaped CJK chars left: {left} (city/log/lottie data)')
