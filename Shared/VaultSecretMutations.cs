using System.Collections.Generic;
using System.Text.Json.Nodes;

// Shared, process-agnostic mutation of a `bw` item JSON object. Linked into both projects so the
// extension (server-side QuickRotate) and the companion (Quick Rotate window) target the secret the
// same way. Pure JSON, no session.
namespace HoobiBitwardenCompanionIpc;

internal static class VaultSecretMutations
{
    // Quick Rotate targets an item with a SINGLE secret/hidden field (the common case: one login
    // password, or one hidden custom field). Sets it to newValue in-place. Returns false with a reason
    // when there is no secret, or more than one (ambiguous - the user should edit explicitly).
    public static bool TrySetSingleHiddenSecret(JsonObject item, string newValue, out string? error)
    {
        error = null;
        var login = item["login"]?.AsObject();
        var hasLoginPassword = login != null && !string.IsNullOrEmpty(login["password"]?.GetValue<string>());

        var hiddenFields = new List<JsonObject>();
        if (item["fields"] is JsonArray fields)
        {
            foreach (var f in fields)
            {
                if (f is JsonObject fo && (fo["type"]?.GetValue<int>() ?? 0) == 1)
                    hiddenFields.Add(fo);
            }
        }

        var candidateCount = (hasLoginPassword ? 1 : 0) + hiddenFields.Count;
        if (candidateCount == 0)
        {
            error = "This item has no password or hidden field to rotate.";
            return false;
        }
        if (candidateCount > 1)
        {
            error = "This item has more than one secret field. Open it and rotate the field you want.";
            return false;
        }

        if (hasLoginPassword)
            login!["password"] = newValue;
        else
            hiddenFields[0]["value"] = newValue;

        return true;
    }
}
