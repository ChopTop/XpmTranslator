using System.Reflection;
using System.Text;
using System.Text.Json;

namespace XpmTranslator.Core;

/// <summary>Access to the data embedded in the executable (dictionaries, CSS, images, hashes).</summary>
public static class Resources
{
    static readonly Assembly Asm = typeof(Resources).Assembly;

    // MSBuild puts %(RecursiveDir) with backslashes into LogicalName; index by a '/'-normalized name
    static readonly Dictionary<string, string> Names =
        Asm.GetManifestResourceNames().ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.Ordinal);

    public static byte[] Bytes(string path)
    {
        using var s = (Names.TryGetValue("Data/" + path.Replace('\\', '/'), out var real) ? Asm.GetManifestResourceStream(real) : null)
                      ?? throw new FileNotFoundException("embedded resource missing: " + path);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static string Text(string path) => Encoding.UTF8.GetString(Bytes(path));

    public static Dictionary<string, string> Map(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Text(path))!;

    /// <summary>Embedded files below a folder, as (app-relative path with backslashes, resource path).</summary>
    public static IEnumerable<(string Rel, string Res)> Folder(string folder)
    {
        var prefix = "Data/" + folder.TrimEnd('/') + "/";
        foreach (var n in Names.Keys.Order(StringComparer.Ordinal))
            if (n.StartsWith(prefix, StringComparison.Ordinal))
                yield return (n[prefix.Length..].Replace('/', '\\'), n["Data/".Length..]);
    }
}
