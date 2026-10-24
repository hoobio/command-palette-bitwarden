using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  private static readonly string[] ReservedNames = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

  internal async Task DownloadAttachmentAsync(AttachmentRequest request, bool verified = false)
  {
    var path = RequireLocalPath(request.Path);
    var item = FindGranted(request.Id, request.RevisionDate, verified);
    var attachment = RequireAttachment(item, request.AttachmentId);
    if (!Directory.Exists(Path.GetDirectoryName(path)))
      throw new IpcException(IpcErrors.Invalid);
    if (!await _service.DownloadAttachmentAsync(item.Id, attachment, path))
      throw new IpcException(IpcErrors.Failed);
  }

  internal async Task<SavedResponse> UploadAttachmentAsync(AttachmentRequest request, bool verified = false)
  {
    var id = RequireGuid(request.Id);
    var file = new FileInfo(RequireLocalPath(request.Path));
    if (!file.Exists)
      throw new IpcException(IpcErrors.NotFound);
    if (file.Length > BitwardenCliService.MaxAttachmentBytes)
      throw new IpcException(IpcErrors.TooLarge);
    RequireUnlocked();
    await RequireWriteGrantAsync(id, verified);
    return Saved(await _service.UploadAttachmentAsync(id, request.RevisionDate, file.FullName)) ?? throw new IpcException(IpcErrors.Failed);
  }

  internal async Task<SavedResponse> DeleteAttachmentAsync(AttachmentRequest request, bool verified = false)
  {
    var item = Find(request.Id);
    var attachment = RequireAttachment(item, request.AttachmentId);
    await RequireWriteGrantAsync(item.Id, verified);
    return Saved(await _service.DeleteAttachmentAsync(item.Id, request.RevisionDate, attachment.Id)) ?? throw new IpcException(IpcErrors.Failed);
  }

  private static ItemAttachment RequireAttachment(BitwardenItem item, string? attachmentId)
  {
    if (attachmentId is not { Length: > 0 and <= 64 } || !attachmentId.All(char.IsAsciiLetterOrDigit))
      throw new IpcException(IpcErrors.Invalid);
    return item.Attachments.FirstOrDefault(a => a.Id == attachmentId) ?? throw new IpcException(IpcErrors.NotFound);
  }

  internal static string RequireLocalPath(string? path)
  {
    var valid = path is { Length: >= 4 and < 32_000 }
      && path[0] != '-'
      && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
      && path.IndexOf(':', 2) < 0
      && path.IndexOfAny(['"', '*', '?', '<', '>', '|', '\0', '\r', '\n']) < 0
      && !path.EndsWith('\\')
      && Path.IsPathFullyQualified(path)
      && string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal)
      && !ReservedNames.Contains(Path.GetFileNameWithoutExtension(path).TrimEnd(), StringComparer.OrdinalIgnoreCase);
    return valid ? path! : throw new IpcException(IpcErrors.Invalid);
  }
}
