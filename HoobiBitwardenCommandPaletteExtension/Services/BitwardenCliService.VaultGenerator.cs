using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed partial class BitwardenCliService
{
  internal async Task<string> GenerateAsync(string args) => (await RunCliAsync(args)).Trim();
}
