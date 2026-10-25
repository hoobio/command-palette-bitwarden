using System.Linq;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class PasswordGeneratorTests
{
  private const int Iterations = 500;
  private const string Ambiguous = "lIO01";

  private static readonly GeneratorOptions[] PasswordOptions =
  [
    new(),
    new(Length: 5, Uppercase: false, Lowercase: false, Numbers: false, Special: true, MinSpecial: 0),
    new(Length: 32, Special: true, MinNumber: 2, MinSpecial: 3, AvoidAmbiguous: true),
    new(Length: 128, Special: true, MinNumber: 9, MinSpecial: 9),
    new(Length: 20, Special: true, MinNumber: 9, MinSpecial: 9),
    new(Length: 8, Uppercase: false, Lowercase: true, Numbers: true, MinNumber: 0, AvoidAmbiguous: true),
  ];

  private static readonly GeneratorOptions[] PassphraseOptions =
  [
    new(GeneratorKinds.Passphrase),
    new(GeneratorKinds.Passphrase, Words: 3, Separator: " ", Capitalize: true),
    new(GeneratorKinds.Passphrase, Words: 20, Separator: "_", IncludeNumber: true),
    new(GeneratorKinds.Passphrase, Words: 6, Separator: ".", Capitalize: true, IncludeNumber: true),
  ];

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(4)]
  [InlineData(5)]
  public void Password_HasTheLength_TheMinimums_AndOnlyEnabledCharacters(int index)
  {
    var options = PasswordOptions[index];
    var upper = options.Uppercase ? PasswordGenerator.Uppercase(options.AvoidAmbiguous) : string.Empty;
    var lower = options.Lowercase ? PasswordGenerator.Lowercase(options.AvoidAmbiguous) : string.Empty;
    var number = options.Numbers ? PasswordGenerator.Numbers(options.AvoidAmbiguous) : string.Empty;
    var special = options.Special ? PasswordGenerator.Special : string.Empty;
    var allowed = upper + lower + number + special;

    for (var i = 0; i < Iterations; i++)
    {
      var value = PasswordGenerator.Generate(options);

      Assert.Equal(options.Length, value.Length);
      Assert.All(value, c => Assert.Contains(c, allowed));
      Assert.True(value.Count(number.Contains) >= (options.Numbers ? options.MinNumber : 0), value);
      Assert.True(value.Count(special.Contains) >= (options.Special ? options.MinSpecial : 0), value);
      Assert.True(!options.Uppercase || value.Any(upper.Contains), value);
      Assert.True(!options.Lowercase || value.Any(lower.Contains), value);
      if (options.AvoidAmbiguous)
        Assert.DoesNotContain(value, Ambiguous.Contains);
    }
  }

  [Fact]
  public void Password_UsesEveryCharacterOfEachSet()
  {
    var options = new GeneratorOptions(Length: 128, Special: true, AvoidAmbiguous: false);
    var seen = string.Concat(Enumerable.Range(0, 200).Select(_ => PasswordGenerator.Generate(options))).ToHashSet();

    Assert.Equal((PasswordGenerator.Uppercase(false) + PasswordGenerator.Lowercase(false) + PasswordGenerator.Numbers(false) + PasswordGenerator.Special).ToHashSet(), seen);
  }

  [Fact]
  public void Password_LetsTheMinimumsWinOverTheLength()
  {
    var value = PasswordGenerator.Generate(new GeneratorOptions(Length: 5, Special: true, MinNumber: 9, MinSpecial: 9));

    Assert.Equal(20, value.Length);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  public void Passphrase_HasTheWords_TheSeparator_TheCapitals_AndTheNumber(int index)
  {
    var options = PassphraseOptions[index];
    var words = PasswordGenerator.WordList.ToHashSet();

    for (var i = 0; i < Iterations; i++)
    {
      var parts = PasswordGenerator.Generate(options).Split(options.Separator);

      Assert.Equal(options.Words, parts.Length);
      Assert.Equal(options.IncludeNumber ? 1 : 0, parts.Count(p => char.IsAsciiDigit(p[^1])));
      Assert.All(parts, p =>
      {
        var word = p.TrimEnd("0123456789".ToCharArray());
        Assert.Equal(options.Capitalize, char.IsUpper(word[0]));
        Assert.Contains(options.Capitalize ? char.ToLowerInvariant(word[0]) + word[1..] : word, words);
        Assert.DoesNotContain('-', word);
        Assert.True(p.Length - word.Length <= 1);
      });
    }
  }

  [Fact]
  public void Passphrase_WithNoSeparator_JoinsTheWords()
  {
    var value = PasswordGenerator.Generate(new GeneratorOptions(GeneratorKinds.Passphrase, Words: 3, Separator: ""));

    Assert.All(value, c => Assert.True(char.IsAsciiLetterLower(c)));
  }

  [Fact]
  public void WordList_IsTheEffLargeList()
  {
    Assert.Equal(7776, PasswordGenerator.WordList.Length);
    Assert.Equal(7776, PasswordGenerator.WordList.Distinct().Count());
    Assert.Equal(("abacus", "zoom"), (PasswordGenerator.WordList[0], PasswordGenerator.WordList[^1]));
    Assert.All(PasswordGenerator.WordList, w => Assert.Matches("^[a-z-]+$", w));
    Assert.Equal(7772, PasswordGenerator.PassphraseWords.Length);
  }

  [Theory]
  [InlineData(GeneratorKinds.Password, 4, 4, "-", 1, 1, true)]
  [InlineData(GeneratorKinds.Password, 129, 4, "-", 1, 1, true)]
  [InlineData(GeneratorKinds.Password, 20, 4, "-", 10, 1, true)]
  [InlineData(GeneratorKinds.Password, 20, 4, "-", 1, -1, true)]
  [InlineData(GeneratorKinds.Password, 20, 4, "-", 1, 1, false)]
  [InlineData(GeneratorKinds.Passphrase, 20, 2, "-", 1, 1, true)]
  [InlineData(GeneratorKinds.Passphrase, 20, 21, "-", 1, 1, true)]
  [InlineData(GeneratorKinds.Passphrase, 20, 4, "\" --raw", 1, 1, true)]
  [InlineData(GeneratorKinds.Passphrase, 20, 4, "{", 1, 1, true)]
  [InlineData(GeneratorKinds.Passphrase, 20, 4, "&", 1, 1, true)]
  [InlineData(GeneratorKinds.Passphrase, 20, 4, "ab", 1, 1, true)]
  [InlineData("username", 20, 4, "-", 1, 1, true)]
  public void Generate_RejectsOutOfRangeOptions(string kind, int length, int words, string separator, int minNumber, int minSpecial, bool anyClass)
  {
    var options = new GeneratorOptions(kind, length, anyClass, anyClass, anyClass, false, minNumber, minSpecial, Words: words, Separator: separator);
    Assert.Equal(IpcErrors.Invalid, Assert.Throws<IpcException>(() => PasswordGenerator.Generate(options)).Code);
  }
}
