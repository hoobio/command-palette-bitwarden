using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static partial class UiaInterop
{
  internal const int UIA_ProcessIdPropertyId = 30002;
  internal const int UIA_ControlTypePropertyId = 30003;
  internal const int UIA_NamePropertyId = 30005;
  internal const int UIA_ValueValuePropertyId = 30045;
  internal const int UIA_ButtonControlTypeId = 50000;
  internal const int UIA_EditControlTypeId = 50004;
  internal const int UIA_InvokePatternId = 10000;
  internal const int TreeScope_Descendants = 4;

  private const ushort VT_I4 = 3;
  private const ushort VT_BSTR = 8;
  private const uint CLSCTX_INPROC_SERVER = 1;
  private static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
  private static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
  private static readonly StrategyBasedComWrappers ComWrappers = new();

  internal static IUIAutomation? CreateAutomation()
  {
    var hr = CoCreateInstance(in CLSID_CUIAutomation, 0, CLSCTX_INPROC_SERVER, in IID_IUIAutomation, out var ptr);
    if (hr != 0)
    {
      DebugLogService.Log("UIA", $"CoCreateInstance(CUIAutomation) failed: 0x{hr:X8}");
      return null;
    }
    return Wrap<IUIAutomation>(ptr);
  }

  // UniqueInstance is required: ComObject.FinalRelease is a no-op on cached wrappers.
  internal static T? Wrap<T>(nint ptr) where T : class
  {
    if (ptr == 0)
      return null;
    var obj = ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
    Marshal.Release(ptr);
    if (obj is T typed)
      return typed;
    Release(obj);
    return null;
  }

  internal static void Release(object? obj) => (obj as ComObject)?.FinalRelease();

  internal static void Release(nint ptr)
  {
    if (ptr != 0)
      Marshal.Release(ptr);
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct Variant
  {
    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value;
    public nint Value2;

    internal static Variant FromInt(int value) => new() { Vt = VT_I4, Value = value };

    internal static Variant FromBstr(nint bstr) => new() { Vt = VT_BSTR, Value = bstr };

    internal readonly string? AsString() => Vt == VT_BSTR && Value != 0 ? Marshal.PtrToStringBSTR(Value) : null;
  }

  [LibraryImport("ole32.dll")]
  private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

  [LibraryImport("oleaut32.dll")]
  internal static partial int VariantClear(ref Variant pvarg);
}

[GeneratedComInterface, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
internal partial interface IUIAutomation
{
  void _ReservedCompareElements();
  void _ReservedCompareRuntimeIds();
  void _ReservedGetRootElement();

  [PreserveSig]
  int ElementFromHandle(nint hwnd, out nint element);

  void _ReservedElementFromPoint();

  [PreserveSig]
  int GetFocusedElement(out nint element);

  void _ReservedGetRootElementBuildCache();
  void _ReservedElementFromHandleBuildCache();
  void _ReservedElementFromPointBuildCache();
  void _ReservedGetFocusedElementBuildCache();
  void _ReservedCreateTreeWalker();
  void _ReservedControlViewWalker();
  void _ReservedContentViewWalker();
  void _ReservedRawViewWalker();
  void _ReservedRawViewCondition();
  void _ReservedControlViewCondition();
  void _ReservedContentViewCondition();
  void _ReservedCreateCacheRequest();
  void _ReservedCreateTrueCondition();
  void _ReservedCreateFalseCondition();

  [PreserveSig]
  int CreatePropertyCondition(int propertyId, UiaInterop.Variant value, out nint condition);

  void _ReservedCreatePropertyConditionEx();

  [PreserveSig]
  int CreateAndCondition(nint condition1, nint condition2, out nint condition);
}

[GeneratedComInterface, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
internal partial interface IUIAutomationElement
{
  [PreserveSig]
  int SetFocus();

  void _ReservedGetRuntimeId();

  [PreserveSig]
  int FindFirst(int scope, nint condition, out nint found);

  void _ReservedFindAll();
  void _ReservedFindFirstBuildCache();
  void _ReservedFindAllBuildCache();
  void _ReservedBuildUpdatedCache();

  [PreserveSig]
  int GetCurrentPropertyValue(int propertyId, out UiaInterop.Variant value);

  void _ReservedGetCurrentPropertyValueEx();
  void _ReservedGetCachedPropertyValue();
  void _ReservedGetCachedPropertyValueEx();
  void _ReservedGetCurrentPatternAs();
  void _ReservedGetCachedPatternAs();

  [PreserveSig]
  int GetCurrentPattern(int patternId, out nint pattern);

  void _ReservedGetCachedPattern();
  void _ReservedGetCachedParent();
  void _ReservedGetCachedChildren();

  [PreserveSig]
  int get_CurrentProcessId(out int processId);

  void _ReservedCurrentControlType();

  void _ReservedCurrentLocalizedControlType();
  void _ReservedCurrentName();
  void _ReservedCurrentAcceleratorKey();
  void _ReservedCurrentAccessKey();
  void _ReservedCurrentHasKeyboardFocus();
  void _ReservedCurrentIsKeyboardFocusable();
  void _ReservedCurrentIsEnabled();
  void _ReservedCurrentAutomationId();
  void _ReservedCurrentClassName();
  void _ReservedCurrentHelpText();
  void _ReservedCurrentCulture();
  void _ReservedCurrentIsControlElement();
  void _ReservedCurrentIsContentElement();

  [PreserveSig]
  int get_CurrentIsPassword(out int isPassword);
}

[GeneratedComInterface, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059")]
internal partial interface IUIAutomationInvokePattern
{
  [PreserveSig]
  int Invoke();
}
