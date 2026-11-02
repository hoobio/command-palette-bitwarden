[CmdletBinding(SupportsShouldProcess)]
param([string]$Root = $PWD)

# The extension and companion harden their process DACL, so Get-Process .Path and Win32_Process.ExecutablePath come back empty; QueryFullProcessImageNameW needs only PROCESS_QUERY_LIMITED_INFORMATION.
Add-Type -Namespace DevExtension -Name Native -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);
[DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
public static string ImagePath(int pid)
{
  var process = OpenProcess(0x1000, false, pid);
  if (process == IntPtr.Zero) return null;
  try
  {
    var path = new System.Text.StringBuilder(1024);
    var size = path.Capacity;
    return QueryFullProcessImageNameW(process, 0, path, ref size) ? path.ToString() : null;
  }
  finally { CloseHandle(process); }
}
'@

Get-Process HoobiBitwardenCommandPaletteExtension, HoobiBitwardenCompanion -ErrorAction SilentlyContinue |
  Where-Object { [DevExtension.Native]::ImagePath($_.Id) -like "$Root*" } |
  Stop-Process -Force
