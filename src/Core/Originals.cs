using System.Text.Json;

namespace XpmTranslator.Core;

/// <summary>
/// Finds the pristine bytes of an app file. Known versions have a SHA-256 list of the original files;
/// a candidate is accepted only if it matches. Candidates, in order: our backup store, the app file itself,
/// "file.bak*" next to it (left by earlier manual translation attempts), extra folders given by the user.
/// </summary>
public sealed class Originals
{
    readonly Install _app;
    readonly Store _store;
    readonly Dictionary<string, string> _known;
    public List<string> ExtraDirs { get; } = new();

    public Originals(Install app, Store store)
    {
        _app = app;
        _store = store;
        var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(Resources.Text("originals.json"))!;
        _known = all.TryGetValue(app.Version, out var k) ? new(k, StringComparer.OrdinalIgnoreCase) : new();
    }

    public bool IsKnownVersion => _known.Count > 0;

    public bool IsOriginal(string rel, byte[] data) =>
        _known.TryGetValue(rel, out var h) && h == Store.Sha(data);

    public string? KnownSha(string rel) => _known.GetValueOrDefault(rel);

    /// <summary>Pristine bytes, or null when no verified copy exists. Unknown versions accept the current file.</summary>
    public byte[]? Get(string rel, out string? source)
    {
        foreach (var (path, label) in Candidates(rel))
        {
            if (!File.Exists(path)) continue;
            var b = File.ReadAllBytes(path);
            if (!IsKnownVersion || IsOriginal(rel, b))
            {
                source = label;
                return b;
            }
            if (!IsKnownVersion) break;
        }
        source = null;
        return null;
    }

    IEnumerable<(string Path, string Label)> Candidates(string rel)
    {
        yield return (_store.BackupPath(rel), "backup");
        var p = _app.Path(rel);
        yield return (p, "app");
        var dir = Path.GetDirectoryName(p)!;
        if (Directory.Exists(dir))
            foreach (var bak in Directory.GetFiles(dir, Path.GetFileName(p) + ".bak*"))
                yield return (bak, Path.GetFileName(bak));
        foreach (var extra in ExtraDirs)
        {
            yield return (Path.Combine(extra, rel), extra);
            if (Directory.Exists(extra))
                foreach (var sub in Directory.GetDirectories(extra))   // e.g. backup\20261013-112908\
                    yield return (Path.Combine(sub, rel), sub);
        }
    }
}
