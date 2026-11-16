using System.Linq;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class ItemActionsPage : ListPage
{
  private readonly BitwardenItem _item;
  private readonly BitwardenCliService? _service;

  public ItemActionsPage(BitwardenItem item, BitwardenCliService? service)
  {
    _item = item;
    _service = service;
    Name = HotkeyCatalog.ShowActions.Title;
    Title = item.Name;
    Icon = new IconInfo("");
  }

  public override IListItem[] GetItems() =>
    VaultItemHelper.BuildContextItems(_item, _service)
      .Select(action => (IListItem)new ListItem(action.Command!) { Title = action.Title, Icon = action.Icon })
      .ToArray();
}
