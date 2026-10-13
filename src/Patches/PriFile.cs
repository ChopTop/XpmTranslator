using System.Text;

namespace XpmTranslator.Patches;

/// <summary>
/// Rewrites string data items inside a PRI (mrm_pri2) resource file. Resource maps reference data items
/// by index, so only the [mrm_dataitem] sections, the TOC and the file header/footer need rebuilding.
/// Strings may be UTF-8 or UTF-16 and live either in the string table or (short ones) among the blobs.
/// </summary>
public static class PriFile
{
    const int Hdr = 32, Trailer = 8;
    static readonly UTF8Encoding Utf8Strict = new(false, true);
    static readonly UnicodeEncoding Utf16Strict = new(false, false, true);

    public static byte[] Patch(byte[] b, IReadOnlyDictionary<string, string> map, out int replaced, out List<string> unmatched)
    {
        if (Encoding.ASCII.GetString(b, 0, 8) != "mrm_pri2") throw new InvalidDataException("not a PRI file");
        uint total = U32(b, 12), tocoff = U32(b, 16), secstart = U32(b, 20);
        int nsec = U16(b, 24);
        if (total != b.Length) throw new InvalidDataException("PRI size mismatch");
        var hit = new HashSet<string>();
        int count = 0;
        var secs = new List<byte[]>();
        for (int i = 0; i < nsec; i++)
        {
            int e = (int)tocoff + i * 32;
            int off = (int)U32(b, e + 24), len = (int)U32(b, e + 28);
            var s = b.AsSpan((int)secstart + off, len).ToArray();
            if (Encoding.ASCII.GetString(b, e, 14) == "[mrm_dataitem]")
                s = Rebuild(s, map, hit, ref count);
            secs.Add(s);
        }
        if (U32(b, b.Length - 16) != 0xDEFFFADE) throw new InvalidDataException("unexpected PRI footer");
        int o = 0;
        var head = b.AsSpan(0, (int)secstart).ToArray();
        for (int i = 0; i < nsec; i++)
        {
            int e = (int)tocoff + i * 32;
            W32(head, e + 24, (uint)o);
            W32(head, e + 28, (uint)secs[i].Length);
            o += secs[i].Length;
        }
        var result = new MemoryStream();
        result.Write(head);
        foreach (var s in secs) result.Write(s);
        uint newTotal = (uint)(result.Length + 16);
        var footer = new byte[16];
        W32(footer, 0, 0xDEFFFADE);
        W32(footer, 4, newTotal);
        Encoding.ASCII.GetBytes("mrm_pri2").CopyTo(footer, 8);
        result.Write(footer);
        var r = result.ToArray();
        W32(r, 12, newTotal);
        replaced = count;
        unmatched = map.Keys.Where(k => !hit.Contains(k)).ToList();
        return r;
    }

    static byte[] Rebuild(byte[] sec, IReadOnlyDictionary<string, string> map, HashSet<string> hit, ref int count)
    {
        int c = Hdr;
        uint u0 = U32(sec, c);
        int ns = U16(sec, c + 4), nb = U16(sec, c + 6);
        int p = c + 12;
        var strings = new List<byte[]>();
        var blobs = new List<byte[]>();
        int ds = p + 4 * ns + 8 * nb;
        for (int k = 0; k < ns; k++)
            strings.Add(sec.AsSpan(ds + U16(sec, p + 4 * k), U16(sec, p + 4 * k + 2)).ToArray());
        int pb = p + 4 * ns;
        for (int k = 0; k < nb; k++)
            blobs.Add(sec.AsSpan(ds + (int)U32(sec, pb + 8 * k), (int)U32(sec, pb + 8 * k + 4)).ToArray());

        bool changed = false;
        for (int k = 0; k < strings.Count; k++)
            if (Decode(strings[k], out var t, out var enc) && map.TryGetValue(t, out var v))
            {
                strings[k] = Encode(v, enc);
                hit.Add(t); count++; changed = true;
            }
        for (int k = 0; k < blobs.Count; k++)
        {
            var raw = blobs[k];
            if (raw.Length >= 4096 || (raw.Length >= 3 && raw[0] == 'X' && raw[1] == 'B' && raw[2] == 'F')) continue;
            if (Decode(raw, out var t, out var enc) && map.TryGetValue(t, out var v))
            {
                blobs[k] = Encode(v, enc);
                hit.Add(t); count++; changed = true;
            }
        }
        if (!changed) return sec;

        var data = new MemoryStream();
        var sInfo = new List<(int, int)>();
        foreach (var raw in strings) { sInfo.Add(((int)data.Length, raw.Length)); data.Write(raw); Pad(data, 4); }
        if (data.Length > 0xFFFF) throw new InvalidDataException("PRI string region exceeds 64 KB");
        Pad(data, 8);
        var bInfo = new List<(int, int)>();
        foreach (var raw in blobs) { bInfo.Add(((int)data.Length, raw.Length)); data.Write(raw); Pad(data, 8); }

        var body = new MemoryStream();
        var tmp = new byte[12];
        W32(tmp, 0, u0); W16(tmp, 4, (ushort)ns); W16(tmp, 6, (ushort)nb); W32(tmp, 8, (uint)data.Length);
        body.Write(tmp);
        foreach (var (o, l) in sInfo) { var x = new byte[4]; W16(x, 0, (ushort)o); W16(x, 2, (ushort)l); body.Write(x); }
        foreach (var (o, l) in bInfo) { var x = new byte[8]; W32(x, 0, (uint)o); W32(x, 4, (uint)l); body.Write(x); }
        body.Write(data.ToArray());
        Pad(body, 8);
        uint totalLen = (uint)(Hdr + body.Length + Trailer);
        var outp = new MemoryStream();
        var hdr = sec.AsSpan(0, Hdr).ToArray();
        W32(hdr, 24, totalLen);  // section length (after id[16], qualifier u32, flags u16, sectionFlags u16)
        outp.Write(hdr);
        outp.Write(body.ToArray());
        var tr = new byte[8];
        W32(tr, 0, 0xDEF5FADE); W32(tr, 4, totalLen);
        outp.Write(tr);
        return outp.ToArray();
    }

    static bool Decode(byte[] raw, out string text, out Encoding enc)
    {
        text = ""; enc = Utf8Strict;
        if (raw.Length > 0 && raw[^1] == 0 && Array.IndexOf(raw, (byte)0, 0, raw.Length - 1) < 0)
        {
            try { text = Utf8Strict.GetString(raw, 0, raw.Length - 1); enc = Utf8Strict; return true; }
            catch (DecoderFallbackException) { }
        }
        if (raw.Length >= 2 && raw.Length % 2 == 0 && raw[^1] == 0 && raw[^2] == 0)
        {
            try
            {
                var t = Utf16Strict.GetString(raw, 0, raw.Length - 2);
                if (!t.Contains('\0')) { text = t; enc = Utf16Strict; return true; }
            }
            catch (DecoderFallbackException) { }
        }
        return false;
    }

    static byte[] Encode(string text, Encoding enc)
    {
        var b = enc.GetBytes(text);
        int z = enc is UTF8Encoding ? 1 : 2;
        var r = new byte[b.Length + z];
        b.CopyTo(r, 0);
        return r;
    }

    static void Pad(MemoryStream s, int n) { while (s.Length % n != 0) s.WriteByte(0); }
    static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
    static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    static void W32(byte[] b, int o, uint v) => BitConverter.GetBytes(v).CopyTo(b, o);
    static void W16(byte[] b, int o, ushort v) => BitConverter.GetBytes(v).CopyTo(b, o);
}
