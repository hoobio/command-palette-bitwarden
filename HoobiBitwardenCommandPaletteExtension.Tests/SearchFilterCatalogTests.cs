using System;
using System.Collections.Generic;
using System.Linq;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class SearchFilterCatalogTests
{
  private static readonly Dictionary<string, string> None = [];

  private static BitwardenCliService CreateService()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData(
    [
      new() { Id = "a", Name = "Alpha", Type = BitwardenItemType.Login, Favorite = true, FolderId = "f-work", OrganizationId = "org-1", RevisionDate = DateTime.UtcNow },
      new() { Id = "b", Name = "Beta", Type = BitwardenItemType.Login, Favorite = true, RevisionDate = DateTime.UtcNow },
      new() { Id = "c", Name = "Gamma", Type = BitwardenItemType.Card, FolderId = "f-home", RevisionDate = DateTime.UtcNow },
    ],
    new() { ["f-work"] = "Work", ["f-home"] = "Home" },
    new() { ["org-1"] = "Acme" });
    return svc;
  }

  [Fact]
  public void Build_StartsWithAllItemsThenStaticFilters()
  {
    var entries = SearchFilterCatalog.Build(None);
    Assert.Equal("All items", entries[0].Name);
    Assert.Null(entries[0].Filter);
    Assert.Equal(SearchFilterCatalog.Static, entries.Skip(1).Take(SearchFilterCatalog.Static.Length));
  }

  [Fact]
  public void Static_CoversEveryNamedFilter()
  {
    var names = SearchFilterCatalog.Static.Select(e => e.Name).ToList();
    foreach (var expected in new[] { "Favourites", "Protected", "Has TOTP", "Has passkey", "Logins", "Cards", "Identities", "Secure notes", "SSH keys" })
      Assert.Contains(expected, names);
  }

  [Fact]
  public void Static_IdsAreUniqueAndFiltersHaveValues()
  {
    var all = SearchFilterCatalog.Build(new Dictionary<string, string> { ["o"] = "Org" });
    Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
    Assert.All(SearchFilterCatalog.Static, e => Assert.False(string.IsNullOrEmpty(e.Filter!.Value.Value)));
  }

  [Fact]
  public void Build_AddsMyVaultAndOrganisationsAfterStatic()
  {
    var entries = SearchFilterCatalog.Build(new Dictionary<string, string> { ["org-2"] = "Beta", ["org-1"] = "acme" });
    var vaults = entries.Where(e => e.Section == "Vaults").ToList();
    Assert.Equal(["My vault", "acme", "Beta"], vaults.Select(v => v.Name));
    Assert.Equal(("org", "none"), vaults[0].Filter);
    Assert.Equal(("org", "org-1"), vaults[1].Filter);
    Assert.Equal(entries[^1], vaults[^1]);
  }

  [Theory]
  [InlineData("Acme", "Acme")]
  [InlineData("Exactly twenty-four char", "Exactly twenty-four char")]
  [InlineData("Contoso International Holdings", "Contoso International H…")]
  [InlineData("Contoso International  Holdings", "Contoso International…")]
  public void Truncate_ShortensLongOrganisationNames(string name, string expected)
  {
    Assert.Equal(expected, SearchFilterCatalog.Truncate(name));
  }

  [Fact]
  public void Resolve_KnownId_ReturnsEntry()
  {
    var entries = SearchFilterCatalog.Build(None);
    Assert.Equal("Favourites", SearchFilterCatalog.Resolve(entries, "is:fav").Name);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("folder:deleted")]
  public void Resolve_MissingId_FallsBackToAllItems(string? id)
  {
    var entries = SearchFilterCatalog.Build(None);
    Assert.Same(SearchFilterCatalog.All, SearchFilterCatalog.Resolve(entries, id));
  }

  [Fact]
  public void Build_SameInputsGiveEqualEntries()
  {
    var organisations = new Dictionary<string, string> { ["1"] = "Alpha" };
    Assert.True(SearchFilterCatalog.Build(organisations).SequenceEqual(SearchFilterCatalog.Build(organisations)));
  }

  [Fact]
  public void SearchCached_NoExtraFilter_ReturnsAll()
  {
    Assert.Equal(3, CreateService().SearchCached().Count);
  }

  [Fact]
  public void SearchCached_ExtraFilter_NarrowsResults()
  {
    var result = CreateService().SearchCached(extraFilter: ("is", "fav"));
    Assert.Equal(["a", "b"], result.Select(i => i.Id).Order());
  }

  [Fact]
  public void SearchCached_ExtraFilterCombinesWithTypedFilter()
  {
    var result = CreateService().SearchCached("folder:Work", extraFilter: ("is", "fav"));
    Assert.Equal(["a"], result.Select(i => i.Id));
  }

  [Fact]
  public void SearchCached_ExtraFilterCombinesWithTypedText()
  {
    var result = CreateService().SearchCached("beta", extraFilter: ("type", "login"));
    Assert.Equal(["b"], result.Select(i => i.Id));
    Assert.Empty(CreateService().SearchCached("beta", extraFilter: ("type", "card")));
  }

  [Fact]
  public void ApplyFilter_OrgNone_ReturnsItemsWithoutOrganisation()
  {
    var svc = CreateService();
    var result = svc.SearchCached(extraFilter: ("org", "none"));
    Assert.Equal(["b", "c"], result.Select(i => i.Id).Order());
  }

  [Fact]
  public void ApplyFilter_OrgById_ReturnsOrganisationItems()
  {
    var result = CreateService().SearchCached(extraFilter: ("org", "org-1"));
    Assert.Equal(["a"], result.Select(i => i.Id));
  }

  [Fact]
  public void ApplyFilter_FolderNone_ReturnsItemsWithoutFolder()
  {
    var result = CreateService().SearchCached(extraFilter: ("folder", "none"));
    Assert.Equal(["b"], result.Select(i => i.Id));
  }

  [Fact]
  public void ApplyFilter_FolderById_ReturnsFolderItems()
  {
    var result = CreateService().SearchCached(extraFilter: ("folder", "f-home"));
    Assert.Equal(["c"], result.Select(i => i.Id));
  }
}
