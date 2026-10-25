using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Command Palette reads PlaceholderText only in ListViewModel.InitializeProperties, right before the GetItems of a user navigation (PowerToys v0.102); a Dock band refresh and a cached page re-fetch call GetItems alone.
internal sealed class AutoBiometricGate
{
  private int _armed;

  internal void Arm() => Volatile.Write(ref _armed, 1);

  internal bool TryConsume() => Interlocked.Exchange(ref _armed, 0) == 1;

  internal static string? Veto(bool openedByUser, bool alreadyTriggered, bool clickFailed, bool handlingAction, bool paletteOpen, bool biometricEnabled, bool rememberSession) =>
    !openedByUser ? "not a page open by the user"
    : alreadyTriggered ? "already prompted for this lock"
    : clickFailed ? "an earlier Windows Hello attempt failed"
    : handlingAction ? "an action is running"
    : !paletteOpen ? "Command Palette is not the foreground window"
    : !biometricEnabled && !rememberSession ? "auto unlock is off"
    : null;
}
