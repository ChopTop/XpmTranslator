using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XpmTranslator.Patches;

/// <summary>
/// WinUI (Microsoft.ui.xaml.dll) loads its own UI strings from &lt;lang&gt;\Microsoft.ui.xaml.dll.mui.
/// Xiaomi ships only zh-CN, so on non-Chinese Windows ContentDialogs (e.g. the update window) fail with
/// ERROR_MUI_FILE_NOT_LOADED (0x80073B01) and the app crashes. The fix supplies the genuine en-US MUI
/// from the installed Microsoft.WindowsAppRuntime framework, after checking that its MUI checksums
/// match the app's Microsoft.ui.xaml.dll.
/// </summary>
public static class Mui
{
    public const string Rel = @"en-US\Microsoft.ui.xaml.dll.mui";

    public sealed record Config(string ServiceChecksum, string Checksum);

    public static Config? ReadConfig(string path)
    {
        var h = LoadLibraryExW(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (h == IntPtr.Zero) return null;
        try
        {
            var r = FindResourceW(h, (IntPtr)1, "MUI");
            if (r == IntPtr.Zero) return null;
            int size = SizeofResource(h, r);
            var p = LockResource(LoadResource(h, r));
            if (p == IntPtr.Zero || size < 0x3C) return null;
            var b = new byte[size];
            Marshal.Copy(p, b, 0, size);
            if (BitConverter.ToUInt32(b, 0) != 0xFECDFECD) return null;
            return new Config(Convert.ToHexString(b, 0x1C, 16), Convert.ToHexString(b, 0x2C, 16));
        }
        finally { FreeLibrary(h); }
    }

    /// <summary>Path of a matching en-US MUI from an installed Windows App Runtime, or null.</summary>
    public static string? FindMatching(string appXamlDll, Action<string> log)
    {
        var want = ReadConfig(appXamlDll);
        if (want == null) { log("  cannot read MUI config of " + appXamlDll); return null; }
        foreach (var dir in RuntimeDirs(log))
        {
            var cand = Path.Combine(dir, Rel);
            if (!File.Exists(cand)) continue;
            var c = ReadConfig(cand);
            if (c == want) return cand;
        }
        return null;
    }

    public static bool Matches(string appXamlDll, byte[] mui)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"xpm_{Guid.NewGuid():N}.mui");
        try
        {
            File.WriteAllBytes(tmp, mui);
            var a = ReadConfig(appXamlDll);
            return a != null && a == ReadConfig(tmp);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    static IEnumerable<string> RuntimeDirs(Action<string> log)
    {
        // Get-AppxPackage is the supported way to read package locations (WindowsApps is not listable).
        // Packages of the current user first; -AllUsers needs elevation and covers other accounts.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in new[] { "", "-AllUsers " })
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Get-AppxPackage {scope}-Name 'Microsoft.WindowsAppRuntime.*' -ErrorAction SilentlyContinue | " +
                "Where-Object Architecture -eq 'X64' | Sort-Object Version -Descending | ForEach-Object InstallLocation\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            string output = "";
            try
            {
                using var p = Process.Start(psi)!;
                output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
            }
            catch (Exception e) { log("  Get-AppxPackage failed: " + e.Message); }
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Directory.Exists(line) && seen.Add(line)) yield return line;
        }
    }

    const uint LOAD_LIBRARY_AS_DATAFILE = 0x2, LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryExW(string f, IntPtr h, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr FindResourceW(IntPtr h, IntPtr name, string type);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LoadResource(IntPtr h, IntPtr r);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LockResource(IntPtr g);
    [DllImport("kernel32.dll", SetLastError = true)] static extern int SizeofResource(IntPtr h, IntPtr r);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeLibrary(IntPtr h);
}
