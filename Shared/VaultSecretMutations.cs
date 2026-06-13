using System.Collections.Generic;
using System.Text.Json.Nodes;

// Shared, process-agnostic mutation of a `bw` item JSON object. Linked into both projects so the
// extension (server-side QuickRotate) and the companion (Quick Rotate window) target the secret the
// same way. Pure JSON, no session.
namespace HoobiBitwardenCompanionIpc;

internal static class VaultSecretMutations
{
    // Quick Rotate's target: the login password takes priority (the primary secret, so any login can
    // be rotated even when it also has hidden fields); otherwise a single hidden custom field. Sets it
    // to newValue in-place. Returns false with a reason when there's no password and zero, or several,
    // hidden fields (ambiguous - the user should edit explicitly).
    public static bool TrySetSingleHiddenSecret(JsonObject item, string newValue, out string? error)
    {
        error = null;
        var login = item["login"]?.AsObject();
        if (login != null && !string.IsNullOrEmpty(login["password"]?.GetValue<string>()))
        {
            login["password"] = newValue;
            return true;
        }

        var hiddenFields = new List<JsonObject>();
        if (item["fields"] is JsonArray fields)
        {
            foreach (var f in fields)
            {
                if (f is JsonObject fo && (fo["type"]?.GetValue<int>() ?? 0) == 1)
                    hiddenFields.Add(fo);
            }
        }

        if (hiddenFields.Count == 0)
        {
            error = "This item has no password or hidden field to rotate.";
            return false;
        }
        if (hiddenFields.Count > 1)
        {
            error = "This item has more than one hidden field. Open it and rotate the field you want.";
            return false;
        }

        hiddenFields[0]["value"] = newValue;
        return true;
    }
}
