using System.Runtime.InteropServices;
using XpmTranslator.Core;

namespace XpmTranslator;

static class Program
{
    static readonly string Usage = @"Xiaomi PC Manager English Translator

  XpmTranslator.exe                         open the window
  XpmTranslator.exe status                  show what is applied
  XpmTranslator.exe apply [all|id ...]      apply components (others are restored)
  XpmTranslator.exe restore                 put all original files back
  XpmTranslator.exe build <dir> [all|id ...] write patched files to <dir> only (for testing)

options:
  --app <dir>        Xiaomi PC Manager folder (default: detected)
  --originals <dir>  extra folder with original files (searched recursively one level)
  --store <dir>      backup store root (default: %ProgramData%\XpmTranslator)
  --no-restart       do not start PC Manager afterwards

component ids: " + string.Join(", ", Component.All.Select(c => c.Id));

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }
        AttachConsole(-1);
        Console.WriteLine();
        try { return Cli(args.ToList()); }
        catch (Exception e) { Console.WriteLine("Error: " + e.Message); return 2; }
    }

    static int Cli(List<string> a)
    {
        string? Opt(string name)
        {
            int i = a.IndexOf(name);
            if (i < 0 || i + 1 >= a.Count) return null;
            var v = a[i + 1];
            a.RemoveRange(i, 2);
            return v;
        }
        var appDir = Opt("--app");
        var storeRoot = Opt("--store");
        var extra = new List<string>();
        for (string? o; (o = Opt("--originals")) != null;) extra.Add(o);
        bool restart = !a.Remove("--no-restart");
        if (a.Count == 0 || a[0] is "-h" or "--help" or "/?") { Console.WriteLine(Usage); return 0; }

        var app = appDir != null ? Install.FromDir(appDir) : Install.Detect()
                  ?? throw new InvalidOperationException("Xiaomi PC Manager not found, use --app <dir>");
        var engine = new Engine(app, Console.WriteLine, storeRoot);
        engine.Originals.ExtraDirs.AddRange(extra);
        Console.WriteLine($"PC Manager {app.Version} at {app.Dir}" + (engine.Originals.IsKnownVersion ? "" : "  (UNKNOWN VERSION: no original hashes, results not guaranteed)"));

        ICollection<string> Ids(IEnumerable<string> xs)
        {
            var l = xs.ToList();
            return l.Count == 0 || l.Contains("all") ? Component.All.Select(c => c.Id).ToList() : l;
        }
        switch (a[0])
        {
            case "status":
                foreach (var c in Component.All) Console.WriteLine($"  {c.Id,-7} {engine.StatusOf(c),-11} {c.Title}");
                return 0;
            case "apply":
                return engine.Apply(Ids(a.Skip(1)), restart) ? 0 : 1;
            case "restore":
                engine.RestoreAll(restart);
                return 0;
            case "build":
                engine.BuildTo(a[1], Ids(a.Skip(2)));
                return 0;
            default:
                Console.WriteLine(Usage);
                return 1;
        }
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
}
