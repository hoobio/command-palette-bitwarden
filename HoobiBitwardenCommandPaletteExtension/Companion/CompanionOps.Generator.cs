using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  internal const int HistoryLimit = 50;
  internal const int RecentLimit = 20;

  private readonly Lock _historyLock = new();
  private readonly List<(GeneratedEntry Entry, ProtectedSecret Value)> _recent = [];
  private readonly List<(GeneratedEntry Entry, ProtectedSecret Value)> _history = [];
  private long _historyId;

  internal GeneratedResponse Generate(GeneratorOptions options)
  {
    RequireUnlocked();
    var value = RequireGenerated(options, PasswordGenerator.Generate(options));
    lock (_historyLock)
    {
      var entry = new GeneratedEntry(++_historyId, options.Kind, DateTime.UtcNow);
      Push(_recent, (entry, ProtectedSecret.From(value)!), RecentLimit);
      return new GeneratedResponse(value, entry.Id);
    }
  }

  internal GeneratorHistoryResponse ListGeneratorHistory()
  {
    RequireUnlocked();
    lock (_historyLock)
      return new GeneratorHistoryResponse(_history.Select(h => h.Entry).ToList());
  }

  internal void CopyGenerated(long id)
  {
    RequireUnlocked();
    _copySecret(Keep(id) ?? throw new IpcException(IpcErrors.NotFound));
  }

  internal async Task<SavedResponse> RotatePasswordAsync(RotateRequest request, bool verified = false)
  {
    var options = request.Options;
    if (request.EntryId is null)
    {
      if (options?.Kind is not (GeneratorKinds.Password or GeneratorKinds.Passphrase))
        throw new IpcException(IpcErrors.Invalid);
      PasswordGenerator.Validate(options);
    }

    var item = Find(request.Id);
    if (item.Type != BitwardenItemType.Login || item.DeletedDate is not null || item.ArchivedDate is not null)
      throw new IpcException(IpcErrors.Invalid);
    await RequireWriteGrantAsync(item.Id, verified);
    var password = request.EntryId is { } entryId ? FindGenerated(entryId).Reveal() : RequireGenerated(options!, PasswordGenerator.Generate(options!));
    var saved = await _service.EditItemAsync(item.Id, request.RevisionDate, current =>
    {
      if (Int(current, "type") != 1)
        throw new IpcException(IpcErrors.Invalid);
      Child(current, "login")["password"] = password;
      return current;
    });
    var response = Saved(saved) ?? throw new IpcException(IpcErrors.Failed);
    _copyText(password, true);
    if (request.EntryId is { } kept)
      Keep(kept);
    else
      lock (_historyLock)
        Push(_history, (new GeneratedEntry(++_historyId, options!.Kind, DateTime.UtcNow), ProtectedSecret.From(password)!), HistoryLimit);
    return response;
  }

  private ProtectedSecret FindGenerated(long id)
  {
    ProtectedSecret? value;
    lock (_historyLock)
      value = _history.Find(h => h.Entry.Id == id).Value ?? _recent.Find(h => h.Entry.Id == id).Value;
    return value ?? throw new IpcException(IpcErrors.NotFound);
  }

  private ProtectedSecret? Keep(long id)
  {
    lock (_historyLock)
    {
      if (_history.Find(h => h.Entry.Id == id).Value is { } listed)
        return listed;
      var index = _recent.FindIndex(h => h.Entry.Id == id);
      if (index < 0)
        return null;
      var recent = _recent[index];
      _recent.RemoveAt(index);
      Push(_history, recent, HistoryLimit);
      return recent.Value;
    }
  }

  internal static void Push(List<(GeneratedEntry Entry, ProtectedSecret Value)> list, (GeneratedEntry Entry, ProtectedSecret Value) item, int limit)
  {
    list.Insert(0, item);
    while (list.Count > limit)
    {
      list[^1].Value.Dispose();
      list.RemoveAt(list.Count - 1);
    }
  }

  internal static string RequireGenerated(GeneratorOptions options, string value)
  {
    var valid = value.Length is > 0 and <= 1000
      && !value.Any(char.IsControl)
      && (options.Kind == GeneratorKinds.Passphrase
        ? options.Separator == " " || !value.Contains(' ', StringComparison.Ordinal)
        : value.Length == options.Length && !value.Any(char.IsWhiteSpace));
    return valid ? value : throw new IpcException(IpcErrors.Failed);
  }

  private void ClearGeneratorHistory()
  {
    lock (_historyLock)
    {
      _recent.ForEach(h => h.Value.Dispose());
      _recent.Clear();
      _history.ForEach(h => h.Value.Dispose());
      _history.Clear();
    }
  }
}
