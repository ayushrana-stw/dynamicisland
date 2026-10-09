using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DynamicIsland.App.Platform.Windows.Teams;

// Minimal UI Automation COM interfaces (source-generated COM, trim/AOT safe).
// Only the vtable slots up to the last method used are declared; the _SlotN methods are
// placeholders that keep the vtable order and are never called.

[GeneratedComInterface]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
internal partial interface IUIAutomation
{
    [PreserveSig] int _Slot0();  // CompareElements
    [PreserveSig] int _Slot1();  // CompareRuntimeIds
    [PreserveSig] int _Slot2();  // GetRootElement
    [PreserveSig] int ElementFromHandle(nint hwnd, out IUIAutomationElement? element);
    [PreserveSig] int _Slot4();  // ElementFromPoint
    [PreserveSig] int _Slot5();  // GetFocusedElement
    [PreserveSig] int _Slot6();  // GetRootElementBuildCache
    [PreserveSig] int _Slot7();  // ElementFromHandleBuildCache
    [PreserveSig] int _Slot8();  // ElementFromPointBuildCache
    [PreserveSig] int _Slot9();  // GetFocusedElementBuildCache
    [PreserveSig] int _Slot10(); // CreateTreeWalker
    [PreserveSig] int _Slot11(); // ControlViewWalker
    [PreserveSig] int _Slot12(); // ContentViewWalker
    [PreserveSig] int _Slot13(); // RawViewWalker
    [PreserveSig] int _Slot14(); // RawViewCondition
    [PreserveSig] int _Slot15(); // ControlViewCondition
    [PreserveSig] int _Slot16(); // ContentViewCondition
    [PreserveSig] int _Slot17(); // CreateCacheRequest
    [PreserveSig] int CreateTrueCondition(out IUIAutomationCondition? condition);
}

[GeneratedComInterface]
[Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
internal partial interface IUIAutomationCondition;

[GeneratedComInterface]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
internal partial interface IUIAutomationElement
{
    [PreserveSig] int _Slot0();  // SetFocus
    [PreserveSig] int _Slot1();  // GetRuntimeId
    [PreserveSig] int _Slot2();  // FindFirst
    [PreserveSig] int FindAll(int scope, IUIAutomationCondition condition, out IUIAutomationElementArray? found);
    [PreserveSig] int _Slot4();  // FindFirstBuildCache
    [PreserveSig] int _Slot5();  // FindAllBuildCache
    [PreserveSig] int _Slot6();  // BuildUpdatedCache
    [PreserveSig] int _Slot7();  // GetCurrentPropertyValue
    [PreserveSig] int _Slot8();  // GetCurrentPropertyValueEx
    [PreserveSig] int _Slot9();  // GetCachedPropertyValue
    [PreserveSig] int _Slot10(); // GetCachedPropertyValueEx
    [PreserveSig] int _Slot11(); // GetCurrentPatternAs
    [PreserveSig] int _Slot12(); // GetCachedPatternAs
    [PreserveSig] int _Slot13(); // GetCurrentPattern
    [PreserveSig] int _Slot14(); // GetCachedPattern
    [PreserveSig] int _Slot15(); // GetCachedParent
    [PreserveSig] int _Slot16(); // GetCachedChildren
    [PreserveSig] int _Slot17(); // CurrentProcessId
    [PreserveSig] int get_CurrentControlType(out int controlType);
    [PreserveSig] int _Slot19(); // CurrentLocalizedControlType
    [PreserveSig] int get_CurrentName([MarshalAs(UnmanagedType.BStr)] out string? name);
}

[GeneratedComInterface]
[Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
internal partial interface IUIAutomationElementArray
{
    [PreserveSig] int get_Length(out int length);
    [PreserveSig] int GetElement(int index, out IUIAutomationElement? element);
}

internal static class UiaControlType
{
    public const int Button = 50000;
    public const int Edit = 50004;
    public const int Text = 50020;
    public const int Group = 50026;
}
