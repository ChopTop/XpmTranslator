"""Extract every JS string/template literal containing Chinese (\\uXXXX-escaped) from a minified bundle.

Anchors on each escaped-CJK run and expands to the enclosing quotes, which avoids having to
tokenize regex literals. Output JSON list of {start, end, q, kind, raw, text, ctx}; start/end
are offsets of the literal body (without quotes).
"""
import json, re, sys

sys.stdout.reconfigure(encoding='utf-8')
src = open(sys.argv[1], encoding='utf-8').read()
ESC = re.compile(r'\\u[4-9][0-9a-fA-F]{3}')
U = re.compile(r'\\u([0-9a-fA-F]{4})')
CJK = re.compile('[一-鿿]')
LOGGERS = ('V(', 'console.log(', 'console.error(', 'console.warn(', 'console.info(', 'console.debug(')


def escaped(p):
    k, b = p - 1, 0
    while k >= 0 and src[k] == '\\':
        b += 1
        k -= 1
    return b % 2 == 1


def find_left(p):
    # nearest unescaped quote to the left that is not inside ${...} of a template
    depth = 0
    k = p - 1
    while k >= 0:
        ch = src[k]
        if ch == '}' :
            depth += 1
        elif ch == '{' and depth:
            depth -= 1
        elif ch in '"\'`' and not escaped(k):
            if depth == 0:
                return k, ch
            # quote inside a template expression: skip the inner string
            q = ch
            k -= 1
            while k >= 0 and not (src[k] == q and not escaped(k)):
                k -= 1
        elif ch == '\n':
            return None, None
        k -= 1
    return None, None


def find_right(p, q):
    k = p
    depth = 0
    n = len(src)
    while k < n:
        ch = src[k]
        if ch == '\\':
            k += 2
            continue
        if q == '`' and ch == '$' and src[k + 1:k + 2] == '{':
            depth += 1
            k += 2
            continue
        if depth:
            if ch == '}':
                depth -= 1
            elif ch in '"\'':
                k += 1
                while k < n and not (src[k] == ch and not escaped(k)):
                    k += 1
            k += 1
            continue
        if ch == q:
            return k
        if ch == '\n' and q != '`':
            return None
        k += 1
    return None


spans = {}
for m in ESC.finditer(src):
    p = m.start()
    if any(s <= p < e for s, e in list(spans)[-3:]):
        continue
    l, q = find_left(p)
    if l is None:
        continue
    r = find_right(l + 1, q)
    if r is None or r < p:
        continue
    spans[(l + 1, r)] = q

out = []
for (s, e), q in sorted(spans.items()):
    raw = src[s:e]
    text = U.sub(lambda m: chr(int(m.group(1), 16)), raw)
    if not CJK.search(text):
        continue
    pre = src[max(0, s - 60):s - 1]
    kind = 'log' if any(pre.rstrip().endswith(x) for x in LOGGERS) else 'ui'
    if re.search(r'(?:^|[^\w$.])(?:V|console\.\w+)\($', pre):
        kind = 'log'
    out.append({'start': s, 'end': e, 'q': q, 'kind': kind, 'raw': raw, 'text': text, 'ctx': pre[-40:]})

uniq = {}
for o in out:
    uniq.setdefault(o['text'], o)
ui = [o for o in uniq.values() if o['kind'] == 'ui']
print('literals', len(out), 'unique', len(uniq), 'ui-unique', len(ui), 'log-unique', len(uniq) - len(ui),
      'ui chars', sum(len(o['text']) for o in ui))
json.dump(out, open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
