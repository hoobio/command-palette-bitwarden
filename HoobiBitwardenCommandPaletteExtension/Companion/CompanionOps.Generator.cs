using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  internal const int HistoryLimit = 50;
  private const string SeparatorAllowlist = "-_.,:;+=#!@$~";

  private readonly Lock _historyLock = new();
  private readonly List<(GeneratedEntry Entry, ProtectedSecret Value)> _history = [];
  private long _historyId;

  internal async Task<GeneratedResponse> GenerateAsync(GeneratorOptions options)
  {
    RequireUnlocked();
    var value = RequireGenerated(options, await _service.GenerateAsync(BuildGenerateArgs(options)));
    return new GeneratedResponse(value, Remember(value, options.Kind));
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
    _copySecret(FindGenerated(id));
  }

  internal async Task<SavedResponse> RotatePasswordAsync(RotateRequest request, bool verified = false)
  {
    var options = request.Options;
    if (request.EntryId is null && options?.Kind is not (GeneratorKinds.Password or GeneratorKinds.Passphrase))
      throw new IpcException(IpcErrors.Invalid);
    var args = request.EntryId is null ? BuildGenerateArgs(options!) : null;
    var item = Find(request.Id);
    if (item.Type != BitwardenItemType.Login || item.DeletedDate is not null || item.ArchivedDate is not null)
      throw new IpcException(IpcErrors.Invalid);
    await RequireWriteGrantAsync(item.Id, verified);
    var password = args is null ? FindGenerated(request.EntryId!.Value).Reveal() : RequireGenerated(options!, await _service.GenerateAsync(args));
    var saved = await _service.EditItemAsync(item.Id, request.RevisionDate, current =>
    {
      if (Int(current, "type") != 1)
        throw new IpcException(IpcErrors.Invalid);
      Child(current, "login")["password"] = password;
      return current;
    });
    var response = Saved(saved) ?? throw new IpcException(IpcErrors.Failed);
    _copyText(password, true);
    if (args is not null)
      Remember(password, options!.Kind);
    return response;
  }

  private ProtectedSecret FindGenerated(long id)
  {
    ProtectedSecret? value;
    lock (_historyLock)
      value = _history.Find(h => h.Entry.Id == id).Value;
    return value ?? throw new IpcException(IpcErrors.NotFound);
  }

  internal static string BuildGenerateArgs(GeneratorOptions options)
  {
    var args = new StringBuilder("generate");
    switch (options.Kind)
    {
      case GeneratorKinds.Passphrase when options.Words is >= 3 and <= 20:
        args.Append(CultureInfo.InvariantCulture, $" --passphrase --words {options.Words}");
        args.Append(options.Separator switch
        {
          "-" => string.Empty,
          "" => " --separator empty",
          " " => " --separator space",
          [var c] when SeparatorAllowlist.Contains(c) => " --separator " + c,
          _ => throw new IpcException(IpcErrors.Invalid),
        });
        if (options.Capitalize)
          args.Append(" --capitalize");
        if (options.IncludeNumber)
          args.Append(" --includeNumber");
        break;
      case GeneratorKinds.Password when options.Length is >= 5 and <= 128 && options.MinNumber is >= 0 and <= 9 && options.MinSpecial is >= 0 and <= 9
        && (options.Uppercase || options.Lowercase || options.Numbers || options.Special):
        args.Append(" -");
        if (options.Uppercase)
          args.Append('u');
        if (options.Lowercase)
          args.Append('l');
        if (options.Numbers)
          args.Append('n');
        if (options.Special)
          args.Append('s');
        args.Append(CultureInfo.InvariantCulture, $" --length {options.Length}");
        if (options.Numbers && options.MinNumber > 0)
          args.Append(CultureInfo.InvariantCulture, $" --minNumber {options.MinNumber}");
        if (options.Special && options.MinSpecial > 0)
          args.Append(CultureInfo.InvariantCulture, $" --minSpecial {options.MinSpecial}");
        if (options.AvoidAmbiguous)
          args.Append(" --ambiguous");
        break;
      default:
        throw new IpcException(IpcErrors.Invalid);
    }

    return args.ToString();
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

  private long Remember(string value, string kind)
  {
    lock (_historyLock)
    {
      var entry = new GeneratedEntry(++_historyId, kind, DateTime.UtcNow);
      _history.Insert(0, (entry, ProtectedSecret.From(value)!));
      while (_history.Count > HistoryLimit)
      {
        _history[^1].Value.Dispose();
        _history.RemoveAt(_history.Count - 1);
      }

      return entry.Id;
    }
  }

  private void ClearGeneratorHistory()
  {
    lock (_historyLock)
    {
      _history.ForEach(h => h.Value.Dispose());
      _history.Clear();
    }
  }
}
