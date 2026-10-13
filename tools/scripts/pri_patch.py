"""Rewrite string data items inside a PRI (mrm_pri2) file.

usage: pri_patch.py <in.pri> <out.pri> <map.json>   (map.json: {"original": "replacement"})
       pri_patch.py <in.pri> <out.pri> <map.json> --blobs <blobmap.json>
         blobmap.json: {"sha1 of original blob": "path/to/new blob file"}

Only [mrm_dataitem] sections are rebuilt; resource maps reference items by index, so
changing lengths is safe as long as offsets inside the section and the TOC are recomputed.
"""
import hashlib, json, struct, sys

HDR = 32          # section header size
TRAILER = 8       # 0xDEF5FADE + section length


def u16z(b):
    return len(b) >= 2 and b.endswith(b'\x00\x00') and len(b) % 2 == 0


def decode(raw):
    """Return (text, encoding) for a data item or (None, None)."""
    if raw.endswith(b'\x00') and b'\x00' not in raw[:-1]:
        try:
            return raw[:-1].decode('utf-8'), 'utf-8'
        except UnicodeDecodeError:
            pass
    if u16z(raw):
        try:
            t = raw[:-2].decode('utf-16le')
            if '\x00' not in t:
                return t, 'utf-16le'
        except UnicodeDecodeError:
            pass
    return None, None


def encode(text, enc):
    return text.encode(enc) + (b'\x00' if enc == 'utf-8' else b'\x00\x00')


def pad(b, n):
    return b + b'\x00' * (-len(b) % n)


def rebuild_dataitem(sec, smap, bmap, stats):
    ident = sec[:16]
    q, f, sf, slen, unk = struct.unpack_from('<IHHII', sec, 16)
    c = HDR
    u0, ns, nb, tdl = struct.unpack_from('<IHHI', sec, c)
    p = c + 12
    sinfo = [struct.unpack_from('<HH', sec, p + 4 * k) for k in range(ns)]
    p += 4 * ns
    binfo = [struct.unpack_from('<II', sec, p + 8 * k) for k in range(nb)]
    p += 8 * nb
    ds = p
    strings = [sec[ds + o:ds + o + l] for o, l in sinfo]
    blobs = [sec[ds + o:ds + o + l] for o, l in binfo]
    changed = False
    for k, raw in enumerate(strings):
        t, enc = decode(raw)
        if t is not None and t in smap:
            strings[k] = encode(smap[t], enc)
            stats.setdefault('hit', set()).add(t)
            stats['strings'] += 1
            changed = True
    for k, raw in enumerate(blobs):
        h = hashlib.sha1(raw).hexdigest()
        if h in bmap:
            blobs[k] = bmap[h]
            stats['blobs'] += 1
            changed = True
            continue
        if len(raw) < 4096 and not raw.startswith(b'XBF'):
            t, enc = decode(raw)  # some short strings are stored as blobs
            if t is not None and t in smap:
                blobs[k] = encode(smap[t], enc)
                stats.setdefault('hit', set()).add(t)
                stats['strings'] += 1
                changed = True
    if not changed:
        return sec
    data = b''
    new_s = []
    for raw in strings:
        new_s.append((len(data), len(raw)))
        data = pad(data + raw, 4)
    if len(data) > 0xFFFF:
        raise SystemExit('string region exceeds 64K in a dataitem section')
    data = pad(data, 8)
    new_b = []
    for raw in blobs:
        new_b.append((len(data), len(raw)))
        data = pad(data + raw, 8)
    body = struct.pack('<IHHI', u0, ns, nb, len(data))
    body += b''.join(struct.pack('<HH', o, l) for o, l in new_s)
    body += b''.join(struct.pack('<II', o, l) for o, l in new_b)
    body += data
    body = pad(body, 8)
    total = HDR + len(body) + TRAILER
    out = ident + struct.pack('<IHHII', q, f, sf, total, unk) + body + struct.pack('<II', 0xDEF5FADE, total)
    return out


def main():
    src, dst, mpath = sys.argv[1:4]
    smap = json.load(open(mpath, encoding='utf-8'))
    bmap = {}
    if '--blobs' in sys.argv:
        for h, path in json.load(open(sys.argv[sys.argv.index('--blobs') + 1], encoding='utf-8')).items():
            bmap[h] = open(path, 'rb').read()
    b = open(src, 'rb').read()
    assert b[:8] == b'mrm_pri2'
    u, total, tocoff, secstart, nsec, x = struct.unpack_from('<IIIIHH', b, 8)
    assert total == len(b)
    toc = []
    for i in range(nsec):
        e = tocoff + i * 32
        ident = b[e:e + 16]
        flags, sflags, qual, off, length = struct.unpack_from('<HHIII', b, e + 16)
        toc.append([ident, flags, sflags, qual, off, length])
    stats = {'strings': 0, 'blobs': 0}
    secs = []
    for ident, flags, sflags, qual, off, length in toc:
        s = b[secstart + off:secstart + off + length]
        if ident.startswith(b'[mrm_dataitem]'):
            s = rebuild_dataitem(s, smap, bmap, stats)
        secs.append(s)
    out = bytearray(b[:secstart])
    off = 0
    for i, s in enumerate(secs):
        toc[i][4] = off
        toc[i][5] = len(s)
        off += len(s)
    for i, (ident, flags, sflags, qual, o, l) in enumerate(toc):
        struct.pack_into('<HHIII', out, tocoff + i * 32 + 16, flags, sflags, qual, o, l)
    for s in secs:
        out += s
    footer = struct.pack('<II', 0xDEFFFADE, 0) + b'mrm_pri2'
    newtotal = len(out) + len(footer)
    out += struct.pack('<II', 0xDEFFFADE, newtotal) + b'mrm_pri2'
    struct.pack_into('<I', out, 12, newtotal)
    assert b[-16:-12] == struct.pack('<I', 0xDEFFFADE), 'unexpected footer'
    open(dst, 'wb').write(out)
    miss = [k for k in smap if k not in stats.get('hit', set())]
    sys.stdout.reconfigure(encoding='utf-8')
    for k in miss: print('UNMATCHED', repr(k[:70]))
    print(f"patched {stats['strings']} strings, {stats['blobs']} blobs; size {len(b)} -> {newtotal}")


main()
