namespace XpmTranslator.Core;

public enum State { NotApplied, Applied, Partial, Modified }

public sealed class Engine
{
    public Install App { get; }
    public Store Store { get; }
    public Originals Originals { get; }
    readonly Action<string> _log;

    public Engine(Install app, Action<string> log, string? storeRoot = null)
    {
        App = app;
        _log = log;
        Store = new Store(app.Version, storeRoot);
        Originals = new Originals(app, Store);
        // a "backup" folder next to the tool (older script-based package) is searched for originals too
        foreach (var near in new[] { Path.Combine(AppContext.BaseDirectory, "backup"), Path.Combine(AppContext.BaseDirectory, "..", "backup") })
            if (Directory.Exists(near)) Originals.ExtraDirs.Add(Path.GetFullPath(near));
    }

    public State StatusOf(Component c)
    {
        int applied = 0, original = 0, total = 0;
        foreach (var rel in c.Files(App))
        {
            total++;
            var p = App.Path(rel);
            var cur = File.Exists(p) ? File.ReadAllBytes(p) : null;
            bool ours = Store.Files.TryGetValue(rel, out var e);
            if (ours && cur != null && Store.Sha(cur) == e!.PatchedSha) applied++;
            else if (Originals.KnownSha(rel) != null) { if (cur != null && Originals.IsOriginal(rel, cur)) original++; }
            else if (!ours) original++;   // file we add (or unknown version): whatever is there is not ours
        }
        if (applied == total) return State.Applied;
        if (original == total) return State.NotApplied;
        return applied > 0 ? State.Partial : State.Modified;
    }

    /// <summary>Builds the selected components and writes them; components not selected are restored.</summary>
    public bool Apply(ICollection<string> selected, bool restartApp = true)
    {
        var outputs = new Dictionary<string, (string Comp, byte[] Data)>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        var ctx = new BuildContext { App = App, Originals = Originals, Log = _log };
        foreach (var c in Component.All.Where(c => selected.Contains(c.Id)))
        {
            _log($"Building: {c.Title}");
            try
            {
                foreach (var (rel, data) in c.Build(ctx)) outputs[rel] = (c.Id, data);
            }
            catch (Exception e)
            {
                failed.Add(c.Title);
                _log($"  FAILED: {e.Message}");
            }
        }

        var toRestore = Component.All.Where(c => !selected.Contains(c.Id))
            .SelectMany(c => c.Files(App)).Where(rel => Store.Files.ContainsKey(rel) && !outputs.ContainsKey(rel)).ToList();
        if (outputs.Count == 0 && toRestore.Count == 0)
        {
            _log("Nothing to do.");
            return failed.Count == 0;
        }

        App.StopUi(_log);
        int written = 0, errors = 0;
        try
        {
            foreach (var (rel, (comp, data)) in outputs)
            {
                try
                {
                    Write(rel, comp, data);
                    written++;
                }
                catch (Exception e) { errors++; _log($"  cannot write {rel}: {e.Message}"); }
            }
            foreach (var rel in toRestore)
            {
                try { RestoreFile(rel); }
                catch (Exception e) { errors++; _log($"  cannot restore {rel}: {e.Message}"); }
            }
        }
        finally { Store.Save(); }

        _log($"Done: {written} files written, {toRestore.Count} restored" + (errors > 0 ? $", {errors} errors (file in use? close the app or reboot and retry)" : "") + ".");
        if (failed.Count > 0) _log("Skipped: " + string.Join(", ", failed));
        if (restartApp) App.StartUi();
        return failed.Count == 0 && errors == 0;
    }

    public void RestoreAll(bool restartApp = true)
    {
        App.StopUi(_log);
        int n = 0;
        try
        {
            foreach (var rel in Store.Files.Keys.ToList())
            {
                try { RestoreFile(rel); n++; }
                catch (Exception e) { _log($"  cannot restore {rel}: {e.Message}"); }
            }
        }
        finally { Store.Save(); }
        _log($"Restored {n} files.");
        if (restartApp) App.StartUi();
    }

    void Write(string rel, string comp, byte[] data)
    {
        var path = App.Path(rel);
        if (!Store.Files.TryGetValue(rel, out var entry))
        {
            entry = new Store.Entry { Component = comp };
            bool own = Component.All.Any(c => c.OwnFiles.Contains(rel, StringComparer.OrdinalIgnoreCase));
            byte[]? original = own ? null
                : Originals.KnownSha(rel) != null ? Originals.Get(rel, out _)
                : File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (original != null)
            {
                Store.SaveOriginal(rel, original);
                entry.Existed = true;
                entry.OriginalSha = Store.Sha(original);
            }
            Store.Files[rel] = entry;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        entry.Component = comp;
        entry.PatchedSha = Store.Sha(data);
    }

    void RestoreFile(string rel)
    {
        var e = Store.Files[rel];
        var path = App.Path(rel);
        if (e.Existed)
        {
            var b = Store.Original(rel) ?? throw new InvalidOperationException("backup missing for " + rel);
            File.WriteAllBytes(path, b);
        }
        else if (File.Exists(path)) File.Delete(path);
        Store.Files.Remove(rel);
    }

    /// <summary>Test helper: build the selected components into a folder without touching the app.</summary>
    public void BuildTo(string outDir, ICollection<string> selected)
    {
        var ctx = new BuildContext { App = App, Originals = Originals, Log = _log };
        foreach (var c in Component.All.Where(c => selected.Contains(c.Id)))
        {
            _log($"Building: {c.Title}");
            try
            {
                foreach (var (rel, data) in c.Build(ctx))
                {
                    var p = Path.Combine(outDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                    File.WriteAllBytes(p, data);
                }
            }
            catch (Exception e) { _log($"  FAILED: {e.Message}"); }
        }
    }
}
