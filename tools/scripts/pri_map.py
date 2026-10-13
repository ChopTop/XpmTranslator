"""Build zh->en map for the PRI from pri_todo.json + pri_en.txt and report unmatched items."""
import json, re, struct, sys

sys.stdout.reconfigure(encoding='utf-8')
sp = sys.argv[1]
todo = json.load(open(sp + '/tr/pri_todo.json', encoding='utf-8'))
en = {}
for line in open(sp + '/tr/pri_en.txt', encoding='utf-8'):
    line = line.rstrip('\n')
    if not line:
        continue
    k, _, v = line.partition(' ')
    if v.endswith('§'):  # trailing-space marker
        v = v[:-1] + ' '
    en[int(k)] = v.replace('\\n', '\n')
TOK = re.compile(r'\{\d\}|%\d!?\w?!?|\n')
m, bad = {}, 0
for i, t in enumerate(todo):
    if i not in en:
        print('MISSING', i)
        bad += 1
        continue
    if sorted(TOK.findall(t['zh'])) != sorted(TOK.findall(en[i])):
        print('TOKENS', i, repr(t['zh'][:40]), repr(en[i][:40]))
        bad += 1
    m[t['zh']] = en[i]
json.dump(m, open(sp + '/tr/pri_map.json', 'w', encoding='utf-8'), ensure_ascii=False)
print('map', len(m), 'bad', bad)

# which values are not present as utf-8 data items?
b = open(sp + '/pri/orig.pri', 'rb').read()
missing = [k for k in m if (k.encode('utf-8') + b'\x00') not in b]
print('not found as utf-8 items:', len(missing))
for k in missing:
    u16 = k.encode('utf-16le') in b
    print('  ', repr(k[:50]), 'utf16-present' if u16 else '', 'utf8-substr' if k.encode('utf-8') in b else '')
