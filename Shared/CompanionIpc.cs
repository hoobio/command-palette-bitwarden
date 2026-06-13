// Shared IPC contract between the Command Palette extension (pipe server / vault authority) and
// the WinUI companion (pipe client). Linked into BOTH projects via <Compile Include .. Link> so the
// command and field names can't drift. Wire format: 4-byte little-endian length prefix + UTF-8 JSON.
//
// Request : { "id": <int>, "command": "<name>", "args": { ... } }
// Response: { "id": <int>, "ok": <bool>, "error": "<string?>", "data": { ... } }
//
// Both ends are full-trust and in the same MSIX package, so the channel is unencrypted local IPC.
namespace HoobiBitwardenCompanionIpc;

internal static class IpcCommands
{
    public const string GetStatus = "GetStatus";
    public const string Unlock = "Unlock";
    public const string UnlockWithBiometrics = "UnlockWithBiometrics";
    public const string Login = "Login";
    public const string SubmitDeviceVerification = "SubmitDeviceVerification";
    public const string SetServerUrl = "SetServerUrl";
    public const string GetItem = "GetItem";
    public const string SaveItem = "SaveItem";
    public const string EditItem = "EditItem";   // apply an edited item (no sync) - step 1 of a save
    public const string Sync = "Sync";           // push to server + refresh palette - step 2 of a save
    public const string Generate = "Generate";
    public const string QuickRotate = "QuickRotate";
}

internal static class IpcFields
{
    // Envelope
    public const string Id = "id";
    public const string Command = "command";
    public const string Args = "args";
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Data = "data";

    // Common args / data
    public const string ItemId = "itemId";
    public const string ItemJson = "itemJson";        // raw `bw get item` JSON, round-tripped for edit
    public const string MustContain = "mustContain";  // string[] values that must be present after save
    public const string Status = "status";            // unlocked | locked | unauthenticated | clinotfound
    public const string Success = "success";
    public const string Value = "value";              // generated secret

    // Auth args
    public const string Password = "password";
    public const string Email = "email";
    public const string TwoFactorCode = "twoFactorCode";
    public const string TwoFactorMethod = "twoFactorMethod";
    public const string DeviceVerificationCode = "code";
    public const string ServerUrl = "serverUrl";
    public const string TwoFactorRequired = "twoFactorRequired";
    public const string DeviceVerificationRequired = "deviceVerificationRequired";

    // Generator option args (mirror GeneratorOptions)
    public const string Mode = "mode";                // password | passphrase
    public const string Length = "length";
    public const string Uppercase = "uppercase";
    public const string Lowercase = "lowercase";
    public const string Numbers = "numbers";
    public const string Symbols = "symbols";
    public const string MinNumber = "minNumber";
    public const string MinSpecial = "minSpecial";
    public const string AvoidAmbiguous = "avoidAmbiguous";
    public const string Words = "words";
    public const string Separator = "separator";
    public const string Capitalize = "capitalize";
    public const string IncludeNumber = "includeNumber";
}

internal static class IpcStatus
{
    public const string Unlocked = "unlocked";
    public const string Locked = "locked";
    public const string Unauthenticated = "unauthenticated";
    public const string CliNotFound = "clinotfound";
}

// Launch arguments the extension passes to the companion process.
internal static class IpcLaunchArgs
{
    public const string Mode = "--mode";
    public const string ItemId = "--id";
    public const string Pipe = "--pipe";
}
