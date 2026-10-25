using System;
using System.Runtime.InteropServices;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ListPager(int Total, int PageSize, int Shown)
{
  internal static ListPager First(int total, int pageSize, int atLeast = 0) =>
    new(total, pageSize, Math.Min(total, Math.Max(pageSize, atLeast)));

  internal bool HasMore => Shown < Total;

  internal int Page => (Shown + PageSize - 1) / PageSize;

  internal int PageCount => (Total + PageSize - 1) / PageSize;

  internal ListPager Next() => this with { Shown = Math.Min(Total, Shown + PageSize) };
}
