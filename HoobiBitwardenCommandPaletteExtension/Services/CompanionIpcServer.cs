using System;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwardenCompanionIpc;
using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Named-pipe server end of the extension <-> companion channel (COMPANION_WINUI_PHASE1 section 3.2).
// The extension is the vault/CLI/session authority: the companion sends intents, the extension runs
// them through BitwardenCliService (which holds the session and cache) and returns results. Because
// the extension is the one mutating the vault, its own palette refresh path (RefreshCacheAsync inside
// SaveItemAsync) gives the "live palette updates" requirement for free.
//
// Wire format: 4-byte little-endian length prefix + UTF-8 JSON. Unencrypted: both ends are full-trust
// in the same MSIX package and the pipe's default ACL restricts it to the current user.
internal sealed partial class CompanionIpcServer : IDisposable
{
  private readonly string _pipeName;
  private readonly BitwardenCliService _service;
  private readonly CancellationTokenSource _cts = new();
  private int _started;

  public CompanionIpcServer(string pipeName, BitwardenCliService service)
  {
    _pipeName = pipeName;
    _service = service;
  }

  public void Start()
  {
    if (Interlocked.Exchange(ref _started, 1) != 0) return;
    _ = Task.Run(AcceptLoopAsync);
    DebugLogService.Log("CompanionIpc", $"Server listening on pipe {_pipeName}");
  }

  private async Task AcceptLoopAsync()
  {
    while (!_cts.IsCancellationRequested)
    {
      NamedPipeServerStream pipe;
      try
      {
        pipe = new NamedPipeServerStream(
          _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
          PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
      }
      catch (Exception ex)
      {
        DebugLogService.Log("CompanionIpc", $"Failed to create pipe instance: {ex.GetType().Name}: {ex.Message}");
        break;
      }

      try
      {
        await pipe.WaitForConnectionAsync(_cts.Token);
      }
      catch (OperationCanceledException)
      {
        await pipe.DisposeAsync();
        break;
      }
      catch (Exception ex)
      {
        DebugLogService.Log("CompanionIpc", $"WaitForConnection failed: {ex.GetType().Name}: {ex.Message}");
        await pipe.DisposeAsync();
        continue;
      }

      _ = HandleClientAsync(pipe);
    }
  }

  private async Task HandleClientAsync(NamedPipeServerStream pipe)
  {
    await using (pipe)
    {
      try
      {
        while (pipe.IsConnected && !_cts.IsCancellationRequested)
        {
          var request = await IpcFraming.ReadMessageAsync(pipe, _cts.Token);
          if (request == null) break;

          JsonObject response;
          try
          {
            response = await DispatchAsync(request);
          }
          catch (Exception ex)
          {
            DebugLogService.Log("CompanionIpc", $"Dispatch threw: {ex.GetType().Name}: {ex.Message}");
            response = Error(request[IpcFields.Id]?.GetValue<int>() ?? 0, ex.Message);
          }

          await IpcFraming.WriteMessageAsync(pipe, response, _cts.Token);
        }
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        DebugLogService.Log("CompanionIpc", $"Client loop ended: {ex.GetType().Name}: {ex.Message}");
      }
    }
  }

  private async Task<JsonObject> DispatchAsync(JsonObject request)
  {
    var id = request[IpcFields.Id]?.GetValue<int>() ?? 0;
    var command = request[IpcFields.Command]?.GetValue<string>() ?? "";
    var args = request[IpcFields.Args]?.AsObject() ?? new JsonObject();
    DebugLogService.Log("CompanionIpc", $"<- {command} (#{id})");

    var data = new JsonObject();
    switch (command)
    {
      case IpcCommands.GetStatus:
      {
        var status = await _service.GetVaultStatusAsync();
        data[IpcFields.Status] = StatusToString(status);
        return Ok(id, data);
      }

      case IpcCommands.Unlock:
      {
        var (success, error) = await _service.UnlockAsync(args[IpcFields.Password]?.GetValue<string>() ?? "");
        data[IpcFields.Success] = success;
        data[IpcFields.Error] = error;
        return Ok(id, data);
      }

      case IpcCommands.UnlockWithBiometrics:
      {
        var (success, error) = await _service.UnlockWithBiometricsAsync();
        data[IpcFields.Success] = success;
        data[IpcFields.Error] = error;
        return Ok(id, data);
      }

      case IpcCommands.Login:
      {
        var (success, error, twoFactorRequired, deviceVerificationRequired) = await _service.LoginAsync(
          args[IpcFields.Email]?.GetValue<string>() ?? "",
          args[IpcFields.Password]?.GetValue<string>() ?? "",
          args[IpcFields.TwoFactorCode]?.GetValue<string>(),
          args[IpcFields.TwoFactorMethod]?.GetValue<int>() ?? 0);
        data[IpcFields.Success] = success;
        data[IpcFields.Error] = error;
        data[IpcFields.TwoFactorRequired] = twoFactorRequired;
        data[IpcFields.DeviceVerificationRequired] = deviceVerificationRequired;
        return Ok(id, data);
      }

      case IpcCommands.SubmitDeviceVerification:
      {
        var (success, error) = await _service.SubmitDeviceVerificationAsync(args[IpcFields.DeviceVerificationCode]?.GetValue<string>() ?? "");
        data[IpcFields.Success] = success;
        data[IpcFields.Error] = error;
        return Ok(id, data);
      }

      case IpcCommands.SetServerUrl:
      {
        var error = await _service.SetServerUrlAsync(new ServerConfig(args[IpcFields.ServerUrl]?.GetValue<string>() ?? ""));
        data[IpcFields.Success] = error == null;
        data[IpcFields.Error] = error;
        return Ok(id, data);
      }

      case IpcCommands.GetItem:
      {
        var raw = await _service.GetItemRawAsync(args[IpcFields.ItemId]?.GetValue<string>() ?? "");
        if (raw == null) return Error(id, "Item not found or vault is locked.");
        data[IpcFields.ItemJson] = raw;
        return Ok(id, data);
      }

      case IpcCommands.SaveItem:
      {
        var itemId = args[IpcFields.ItemId]?.GetValue<string>() ?? "";
        var itemJson = args[IpcFields.ItemJson]?.GetValue<string>() ?? "";
        var mustContain = ReadStringArray(args[IpcFields.MustContain]);
        var result = await _service.SaveItemAsync(itemId, itemJson, mustContain);
        data[IpcFields.Success] = result.Success;
        data[IpcFields.Error] = result.Error;
        if (result.Success)
        {
          var refetched = await _service.GetItemRawAsync(itemId);
          if (refetched != null) data[IpcFields.ItemJson] = refetched;
        }
        return Ok(id, data);
      }

      // EditItem + Sync are the discrete steps of a save, so the companion can show live progress
      // (apply -> sync -> verify) instead of one opaque call.
      case IpcCommands.EditItem:
      {
        try
        {
          var edited = await _service.EditItemAsync(args[IpcFields.ItemId]?.GetValue<string>() ?? "", args[IpcFields.ItemJson]?.GetValue<string>() ?? "");
          data[IpcFields.Success] = edited != null;
          data[IpcFields.Error] = edited == null ? "The Bitwarden CLI rejected the edit." : null;
        }
        catch (Exception ex)
        {
          data[IpcFields.Success] = false;
          data[IpcFields.Error] = ex.Message;
        }
        return Ok(id, data);
      }

      case IpcCommands.Sync:
      {
        try
        {
          await _service.SyncVaultAsync();
          await _service.RefreshCacheAsync(); // live palette update
          data[IpcFields.Success] = true;
        }
        catch (Exception ex)
        {
          data[IpcFields.Success] = false;
          data[IpcFields.Error] = ex.Message;
        }
        return Ok(id, data);
      }

      case IpcCommands.Generate:
      {
        var value = await _service.GenerateAsync(ParseGeneratorOptions(args));
        if (value == null) return Error(id, "The generator returned no value.");
        data[IpcFields.Value] = value;
        return Ok(id, data);
      }

      case IpcCommands.QuickRotate:
        return await QuickRotateAsync(id, args[IpcFields.ItemId]?.GetValue<string>() ?? "", data);

      default:
        return Error(id, $"Unknown command '{command}'.");
    }
  }

  private async Task<JsonObject> QuickRotateAsync(int id, string itemId, JsonObject data)
  {
    var raw = await _service.GetItemRawAsync(itemId);
    if (raw == null) return Error(id, "Item not found or vault is locked.");

    if (JsonNode.Parse(raw) is not JsonObject item)
      return Error(id, "Could not parse the item.");

    var newValue = await _service.GenerateAsync(GeneratorOptionsProvider.DefaultPassword());
    if (string.IsNullOrEmpty(newValue)) return Error(id, "The generator returned no value.");

    if (!BitwardenCliService.TrySetSingleHiddenSecret(item, newValue, out var rotateError))
      return Error(id, rotateError ?? "Cannot rotate this item.");

    var result = await _service.SaveItemAsync(itemId, item.ToJsonString(), [newValue]);
    data[IpcFields.Success] = result.Success;
    data[IpcFields.Error] = result.Error;
    data[IpcFields.Value] = result.Success ? newValue : null;
    return Ok(id, data);
  }

  private static GeneratorOptions ParseGeneratorOptions(JsonObject args)
  {
    var isPassphrase = string.Equals(args[IpcFields.Mode]?.GetValue<string>(), "passphrase", StringComparison.OrdinalIgnoreCase);
    var defaults = isPassphrase ? GeneratorOptionsProvider.DefaultPassphrase() : GeneratorOptionsProvider.DefaultPassword();
    return defaults with
    {
      Mode = isPassphrase ? GeneratorMode.Passphrase : GeneratorMode.Password,
      Length = args[IpcFields.Length]?.GetValue<int>() ?? defaults.Length,
      Uppercase = args[IpcFields.Uppercase]?.GetValue<bool>() ?? defaults.Uppercase,
      Lowercase = args[IpcFields.Lowercase]?.GetValue<bool>() ?? defaults.Lowercase,
      Numbers = args[IpcFields.Numbers]?.GetValue<bool>() ?? defaults.Numbers,
      Symbols = args[IpcFields.Symbols]?.GetValue<bool>() ?? defaults.Symbols,
      MinNumber = args[IpcFields.MinNumber]?.GetValue<int>() ?? defaults.MinNumber,
      MinSpecial = args[IpcFields.MinSpecial]?.GetValue<int>() ?? defaults.MinSpecial,
      AvoidAmbiguous = args[IpcFields.AvoidAmbiguous]?.GetValue<bool>() ?? defaults.AvoidAmbiguous,
      Words = args[IpcFields.Words]?.GetValue<int>() ?? defaults.Words,
      Separator = args[IpcFields.Separator]?.GetValue<string>() ?? defaults.Separator,
      Capitalize = args[IpcFields.Capitalize]?.GetValue<bool>() ?? defaults.Capitalize,
      IncludeNumber = args[IpcFields.IncludeNumber]?.GetValue<bool>() ?? defaults.IncludeNumber,
    };
  }

  private static string[] ReadStringArray(JsonNode? node)
  {
    if (node is not JsonArray arr) return [];
    var list = new System.Collections.Generic.List<string>(arr.Count);
    foreach (var n in arr)
    {
      var s = n?.GetValue<string>();
      if (!string.IsNullOrEmpty(s)) list.Add(s);
    }
    return [.. list];
  }

  private static string StatusToString(VaultStatus status) => status switch
  {
    VaultStatus.Unlocked => IpcStatus.Unlocked,
    VaultStatus.Locked => IpcStatus.Locked,
    VaultStatus.Unauthenticated => IpcStatus.Unauthenticated,
    _ => IpcStatus.CliNotFound,
  };

  private static JsonObject Ok(int id, JsonObject data) => new()
  {
    [IpcFields.Id] = id,
    [IpcFields.Ok] = true,
    [IpcFields.Data] = data,
  };

  private static JsonObject Error(int id, string message) => new()
  {
    [IpcFields.Id] = id,
    [IpcFields.Ok] = false,
    [IpcFields.Error] = message,
  };

  public void Dispose()
  {
    try { _cts.Cancel(); } catch { }
    _cts.Dispose();
  }
}
