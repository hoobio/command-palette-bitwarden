using HoobiBitwardenCommandPaletteExtension.Helpers;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class ListPagerTests
{
  [Fact]
  public void First_ShowsOnePage_WhenMoreResultsExist()
  {
    var pager = ListPager.First(100, 40);

    Assert.Equal(40, pager.Shown);
    Assert.True(pager.HasMore);
    Assert.Equal(1, pager.Page);
    Assert.Equal(3, pager.PageCount);
  }

  [Fact]
  public void First_ShowsAllResults_WhenFewerThanOnePage()
  {
    var pager = ListPager.First(10, 40);

    Assert.Equal(10, pager.Shown);
    Assert.False(pager.HasMore);
    Assert.Equal(1, pager.Page);
    Assert.Equal(1, pager.PageCount);
  }

  [Fact]
  public void First_ShowsNothing_ForNoResults()
  {
    var pager = ListPager.First(0, 40);

    Assert.Equal(0, pager.Shown);
    Assert.False(pager.HasMore);
    Assert.Equal(0, pager.PageCount);
  }

  [Fact]
  public void Next_AddsOnePage()
  {
    var pager = ListPager.First(100, 40).Next();

    Assert.Equal(80, pager.Shown);
    Assert.True(pager.HasMore);
    Assert.Equal(2, pager.Page);
  }

  [Fact]
  public void Next_StopsAtTheLastPartialPage()
  {
    var pager = ListPager.First(100, 40).Next().Next();

    Assert.Equal(100, pager.Shown);
    Assert.False(pager.HasMore);
    Assert.Equal(3, pager.Page);
    Assert.Equal(pager, pager.Next());
  }

  [Fact]
  public void First_ResetsToOnePage_AfterPagingOn()
  {
    var paged = ListPager.First(100, 40).Next();

    var reset = ListPager.First(paged.Total, paged.PageSize);

    Assert.Equal(40, reset.Shown);
  }

  [Theory]
  [InlineData(80, 80)]
  [InlineData(10, 40)]
  [InlineData(500, 100)]
  public void First_KeepsAtLeastTheRequestedCount_WithinTheTotal(int atLeast, int expected)
  {
    Assert.Equal(expected, ListPager.First(100, 40, atLeast).Shown);
  }
}
