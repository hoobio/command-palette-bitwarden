using Avalonia.Media.Imaging;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal enum FilterKind
{
  Header,
  All,
  Favorites,
  Type,
  Folder,
  Collection,
  Organization,
  Archive,
  Trash,
}

internal sealed record FilterViewModel(FilterKind Kind, string Label, string Glyph = "", string? Id = null, int Type = 0)
{
  public bool IsHeader => Kind == FilterKind.Header;

  public bool Matches(ItemSummary item) => Kind switch
  {
    FilterKind.Trash => item.Deleted,
    FilterKind.Archive => item.Archived && !item.Deleted,
    _ when item.Deleted || item.Archived => false,
    FilterKind.Favorites => item.Favorite,
    FilterKind.Type => item.Type == Type,
    FilterKind.Folder => item.FolderId == Id,
    FilterKind.Collection => Id is not null && item.CollectionIds.Contains(Id),
    FilterKind.Organization => item.OrganizationId == Id,
    _ => true,
  };
}

internal sealed class ItemRowViewModel(ItemSummary summary)
{
  private static readonly Dictionary<string, Bitmap?> Icons = new(StringComparer.OrdinalIgnoreCase);

  public ItemSummary Summary { get; } = summary;

  public string Id => Summary.Id;

  public string Name => Summary.Name;

  public string? Subtext => Summary.Subtext;

  public bool HasSubtext => !string.IsNullOrEmpty(Summary.Subtext);

  public string Glyph => ItemViewModel.GlyphFor(Summary.Type);

  public Bitmap? Icon => Summary.IconPath is { } path ? LoadIcon(path) : null;

  public bool HasIcon => Icon is not null;

  internal bool IsSameVersion(ItemSummary other) =>
    other.RevisionDate == Summary.RevisionDate
    && other.Deleted == Summary.Deleted
    && other.Archived == Summary.Archived
    && other.IconPath == Summary.IconPath;

  internal bool Matches(string query)
  {
    if (query.Length == 0
      || Summary.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
      || (Summary.Subtext?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
      return true;
    foreach (var host in Summary.Hosts)
    {
      if (host.Contains(query, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }

  private static Bitmap? LoadIcon(string path)
  {
    if (Icons.TryGetValue(path, out var icon))
      return icon;
    try
    {
      icon = new Bitmap(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
    {
      icon = null;
    }

    return Icons[path] = icon;
  }
}
