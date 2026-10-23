using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class ProtectedSecretTests
{
  [Theory]
  [InlineData("hunter2")]
  [InlineData("pässwörd ✓ 密码 🔑")]
  [InlineData("a")]
  [InlineData("12345678")]
  [InlineData("123456789")]
  [InlineData("1234567812345678")]
  [InlineData("12345678123456789")]
  public void Reveal_RoundTripsPlaintext(string plaintext)
  {
    using var secret = ProtectedSecret.From(plaintext)!;
    Assert.Equal(plaintext, secret.Reveal());
    Assert.Equal(plaintext.Length, secret.Length);
  }

  [Fact]
  public void Reveal_RoundTripsLongSecretOverStackLimit()
  {
    var plaintext = new string('x', 5000) + "end";
    using var secret = ProtectedSecret.From(plaintext)!;
    Assert.Equal(plaintext, secret.Reveal());
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  public void From_EmptyInput_ReturnsNull(string? plaintext)
  {
    Assert.Null(ProtectedSecret.From(plaintext));
  }

  [Fact]
  public void Use_PassesPlaintextSpan()
  {
    using var secret = ProtectedSecret.From("Correct Horse")!;
    Assert.True(secret.Use(static s => s.Contains("horse", StringComparison.OrdinalIgnoreCase)));
    Assert.False(secret.Use(static s => s.Contains("battery", StringComparison.OrdinalIgnoreCase)));
  }

  [Fact]
  public void Use_DoesNotAllocate()
  {
    using var secret = ProtectedSecret.From("no strings attached")!;
    SecretFunc<int> length = static s => s.Length;
    secret.Use(length);

    var before = GC.GetAllocatedBytesForCurrentThread();
    var result = secret.Use(length);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.Equal(19, result);
    Assert.Equal(0, allocated);
  }

  [Fact]
  public void Dispose_BlocksFurtherAccess()
  {
    var secret = ProtectedSecret.From("gone")!;
    secret.Dispose();
    secret.Dispose();
    Assert.Throws<ObjectDisposedException>(secret.Reveal);
    Assert.Throws<ObjectDisposedException>(() => secret.Use(static s => s.Length));
  }
}
