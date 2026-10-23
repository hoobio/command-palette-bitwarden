using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class SecureClipboardServiceTests
{
  [Fact]
  public void RetryOpen_SucceedsFirstTry_CallsOnce()
  {
    var calls = 0;
    Assert.True(SecureClipboardService.RetryOpen(() => { calls++; return true; }, 5, 0));
    Assert.Equal(1, calls);
  }

  [Fact]
  public void RetryOpen_SucceedsAfterFailures_Retries()
  {
    var calls = 0;
    Assert.True(SecureClipboardService.RetryOpen(() => ++calls == 3, 5, 0));
    Assert.Equal(3, calls);
  }

  [Fact]
  public void RetryOpen_AlwaysFails_StopsAtAttemptLimit()
  {
    var calls = 0;
    Assert.False(SecureClipboardService.RetryOpen(() => { calls++; return false; }, 4, 0));
    Assert.Equal(4, calls);
  }
}
