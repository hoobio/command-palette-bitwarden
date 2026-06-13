using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCompanion.Services;

// Typed facade over the IPC client - the companion's "vault API". Every call is an intent the
// extension executes (it owns the session/CLI). This is the single seam the WinUI pages depend on,
// so it stays reusable if the companion grows into a fuller vault UI later.
internal sealed class VaultClient
{
    private readonly ExtensionIpcClient _ipc;

    public VaultClient(ExtensionIpcClient ipc) => _ipc = ipc;

    public Task ConnectAsync() => _ipc.ConnectAsync();

    public async Task<string> GetStatusAsync()
    {
        var r = await _ipc.SendAsync(IpcCommands.GetStatus);
        return r.GetString(IpcFields.Status) ?? IpcStatus.CliNotFound;
    }

    public async Task<(bool Ok, string? Error)> UnlockAsync(string password)
    {
        var r = await _ipc.SendAsync(IpcCommands.Unlock, new JsonObject { [IpcFields.Password] = password });
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error));
    }

    public async Task<(bool Ok, string? Error)> UnlockWithBiometricsAsync()
    {
        var r = await _ipc.SendAsync(IpcCommands.UnlockWithBiometrics);
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error));
    }

    public async Task<(bool Ok, string? Error, bool TwoFactorRequired, bool DeviceVerificationRequired)> LoginAsync(
        string email, string password, string? twoFactorCode = null, int twoFactorMethod = 0)
    {
        var args = new JsonObject
        {
            [IpcFields.Email] = email,
            [IpcFields.Password] = password,
            [IpcFields.TwoFactorCode] = twoFactorCode,
            [IpcFields.TwoFactorMethod] = twoFactorMethod,
        };
        var r = await _ipc.SendAsync(IpcCommands.Login, args);
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error),
            r.GetBool(IpcFields.TwoFactorRequired), r.GetBool(IpcFields.DeviceVerificationRequired));
    }

    public async Task<(bool Ok, string? Error)> SubmitDeviceVerificationAsync(string code)
    {
        var r = await _ipc.SendAsync(IpcCommands.SubmitDeviceVerification, new JsonObject { [IpcFields.DeviceVerificationCode] = code });
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error));
    }

    public async Task<(bool Ok, string? Error)> SetServerUrlAsync(string serverUrl)
    {
        var r = await _ipc.SendAsync(IpcCommands.SetServerUrl, new JsonObject { [IpcFields.ServerUrl] = serverUrl });
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error));
    }

    public async Task<string?> GetServerUrlAsync()
    {
        var r = await _ipc.SendAsync(IpcCommands.GetServerUrl);
        return r.Ok ? r.GetString(IpcFields.ServerUrl) : null;
    }

    public async Task<JsonObject?> GetItemAsync(string id)
    {
        var r = await _ipc.SendAsync(IpcCommands.GetItem, new JsonObject { [IpcFields.ItemId] = id });
        if (!r.Ok) return null;
        var json = r.GetString(IpcFields.ItemJson);
        return json == null ? null : JsonNode.Parse(json) as JsonObject;
    }

    public async Task<(bool Ok, string? Error, JsonObject? Item)> SaveItemAsync(string id, JsonObject item, IEnumerable<string>? mustContain = null)
    {
        var args = new JsonObject
        {
            [IpcFields.ItemId] = id,
            [IpcFields.ItemJson] = item.ToJsonString(),
        };
        if (mustContain != null)
        {
            var arr = new JsonArray();
            foreach (var v in mustContain) arr.Add(v);
            args[IpcFields.MustContain] = arr;
        }

        var r = await _ipc.SendAsync(IpcCommands.SaveItem, args);
        var ok = r.GetBool(IpcFields.Success);
        var refreshedJson = r.GetString(IpcFields.ItemJson);
        var refreshed = refreshedJson == null ? null : JsonNode.Parse(refreshedJson) as JsonObject;
        return (ok, r.GetString(IpcFields.Error), refreshed);
    }

    // Stepwise save so the UI can show live progress: apply -> sync -> verify on the server. The
    // verify (mustContain present in the re-fetched item) is the §3.6 data-loss safety - success is
    // only reported once the value is confirmed server-side. progress() is invoked on the caller's
    // (UI) thread between steps.
    public async Task<(bool Ok, string? Error, JsonObject? Item)> SaveSteppedAsync(
        string id, JsonObject item, IEnumerable<string>? mustContain, Action<string> progress)
    {
        progress("Applying your changes…");
        var (editOk, editErr) = await EditItemAsync(id, item);
        if (!editOk) return (false, editErr ?? "The change could not be applied.", null);

        progress("Syncing to the Bitwarden server…");
        var (syncOk, syncErr) = await SyncAsync();
        if (!syncOk) return (false, $"Saved locally but the server sync failed: {syncErr}. The change may not be on the server yet.", null);

        progress("Verifying the change on the server…");
        var refetched = await GetItemAsync(id);
        if (refetched == null) return (false, "Could not verify the save reached the server.", null);

        if (mustContain != null)
        {
            var json = refetched.ToJsonString();
            foreach (var value in mustContain)
            {
                if (!string.IsNullOrEmpty(value) && !json.Contains(value, StringComparison.Ordinal))
                    return (false, "Verification failed: the new value is not present on the server. Do NOT assume it was saved.", null);
            }
        }

        return (true, null, refetched);
    }

    private async Task<(bool Ok, string? Error)> EditItemAsync(string id, JsonObject item)
    {
        var r = await _ipc.SendAsync(IpcCommands.EditItem, new JsonObject { [IpcFields.ItemId] = id, [IpcFields.ItemJson] = item.ToJsonString() });
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error));
    }

    private async Task<(bool Ok, string? Error)> SyncAsync()
    {
        var r = await _ipc.SendAsync(IpcCommands.Sync);
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error));
    }

    public async Task<string?> GenerateAsync(GeneratorOptions options)
    {
        var r = await _ipc.SendAsync(IpcCommands.Generate, options.ToIpcArgs());
        return r.Ok ? r.GetString(IpcFields.Value) : null;
    }

    public async Task<(bool Ok, string? Error, string? Value)> QuickRotateAsync(string id)
    {
        var r = await _ipc.SendAsync(IpcCommands.QuickRotate, new JsonObject { [IpcFields.ItemId] = id });
        return (r.GetBool(IpcFields.Success), r.GetString(IpcFields.Error), r.GetString(IpcFields.Value));
    }
}
