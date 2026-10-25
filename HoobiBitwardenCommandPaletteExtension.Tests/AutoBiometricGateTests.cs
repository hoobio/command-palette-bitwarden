namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class AutoBiometricGateTests
{
  [Fact]
  public void IsPaletteOpen_IsFalse_WhenNoCommandPaletteWindowHasTheForeground() =>
    Assert.False(HoobiBitwardenCommandPaletteExtensionPage.IsPaletteOpen(() => 0));

  [Fact]
  public void IsPaletteOpen_IsTrue_WhenACommandPaletteWindowHasTheForeground() =>
    Assert.True(HoobiBitwardenCommandPaletteExtensionPage.IsPaletteOpen(() => 1234));

  [Theory]
  [InlineData(0, 5_000, false)]
  [InlineData(1_000, 1_500, true)]
  [InlineData(1_000, 11_000, true)]
  [InlineData(1_000, 11_001, false)]
  public void WasShownRecently_NeedsAPlaceholderReadWithinTheWindow(long shownTicks, long nowTicks, bool expected) =>
    Assert.Equal(expected, HoobiBitwardenCommandPaletteExtensionPage.WasShownRecently(shownTicks, nowTicks));
}
