using System.Diagnostics;
using Microsoft.Win32;

namespace XpmTranslator.Core;

/// <summary>A Xiaomi PC Manager installation (the versioned folder, e.g. ...\XiaomiPCManager\5.3.2.407).</summary>
public sealed record Install(string Dir, string Version)
{
    public static readonly string[] UiProcesses = { "XiaomiPcManager", "PcClipboard", "XiaomiAISearch" };

    public string Path(string rel) => System.IO.Path.Combine(Dir, rel);

    public static Install? Detect()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\XiaomiPCManager");
            if (k?.GetValue("UninstallString") is string un)
            {
                var dir = System.IO.Path.GetDirectoryName(un.Trim('"'));
                if (dir != null && File.Exists(System.IO.Path.Combine(dir, "XiaomiPcManager.exe")))
                    return FromDir(dir);
            }
        }
        var root = @"C:\Program Files\MI\XiaomiPCManager";
        if (Directory.Exists(root))
            foreach (var d in Directory.GetDirectories(root).OrderByDescending(x => x))
                if (File.Exists(System.IO.Path.Combine(d, "XiaomiPcManager.exe")))
                    return FromDir(d);
        return null;
    }

    public static Install FromDir(string dir)
    {
        dir = System.IO.Path.GetFullPath(dir);
        var name = System.IO.Path.GetFileName(dir.TrimEnd('\\'));
        var ver = System.Version.TryParse(name, out _) ? name : FileVersionInfo.GetVersionInfo(System.IO.Path.Combine(dir, "XiaomiPcManager.dll")).FileVersion ?? "?";
        return new Install(dir, ver);
    }

    public void StopUi(Action<string> log)
    {
        foreach (var name in UiProcesses)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var exe = ImagePath(p.Id);
                    if (exe == null) { log($"  {name} (pid {p.Id}): cannot read its path, left running"); continue; }
                    if (!exe.StartsWith(Dir + "\\", StringComparison.OrdinalIgnoreCase)) continue; // another installation
                    log($"Closing {name} (pid {p.Id})");
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception e) { log($"  could not close {name}: {e.Message}"); }
            }
    }

    /// <summary>Full image path via PROCESS_QUERY_LIMITED_INFORMATION (also works for elevated processes).</summary>
    static string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int len = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref len) ? sb.ToString(0, len) : null;
        }
        finally { CloseHandle(h); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    public void StartUi()
    {
        var exe = Path("XiaomiPcManager.exe");
        if (File.Exists(exe))
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Dir });
    }
}
