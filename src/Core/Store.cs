using System.Security.Cryptography;
using System.Text.Json;

namespace XpmTranslator.Core;

/// <summary>
/// Backup store under %ProgramData%\XpmTranslator\&lt;version&gt;: the pristine copy of every file we replaced,
/// plus a manifest of what was written. Lives outside the app folder so app updates/uninstall don't wipe it.
/// </summary>
public sealed class Store
{
    public sealed class Entry
    {
        public string Component { get; set; } = "";
        public bool Existed { get; set; }            // false: the file was added by us, restore deletes it
        public string? OriginalSha { get; set; }
        public string PatchedSha { get; set; } = "";
    }

    public string Dir { get; }
    public Dictionary<string, Entry> Files { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    string ManifestPath => Path.Combine(Dir, "manifest.json");

    public Store(string version, string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XpmTranslator");
        Dir = Path.Combine(root, version);
        if (File.Exists(ManifestPath))
            Files = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(ManifestPath))
                    is { } d ? new(d, StringComparer.OrdinalIgnoreCase) : Files;
    }

    public string BackupPath(string rel) => Path.Combine(Dir, "original", rel);

    public byte[]? Original(string rel) => File.Exists(BackupPath(rel)) ? File.ReadAllBytes(BackupPath(rel)) : null;

    public void SaveOriginal(string rel, byte[] data)
    {
        var p = BackupPath(rel);
        if (File.Exists(p)) return; // never overwrite a pristine backup
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, data);
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(Files, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
