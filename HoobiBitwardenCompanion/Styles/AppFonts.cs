using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace HoobiBitwardenCompanion.Styles;

internal static class AppFonts
{
  private static readonly string[] EmojiFamilies = ["Segoe UI Emoji"];

  public static AppBuilder UseHoobiFonts(this AppBuilder builder) => builder
    .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFontCollection(new Uri("fonts:Hoobi"), new Uri("avares://HoobiBitwardenCompanion/Assets/Fonts"))))
    .With(new FontManagerOptions
    {
      DefaultFamilyName = "fonts:Hoobi#Hanken Grotesk",
      FontFallbacks = [.. EmojiFamilies.Select(family => new FontFallback { FontFamily = new FontFamily(family) })],
    });
}
