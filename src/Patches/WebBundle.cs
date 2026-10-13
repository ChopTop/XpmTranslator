using System.Text;
using System.Text.RegularExpressions;

namespace XpmTranslator.Patches;

/// <summary>
/// Translates string/template literals in the minified React bundle (dist/static/js/main.js).
/// Chinese text is stored as \uXXXX escapes. Each escaped CJK run is expanded to its enclosing quotes
/// (avoids tokenizing regex literals); the literal is replaced when its decoded text is in the dictionary.
/// Logging calls, the weather city table and Lottie layer names are left alone.
/// </summary>
public static class WebBundle
{
    static readonly Regex Esc = new(@"\\u[4-9][0-9a-fA-F]{3}", RegexOptions.Compiled);
    static readonly Regex U = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);
    static readonly Regex Cjk = new("[一-鿿]", RegexOptions.Compiled);
    static readonly Regex LogCall = new(@"(?:^|[^\w$.])(?:V|console\.\w+)\($", RegexOptions.Compiled);
    static readonly Regex Lottie = new(@"(?:""nm""|\bnm|""mn""|\bmn|""cl""|""ln"")\s*:\s*$", RegexOptions.Compiled);
    static readonly string[] Loggers = { "V(", "console.log(", "console.error(", "console.warn(", "console.info(", "console.debug(" };

    public sealed record Literal(int Start, int End, char Quote, string Kind, string Text);

    public static List<Literal> Extract(string src)
    {
        var order = new List<(int S, int E)>();
        var spans = new Dictionary<(int S, int E), char>();
        foreach (Match m in Esc.Matches(src))
        {
            int p = m.Index;
            bool inside = false;
            foreach (var (s, e) in order.Skip(Math.Max(0, order.Count - 3)))
                if (s <= p && p < e) { inside = true; break; }
            if (inside) continue;
            var (l, q) = FindLeft(src, p);
            if (l < 0) continue;
            int r = FindRight(src, l + 1, q);
            if (r < 0 || r < p) continue;
            var key = (l + 1, r);
            if (!spans.ContainsKey(key)) order.Add(key);
            spans[key] = q;
        }

        int cityStart = src.IndexOf("JSON.parse('{\"d\":[", StringComparison.Ordinal);
        int cityEnd = cityStart >= 0 ? src.IndexOf("')", cityStart, StringComparison.Ordinal) : -1;
        var res = new List<Literal>();
        foreach (var ((s, e), q) in spans.OrderBy(x => x.Key.S).ThenBy(x => x.Key.E))
        {
            var raw = src[s..e];
            var text = U.Replace(raw, mm => ((char)Convert.ToInt32(mm.Groups[1].Value, 16)).ToString());
            if (!Cjk.IsMatch(text)) continue;
            int from = Math.Max(0, s - 60);
            var pre = s - 1 > from ? src[from..(s - 1)] : "";
            var kind = Loggers.Any(x => pre.TrimEnd().EndsWith(x, StringComparison.Ordinal)) || LogCall.IsMatch(pre) ? "log" : "ui";
            var ctx = pre.Length > 40 ? pre[^40..] : pre;
            if (cityStart >= 0 && cityStart <= s && s <= cityEnd) kind = "city";
            else if (Lottie.IsMatch(ctx)) kind = "lottie";
            res.Add(new Literal(s, e, q, kind, text));
        }
        return res;
    }

    /// <summary>Returns the translated bundle; counts translated and untranslated UI literals.</summary>
    public static string Translate(string src, IReadOnlyDictionary<string, string> map, out int replaced, out List<string> missing)
    {
        var sb = new StringBuilder(src.Length);
        int last = 0;
        replaced = 0;
        missing = new List<string>();
        foreach (var lit in Extract(src))
        {
            if (lit.Kind != "ui" || lit.Start < last) continue;
            if (!map.TryGetValue(lit.Text, out var v))
            {
                missing.Add(lit.Text);
                continue;
            }
            sb.Append(src, last, lit.Start - last);
            sb.Append(Encode(v, lit.Quote));
            last = lit.End;
            replaced++;
        }
        sb.Append(src, last, src.Length - last);
        return sb.ToString();
    }

    // where the PC Cleanup list renders a group name and an item name (group-name span; span after the item icon)
    static readonly Regex[] NameSlots =
    {
        new(@"(\[""group-name""\],children:)([\w$]+)\}", RegexOptions.Compiled),
        new(@"(\(""img"",\{src:[\w$]+\.thumbnail,alt:""""\}\),Object\([\w$]+\.jsx\)\(""span"",\{children:)([\w$]+\.name)\}", RegexOptions.Compiled),
    };

    /// <summary>
    /// PC Cleanup group/item names are not in the bundle: the native cleaner sends them at run time (they live in the
    /// encrypted Plugins\data\rubbish.dat). A lookup function is prepended and the render slots are wrapped with it.
    /// </summary>
    public static string TranslateRuntimeNames(string src, IReadOnlyDictionary<string, string> map, out int slots)
    {
        slots = 0;
        foreach (var re in NameSlots)
        {
            int n = 0;
            src = re.Replace(src, m => { n++; return m.Groups[1].Value + "__xpmName(" + m.Groups[2].Value + ")}"; });
            slots += n;
        }
        if (slots == 0) return src;
        var sb = new StringBuilder("var __xpmName=function(m){return function(s){return typeof s===\"string\"&&Object.prototype.hasOwnProperty.call(m,s)?m[s]:s}}({");
        foreach (var (k, v) in map) sb.Append(Quote(k)).Append(':').Append(Quote(v)).Append(',');
        if (map.Count > 0) sb.Length--;
        return sb.Append("});\n").Append(src).ToString();
    }

    /// <summary>Plain text as a double-quoted JS string, non-ASCII \u-escaped like the rest of the bundle.</summary>
    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 8).Append('"');
        foreach (var ch in s)
        {
            if (ch is '"' or '\\') sb.Append('\\').Append(ch);
            else if (ch < 0x20 || ch > 0x7e) sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Dictionary values are JS source fragments: keep their escapes, escape the quote, \u-escape non-ASCII.</summary>
    static string Encode(string v, char q)
    {
        var sb = new StringBuilder(v.Length + 8);
        for (int k = 0; k < v.Length; k++)
        {
            char ch = v[k];
            if (ch == '\\' && k + 1 < v.Length) { sb.Append(v, k, 2); k++; continue; }
            if (ch == '\\') { sb.Append(ch); continue; }
            if (ch == q) sb.Append('\\').Append(ch);
            else if (ch > 0x7e) sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    static bool Escaped(string src, int p)
    {
        int k = p - 1, n = 0;
        while (k >= 0 && src[k] == '\\') { n++; k--; }
        return n % 2 == 1;
    }

    static bool IsQuote(char c) => c == '"' || c == '\'' || c == '`';

    static (int, char) FindLeft(string src, int p)
    {
        int depth = 0;
        for (int k = p - 1; k >= 0; k--)
        {
            char ch = src[k];
            if (ch == '}') depth++;
            else if (ch == '{' && depth > 0) depth--;
            else if (IsQuote(ch) && !Escaped(src, k))
            {
                if (depth == 0) return (k, ch);
                char q = ch; // quote inside a template expression: skip the inner string
                k--;
                while (k >= 0 && !(src[k] == q && !Escaped(src, k))) k--;
            }
            else if (ch == '\n') return (-1, '\0');
        }
        return (-1, '\0');
    }

    static int FindRight(string src, int p, char q)
    {
        int k = p, depth = 0, n = src.Length;
        while (k < n)
        {
            char ch = src[k];
            if (ch == '\\') { k += 2; continue; }
            if (q == '`' && ch == '$' && k + 1 < n && src[k + 1] == '{') { depth++; k += 2; continue; }
            if (depth > 0)
            {
                if (ch == '}') depth--;
                else if (ch == '"' || ch == '\'')
                {
                    k++;
                    while (k < n && !(src[k] == ch && !Escaped(src, k))) k++;
                }
                k++;
                continue;
            }
            if (ch == q) return k;
            if (ch == '\n' && q != '`') return -1;
            k++;
        }
        return -1;
    }
}
