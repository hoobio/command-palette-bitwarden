using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class AutoBiometricGateTests
{
  [Fact]
  public void IsPaletteOpen_IsFalse_WhenNoCommandPaletteWindowHasTheForeground() =>
    Assert.False(HoobiBitwardenCommandPaletteExtensionPage.IsPaletteOpen(() => 0));

  [Fact]
  public void IsPaletteOpen_IsTrue_WhenACommandPaletteWindowHasTheForeground() =>
    Assert.True(HoobiBitwardenCommandPaletteExtensionPage.IsPaletteOpen(() => 1234));

  [Fact]
  public void TryConsume_IsFalse_UntilAPlaceholderReadArmsTheGate()
  {
    var gate = new AutoBiometricGate();
    Assert.False(gate.TryConsume());
    gate.Arm();
    Assert.True(gate.TryConsume());
  }

  [Fact]
  public void TryConsume_SpendsTheArm_SoALaterGetItemsWithoutANavigationGetsNothing()
  {
    var gate = new AutoBiometricGate();
    gate.Arm();
    gate.Arm();
    Assert.True(gate.TryConsume());
    Assert.False(gate.TryConsume());
  }

  [Fact]
  public void Veto_IsNull_ForAPageOpenByTheUser_WithThePaletteInFront() =>
    Assert.Null(AutoBiometricGate.Veto(openedByUser: true, alreadyTriggered: false, clickFailed: false, handlingAction: false, paletteOpen: true, biometricEnabled: true, rememberSession: false));

  [Fact]
  public void Veto_AllowsARememberedSessionRestore_WithoutBiometrics() =>
    Assert.Null(AutoBiometricGate.Veto(openedByUser: true, alreadyTriggered: false, clickFailed: false, handlingAction: false, paletteOpen: true, biometricEnabled: false, rememberSession: true));

  [Theory]
  [InlineData(false, false, false, false, true, true, "not a page open by the user")]
  [InlineData(true, true, false, false, true, true, "already prompted for this lock")]
  [InlineData(true, false, true, false, true, true, "an earlier Windows Hello attempt failed")]
  [InlineData(true, false, false, true, true, true, "an action is running")]
  [InlineData(true, false, false, false, false, true, "Command Palette is not the foreground window")]
  [InlineData(true, false, false, false, true, false, "auto unlock is off")]
  public void Veto_NamesTheFirstFailingGate(bool openedByUser, bool alreadyTriggered, bool clickFailed, bool handlingAction, bool paletteOpen, bool biometricEnabled, string expected) =>
    Assert.Equal(expected, AutoBiometricGate.Veto(openedByUser, alreadyTriggered, clickFailed, handlingAction, paletteOpen, biometricEnabled, rememberSession: false));

  [Fact]
  public void Veto_RefusesAStatusChangeRebuild_EvenWithThePaletteInFront() =>
    Assert.Equal("not a page open by the user", AutoBiometricGate.Veto(openedByUser: false, alreadyTriggered: false, clickFailed: false, handlingAction: false, paletteOpen: true, biometricEnabled: true, rememberSession: true));
}
