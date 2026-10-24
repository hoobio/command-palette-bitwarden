using System.Linq;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  internal async Task<FilterRef> CreateFolderAsync(FolderRequest request)
  {
    var name = RequireFolderName(request.Name);
    RequireUnlocked();
    var saved = await _service.CreateFolderAsync(name);
    return new FilterRef(Str(saved, "id") ?? throw new IpcException(IpcErrors.Failed), name);
  }

  internal async Task RenameFolderAsync(FolderRequest request)
  {
    var name = RequireFolderName(request.Name);
    await _service.RenameFolderAsync(RequireFolder(request.Id), name);
  }

  internal Task DeleteFolderAsync(FolderRequest request) => _service.DeleteFolderAsync(RequireFolder(request.Id));

  private string RequireFolder(string? id)
  {
    var folder = RequireGuid(id);
    RequireUnlocked();
    return _service.GetFolders().Keys.FirstOrDefault(k => string.Equals(k, folder, System.StringComparison.OrdinalIgnoreCase))
      ?? throw new IpcException(IpcErrors.NotFound);
  }

  internal static string RequireFolderName(string? name) =>
    name?.Trim() is { Length: > 0 } trimmed && !trimmed.Any(char.IsControl) ? trimmed : throw new IpcException(IpcErrors.Invalid);
}
