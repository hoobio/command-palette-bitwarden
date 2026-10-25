namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class AutoBiometricGateTests
{
  [Fact]
  public void IsPaletteOpen_IsFalse_WhenNoCommandPaletteWindowHasTheForeground() =>
    Assert.False(HoobiBitwardenCommandPaletteExtensionPage.IsPaletteOpen(() => 0));

  [Fact]
  public void IsPaletteOpen_IsTrue_WhenACommandPaletteWindowHasTheForeground() =>
    Assert.True(HoobiBitwardenCommandPaletteExtensionPage.IsPaletteOpen(() => 1234));
}
