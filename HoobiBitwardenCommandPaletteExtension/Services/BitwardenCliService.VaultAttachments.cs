using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed partial class BitwardenCliService
{
  internal const long MaxAttachmentBytes = 500L * 1024 * 1024;

  internal Task<JsonObject> UploadAttachmentAsync(string id, DateTime expectedRevision, string path)
  {
    var name = Path.GetFileName(path);
    return WriteAsync(id, expectedRevision,
      async _ => ThrowIfRejected(await RunWriteAsync($"create attachment --file {Quote(path)} --itemid {id}")),
      (current, saved) => ParseAttachments(saved?["attachments"]) is var after
        && after.Count == ParseAttachments(current?["attachments"]).Count + 1
        && after.Any(a => a.FileName == name));
  }

  internal Task<JsonObject> DeleteAttachmentAsync(string id, DateTime expectedRevision, string attachmentId) =>
    WriteAsync(id, expectedRevision,
      async _ => ThrowIfRejected(await RunWriteAsync($"delete attachment {attachmentId} --itemid {id}")),
      (_, saved) => saved is not null && ParseAttachments(saved["attachments"]).All(a => a.Id != attachmentId));

  internal async Task<bool> DownloadAttachmentAsync(string id, ItemAttachment attachment, string path)
  {
    var started = DateTime.UtcNow.AddSeconds(-2);
    string output;
    try
    {
      output = await RunCliAsync($"get attachment {attachment.Id} --itemid {RequireId(id)} --output {Quote(path)}", WriteTimeoutMs, verbOnlyLog: true);
    }
    catch (TimeoutException)
    {
      throw new VaultWriteException(VaultWriteError.Unknown);
    }

    // The size bw reports is the encrypted size, which is larger than the decrypted file.
    var file = new FileInfo(path);
    if (file.Exists && file.LastWriteTimeUtc >= started && file.Length <= attachment.Size)
      return true;
    ThrowIfRejected(output);
    return false;
  }

  private static bool IsAttachmentDownload(string args) => args.StartsWith("get attachment ", StringComparison.Ordinal);

  private static string Quote(string path) => "\"" + path + "\"";

  internal static string ThrowIfRejected(string output)
  {
    if (ParseObject(output) is not null)
      return output;
    if (output.Contains("premium", StringComparison.OrdinalIgnoreCase))
      throw new VaultWriteException(VaultWriteError.Premium);
    if (output.Contains("Max file size", StringComparison.OrdinalIgnoreCase))
      throw new VaultWriteException(VaultWriteError.TooLarge);
    if (output.Contains("Not enough storage", StringComparison.OrdinalIgnoreCase))
      throw new VaultWriteException(VaultWriteError.NoStorage);
    return output;
  }

  internal static IReadOnlyList<ItemAttachment> ParseAttachments(JsonNode? node) =>
    node is JsonArray array
      ? array.Select(a => (Id: SafeText(a, "id"), FileName: SafeText(a, "fileName"), Size: SizeOf(a?["size"]), SizeName: SafeText(a, "sizeName")))
        .Where(a => !string.IsNullOrEmpty(a.Id) && !string.IsNullOrEmpty(a.FileName))
        .Select(a => new ItemAttachment(a.Id!, a.FileName!, a.Size, a.SizeName))
        .ToArray()
      : Array.Empty<ItemAttachment>();

  private static string? SafeText(JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

  private static long SizeOf(JsonNode? node) => node switch
  {
    JsonValue value when value.TryGetValue<long>(out var number) => number,
    JsonValue value when value.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
    _ => 0,
  };
}
