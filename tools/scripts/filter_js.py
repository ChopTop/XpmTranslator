"""Classify extracted literals: ui / log / city / lottie. Writes todo list of unique UI texts."""
import json, re, sys

sys.stdout.reconfigure(encoding='utf-8')
d = json.load(open(sys.argv[1], encoding='utf-8'))
src = open(sys.argv[3], encoding='utf-8').read()
city_start = src.find("JSON.parse('{\"d\":[")
city_end = src.find("')", city_start)
for o in d:
    if city_start <= o['start'] <= city_end:
        o['kind'] = 'city'
    elif re.search(r'(?:"nm"|\bnm|"mn"|\bmn|"cl"|"ln")\s*:\s*$', o['ctx']):
        o['kind'] = 'lottie'
json.dump(d, open(sys.argv[1], 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
seen = {}
for o in d:
    if o['kind'] == 'ui':
        seen.setdefault(o['text'], o['ctx'][-25:])
todo = [{'zh': t, 'ctx': c} for t, c in seen.items()]
json.dump(todo, open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
from collections import Counter
print(Counter(o['kind'] for o in d), 'unique ui:', len(todo), 'chars:', sum(len(t['zh']) for t in todo))
