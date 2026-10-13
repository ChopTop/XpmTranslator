using System.Text;
using XpmTranslator.Patches;

namespace XpmTranslator.Core;

/// <summary>Everything a component needs while building its patched files.</summary>
public sealed class BuildContext
{
    public required Install App { get; init; }
    public required Originals Originals { get; init; }
    public required Action<string> Log { get; init; }

    public byte[] Original(string rel)
    {
        var b = Originals.Get(rel, out var src);
        if (b == null)
            throw new InvalidOperationException(
                $"no original copy of {rel} (the file was modified and no backup/.bak with the original 5.3.2.407 content was found)");
        if (src != "app") Log($"  original {rel} taken from {src}");
        return b;
    }
}

public abstract class Component
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }
    public virtual bool Fix => false;

    /// <summary>App-relative paths this component may write (used for status and restore).</summary>
    public abstract IEnumerable<string> Files(Install app);

    /// <summary>Files that never exist in a stock install: restore deletes them even if an older copy was present.</summary>
    public virtual IEnumerable<string> OwnFiles => Array.Empty<string>();

    /// <summary>Patched content per app-relative path.</summary>
    public abstract Dictionary<string, byte[]> Build(BuildContext ctx);

    public static IReadOnlyList<Component> All { get; } = new Component[]
    {
        new WebTranslation(), new WebLayout(), new PriTranslation(), new AssemblyTexts(),
        new OsdImages(), new ChargerPopup(), new PrivacyText(), new MuiFix(), new UpdateNotesFix(),
    };
}

sealed class WebTranslation : Component
{
    const string Js = @"dist\static\js\main.js";
    public override string Id => "web";
    public override string Title => "Main window";
    public override string Description => "All texts of the main window (web UI): devices, toolbox, battery & performance, settings, support.";
    public override IEnumerable<string> Files(Install app) => new[] { Js };
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var src = Encoding.UTF8.GetString(ctx.Original(Js));
        var res = WebBundle.Translate(src, Resources.Map("web.json"), out var n, out var missing);
        ctx.Log($"  main window: {n} texts translated" + (missing.Count > 0 ? $", {missing.Count} not in dictionary" : ""));
        res = WebBundle.TranslateRuntimeNames(res, Resources.Map("cleanup.json"), out var slots);
        ctx.Log(slots == 2 ? "  PC Cleanup item names: hooked" : $"  PC Cleanup item names: {slots} of 2 places found, names may stay Chinese");
        return new() { [Js] = new UTF8Encoding(false).GetBytes(res) };
    }
}

sealed class WebLayout : Component
{
    const string Html = @"dist\index.html", Css = @"dist\static\css\custom.css";
    const string Link = "<link href=\"./static/css/main.css\" rel=\"stylesheet\">";
    public override string Id => "layout";
    public override string Title => "Main window layout fixes";
    public override string Description => "Extra stylesheet so longer English texts fit: cards, sidebar, header, drop-downs, buttons.";
    public override bool Fix => true;
    public override IEnumerable<string> Files(Install app) => new[] { Html, Css };
    public override IEnumerable<string> OwnFiles => new[] { Css };
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var b = ctx.Original(Html);
        bool bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
        var html = Encoding.UTF8.GetString(b, bom ? 3 : 0, b.Length - (bom ? 3 : 0));
        if (!html.Contains(Link)) throw new InvalidOperationException("index.html: main.css link not found");
        html = html.Replace(Link, Link + "<link href=\"./static/css/custom.css\" rel=\"stylesheet\">");
        var outb = (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(html)).ToArray();
        return new() { [Html] = outb, [Css] = Resources.Bytes("custom.css") };
    }
}

sealed class PriTranslation : Component
{
    const string Pri = "resources.pri";
    public override string Id => "pri";
    public override string Title => "Control Center, tray and dialogs";
    public override string Description => "WinUI resources: tray Control Center, Mi File Transfer, pop-ups, update/exit dialogs, subtitles window.";
    public override IEnumerable<string> Files(Install app) => new[] { Pri };
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var res = PriFile.Patch(ctx.Original(Pri), Resources.Map("pri.json"), out var n, out var miss);
        ctx.Log($"  resources.pri: {n} strings translated" + (miss.Count > 0 ? $", {miss.Count} dictionary entries unused" : ""));
        return new() { [Pri] = res };
    }
}

sealed class AssemblyTexts : Component
{
    static readonly (string Rel, string Map)[] Targets =
    {
        ("PcControlCenter.dll", "asm_PcControlCenter.json"),
        ("SubtitleTranscriptor.dll", "asm_SubtitleTranscriptor.json"),
    };
    public override string Id => "asm";
    public override string Title => "Texts built into the program code";
    public override string Description => "File transfer notifications, earphone hint, subtitle window defaults (PcControlCenter.dll, SubtitleTranscriptor.dll).";
    public override IEnumerable<string> Files(Install app) => Targets.Select(t => t.Rel);
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var r = new Dictionary<string, byte[]>();
        foreach (var (rel, map) in Targets)
        {
            r[rel] = AssemblyPatch.Patch(ctx.Original(rel), Resources.Map(map), ctx.App.Dir, out var n, out var miss);
            ctx.Log($"  {rel}: {n} literals replaced" + (miss.Count > 0 ? $", not found: {miss.Count}" : ""));
        }
        return r;
    }
}

sealed class PrivacyText : Component
{
    static readonly string[] Names = { "privacy.txt", "cancel_privacy.txt" };
    const string Dir = @"Assets\privacy_content\";
    public override string Id => "privacy";
    public override string Title => "Privacy policy text";
    public override string Description => "Unofficial English translation of the privacy policy shown on first start and of the \"withdraw consent\" notice.";
    public override IEnumerable<string> Files(Install app) => Names.Select(n => Dir + n);
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var r = new Dictionary<string, byte[]>();
        foreach (var name in Names)
        {
            var orig = ctx.Original(Dir + name);
            // keep the original encoding: privacy.txt is UTF-8 without BOM, cancel_privacy.txt is UTF-16LE with BOM
            bool utf16 = orig.Length >= 2 && orig[0] == 0xFF && orig[1] == 0xFE;
            var text = Resources.Text("privacy/" + name).Replace("\r\n", "\n").Replace("\n", "\r\n");
            r[Dir + name] = utf16 ? new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()
                                  : new UTF8Encoding(false).GetBytes(text);
        }
        ctx.Log($"  {r.Count} files");
        return r;
    }
}

sealed class UpdateNotesFix : Component
{
    const string Dll = "XiaomiPcManager.dll";
    public override string Id => "otalog";
    public override string Title => "Fix: \"????\" in the update window";
    public override string Description => "The update notes are converted through the ANSI code page by Xiaomi's native code, so Chinese text turns into '?'. Unreadable notes are replaced with \"Bug fixes and improvements.\"";
    public override bool Fix => true;
    public override IEnumerable<string> Files(Install app) => new[] { Dll };
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var res = AssemblyPatch.Patch(ctx.Original(Dll), Resources.Map("asm_XiaomiPcManager.json"), ctx.App.Dir, out var n, out _);
        ctx.Log($"  {Dll}: {n} change(s)");
        return new() { [Dll] = res };
    }
}

sealed class OsdImages : Component
{
    public override string Id => "osd";
    public override string Title => "Fn-key pop-ups (OSD)";
    public override string Description => "Captions on the on-screen pop-ups: Mic on/off, performance modes, battery saver, keyboard light Auto.";
    public override IEnumerable<string> Files(Install app) => Resources.Folder("images/res").Select(x => @"res\" + x.Rel);
    public override Dictionary<string, byte[]> Build(BuildContext ctx) => Images(ctx, "images/res", @"res\");

    internal static Dictionary<string, byte[]> Images(BuildContext ctx, string folder, string prefix)
    {
        var r = new Dictionary<string, byte[]>();
        foreach (var (rel, res) in Resources.Folder(folder))
        {
            ctx.Original(prefix + rel); // only replace images we know
            r[prefix + rel] = Resources.Bytes(res);
        }
        ctx.Log($"  {r.Count} images");
        return r;
    }
}

sealed class ChargerPopup : Component
{
    const string Xml = @"skin\xml\LowPowerAlertWnd.xml";
    public override string Id => "skin";
    public override string Title => "Non-original charger warning";
    public override string Description => "Text of the charger warning window and its \"Don't remind me again\" checkbox.";
    public override IEnumerable<string> Files(Install app) =>
        new[] { Xml }.Concat(Resources.Folder("images/skin").Select(x => @"skin\" + x.Rel));
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var xml = Encoding.UTF8.GetString(ctx.Original(Xml));
        foreach (var (k, v) in Resources.Map("skin.json"))
        {
            if (!xml.Contains(k)) throw new InvalidOperationException("skin XML: text not found: " + k);
            xml = xml.Replace(k, v);
        }
        var r = OsdImages.Images(ctx, "images/skin", @"skin\");
        r[Xml] = new UTF8Encoding(false).GetBytes(xml);
        return r;
    }
}

sealed class MuiFix : Component
{
    public override string Id => "mui";
    public override string Title => "Fix: crash on dialogs (update window etc.)";
    public override string Description => "Adds the genuine English WinUI language file from Microsoft's Windows App Runtime. Without it every WinUI dialog crashes the app on non-Chinese Windows.";
    public override bool Fix => true;
    public override IEnumerable<string> Files(Install app) => new[] { Mui.Rel };
    public override Dictionary<string, byte[]> Build(BuildContext ctx)
    {
        var src = Mui.FindMatching(ctx.App.Path("Microsoft.ui.xaml.dll"), ctx.Log)
                  ?? throw new InvalidOperationException("no matching Microsoft.WindowsAppRuntime en-US MUI found (install Windows App Runtime 1.4)");
        ctx.Log("  using " + src);
        return new() { [Mui.Rel] = File.ReadAllBytes(src) };
    }
}
