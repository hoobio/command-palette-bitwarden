using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class SectionHeaderSetting : Setting<bool>
{
    private readonly bool _separator;

    public SectionHeaderSetting(string key, string label, bool separator = true)
        : base(key, false)
    {
        Label = label;
        _separator = separator;
    }

    public override Dictionary<string, object> ToDictionary() => new()
    {
        { "type", "TextBlock" },
        { "text", Label },
        { "weight", "Bolder" },
        { "size", "Medium" },
        { "separator", _separator },
        { "spacing", _separator ? "Large" : "Small" },
        { "wrap", true },
    };

    public override void Update(JsonObject payload)
    {
    }

    public override string ToState() => $"\"{Key}\": \"\"";
}
