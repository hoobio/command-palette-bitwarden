using System;
using System.Linq;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class PasswordGeneratorTests
{
    [Fact]
    public void Password_HonorsLengthAndClasses()
    {
        var opts = GeneratorOptionsProvider.DefaultPassword() with { Length = 24 };
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(opts);
            Assert.Equal(24, pw.Length);
            Assert.Contains(pw, char.IsUpper);
            Assert.Contains(pw, char.IsLower);
            Assert.Contains(pw, char.IsDigit);
            Assert.Contains(pw, c => "!@#$%^&*".Contains(c, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Password_AvoidAmbiguous_ExcludesAmbiguousChars()
    {
        var opts = GeneratorOptionsProvider.DefaultPassword() with { Length = 64, AvoidAmbiguous = true };
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(opts);
            Assert.DoesNotContain(pw, c => "Il1O0".Contains(c, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Password_HonorsMinNumberAndMinSpecial()
    {
        var opts = GeneratorOptionsProvider.DefaultPassword() with { Length = 20, MinNumber = 3, MinSpecial = 4 };
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(opts);
            Assert.True(pw.Count(char.IsDigit) >= 3);
            Assert.True(pw.Count(c => "!@#$%^&*".Contains(c, StringComparison.Ordinal)) >= 4);
        }
    }

    [Fact]
    public void Password_OnlyLowercase_WhenOthersDisabled()
    {
        var opts = GeneratorOptionsProvider.DefaultPassword() with
        {
            Uppercase = false,
            Numbers = false,
            Symbols = false,
            Length = 16,
        };
        var pw = PasswordGenerator.Generate(opts);
        Assert.Equal(16, pw.Length);
        Assert.All(pw, c => Assert.True(char.IsLower(c)));
    }

    [Fact]
    public void Passphrase_HasRequestedWordCountAndNumber()
    {
        var opts = GeneratorOptionsProvider.DefaultPassphrase() with { Words = 5, Separator = "-", Capitalize = true, IncludeNumber = true };
        var phrase = PasswordGenerator.Generate(opts);
        var parts = phrase.Split('-');
        Assert.Equal(5, parts.Length);
        Assert.Contains(phrase, char.IsDigit);              // include-number added a digit
        Assert.Contains(phrase, char.IsUpper);              // capitalized
    }

    [Fact]
    public void Passphrase_CustomSeparator()
    {
        var opts = GeneratorOptionsProvider.DefaultPassphrase() with { Words = 4, Separator = ".", IncludeNumber = false, Capitalize = false };
        var phrase = PasswordGenerator.Generate(opts);
        Assert.Equal(4, phrase.Split('.').Length);
    }
}
