using System;
using System.Runtime.InteropServices;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Launches a child process that BREAKS AWAY from the extension's MSIX package identity. The companion
// is built unpackaged + self-contained; if it inherits the packaged extension's identity (the default
// for a child of a packaged app) the Windows App SDK self-contained runtime mis-initialises and WinUI
// crashes at startup (0xc000027b in Microsoft.UI.Xaml.dll). Setting the DESKTOP_APP_POLICY breakaway
// attribute makes the child run as a normal desktop app, exactly as if launched from Explorer.
internal static partial class DesktopAppLauncher
{
  private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
  // ProcThreadAttributeValue(ProcThreadAttributeDesktopAppPolicy=18, Thread=false, Input=true, Additive=false)
  // = 18 | PROC_THREAD_ATTRIBUTE_INPUT(0x20000) = 0x00020012.
  private const nuint PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY = 0x00020012;
  private const uint PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE = 0x00000001;

  public static (bool Ok, int Win32Error) TryLaunchDetached(string exePath, string commandLine, string workingDirectory, Action<string>? log = null)
  {
    nuint size = 0;
    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size); // query required size
    log?.Invoke($"attr-list size={size}");
    var attributeList = Marshal.AllocHGlobal((nint)size);
    try
    {
      if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        return (false, LogErr(log, "Initialize"));

      uint policy = PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE;
      if (!UpdateProcThreadAttribute(attributeList, 0, PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY, ref policy, sizeof(uint), IntPtr.Zero, IntPtr.Zero))
        return (false, LogErr(log, "Update"));

      var startupInfo = new STARTUPINFOEXW();
      startupInfo.StartupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFOEXW>();
      startupInfo.lpAttributeList = attributeList;

      // CreateProcessW may modify lpCommandLine in place, so pass a mutable, null-terminated buffer.
      var commandLineBuffer = (commandLine + '\0').ToCharArray();

      var created = CreateProcess(
        exePath,
        commandLineBuffer,
        IntPtr.Zero,
        IntPtr.Zero,
        false,
        EXTENDED_STARTUPINFO_PRESENT,
        IntPtr.Zero,
        workingDirectory,
        ref startupInfo,
        out var processInfo);

      if (created)
      {
        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
        return (true, 0);
      }
      return (false, LogErr(log, "CreateProcess"));
    }
    finally
    {
      DeleteProcThreadAttributeList(attributeList);
      Marshal.FreeHGlobal(attributeList);
    }
  }

  private static int LogErr(Action<string>? log, string step)
  {
    var err = Marshal.GetLastWin32Error();
    log?.Invoke($"{step} failed win32={err}");
    return err;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct STARTUPINFOW
  {
    public uint cb;
    public IntPtr lpReserved;
    public IntPtr lpDesktop;
    public IntPtr lpTitle;
    public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
    public ushort wShowWindow;
    public ushort cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput, hStdOutput, hStdError;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct STARTUPINFOEXW
  {
    public STARTUPINFOW StartupInfo;
    public IntPtr lpAttributeList;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct PROCESS_INFORMATION
  {
    public IntPtr hProcess;
    public IntPtr hThread;
    public uint dwProcessId;
    public uint dwThreadId;
  }

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, uint dwAttributeCount, uint dwFlags, ref nuint lpSize);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, nuint attribute, ref uint lpValue, nuint cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

  [LibraryImport("kernel32.dll")]
  private static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

  [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CreateProcess(
    string? lpApplicationName,
    char[] lpCommandLine,
    IntPtr lpProcessAttributes,
    IntPtr lpThreadAttributes,
    [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
    uint dwCreationFlags,
    IntPtr lpEnvironment,
    string? lpCurrentDirectory,
    ref STARTUPINFOEXW lpStartupInfo,
    out PROCESS_INFORMATION lpProcessInformation);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CloseHandle(IntPtr hObject);
}
