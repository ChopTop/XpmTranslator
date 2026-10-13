# Start a GUI process, wait, and save a screenshot of its main window.
param([string]$Exe, [string]$ArgList, [string]$Out, [int]$Wait = 6)
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
public struct RECT { public int L, T, R, B; }
'@
$p = Start-Process $Exe -ArgumentList $ArgList -PassThru
for ($i = 0; $i -lt $Wait * 4 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
Start-Sleep -Seconds $Wait
$p.Refresh()
$r = New-Object W.U+RECT
[void][W.U]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][W.U]::PrintWindow($p.MainWindowHandle, $hdc, 2); $g.ReleaseHdc($hdc)
$bmp.Save($Out)
Stop-Process -Id $p.Id -Force
"saved $Out ($($bmp.Width)x$($bmp.Height))"
