# Loads Microsoft.ui.xaml.dll as a resource module with the given UI language (-Lang, default en-US) and asks for strings,
# to see whether Windows accepts the MUI satellite next to it.
param([string]$Dir, [string]$Lang = "en-US", [int[]]$Ids = @(5024..5039))
Add-Type -Namespace T -Name N -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr LoadLibraryExW(string f, IntPtr h, uint flags);
[DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern int LoadStringW(IntPtr h, uint id, System.Text.StringBuilder sb, int max);
[DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool SetThreadPreferredUILanguages(uint flags, string langs, out uint num);
'@
$n = 0
[void][T.N]::SetThreadPreferredUILanguages(8, "$Lang`0`0", [ref]$n)   # MUI_LANGUAGE_NAME
$h = [T.N]::LoadLibraryExW((Join-Path $Dir 'Microsoft.ui.xaml.dll'), [IntPtr]::Zero, 0x20 -bor 0x2)  # IMAGE_RESOURCE | DATAFILE
if ($h -eq [IntPtr]::Zero) { "LoadLibraryEx failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"; exit }
$ok = 0
foreach ($id in $Ids) {
    $sb = New-Object System.Text.StringBuilder 512
    $len = [T.N]::LoadStringW($h, $id, $sb, 512)
    $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    if ($len -gt 0) { $ok++; if ($ok -le 3) { "  id $id -> '$($sb.ToString())'" } }
    elseif ($id -eq $Ids[0]) { "  id $id -> FAILED, error $err (0x{0:X}): {1}" -f $err, (New-Object ComponentModel.Win32Exception $err).Message }
}
"loaded strings: $ok / $($Ids.Count)"
