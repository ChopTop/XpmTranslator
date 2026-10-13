"""List unique Chinese string values (app-owned resources only) from a makepri Detailed dump.

Resources that already have an EN-US candidate (WinUI/Toolkit system strings) are skipped:
Windows picks those by language on its own.
"""
import json, re, sys
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8')
CJK = re.compile('[一-鿿]')
root = ET.parse(sys.argv[1]).getroot()
vals = {}
for nr in root.iter('NamedResource'):
    name = nr.get('uri', '').replace('ms-resource://XiaomiPcManager/', '')
    cands = [c for c in nr.iter('Candidate') if c.get('type') == 'String']
    langs = {q.get('value') for c in cands for q in c.iter('Qualifier') if q.get('name') == 'Language'}
    if langs & {'EN-US', 'EN-GB'}:
        continue
    for cand in cands:
        v = cand.find('Value')
        if v is not None and v.text and CJK.search(v.text):
            vals.setdefault(v.text, []).append(name)
todo = [{'zh': k, 'names': v} for k, v in vals.items()]
json.dump(todo, open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
with open(sys.argv[3], 'w', encoding='utf-8') as f:
    for i, t in enumerate(todo):
        n = t['names'][0].split('/')
        f.write(f"{i} [{'/'.join(n[-3:-1])}] {t['zh']!r}\n")
print('unique', len(todo))
