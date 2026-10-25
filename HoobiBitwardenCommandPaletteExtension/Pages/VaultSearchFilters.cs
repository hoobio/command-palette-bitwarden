using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Helpers;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class VaultSearchFilters : Filters
{
    private readonly IFilterItem[] _items;

    internal IReadOnlyList<SearchFilterEntry> Entries { get; }

    internal SearchFilterEntry Selected => SearchFilterCatalog.Resolve(Entries, CurrentFilterId);

    internal VaultSearchFilters(IReadOnlyList<SearchFilterEntry> entries, string currentFilterId, Action onSelectionChanged)
    {
        Entries = entries;
        _items = BuildItems(entries);
        CurrentFilterId = SearchFilterCatalog.Resolve(entries, currentFilterId).Id;
        PropChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CurrentFilterId))
                onSelectionChanged();
        };
    }

    public override IFilterItem[] GetFilters() => _items;

    private static IFilterItem[] BuildItems(IReadOnlyList<SearchFilterEntry> entries)
    {
        var items = new List<IFilterItem>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0 && entries[i].Section != entries[i - 1].Section)
                items.Add(new Separator(entries[i].Section));
            items.Add(new Filter { Id = entries[i].Id, Name = entries[i].Name, Icon = new IconInfo(entries[i].Glyph) });
        }
        return [.. items];
    }
}
