using System.Text.Json;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.Services;

internal static class CompanionSettings
{
  internal static string FilePath { get; set; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoobiBitwardenCommandPalette", "companion-generator.json");

  public static GeneratorOptions LoadGenerator()
  {
    try
    {
      return File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), IpcJson.Default.GeneratorOptions) ?? new() : new();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
      return new();
    }
  }

  public static void SaveGenerator(GeneratorOptions options)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    File.WriteAllText(FilePath, JsonSerializer.Serialize(options, IpcJson.Default.GeneratorOptions));
  }
}
