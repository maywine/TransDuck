using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace TransDuck.Platform.Windows.Capture;

/// <summary>
/// Bridges the desktop-only IGraphicsCaptureItemInterop factory for monitor capture.
/// </summary>
internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForMonitor(IntPtr monitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var result = interop.CreateForMonitor(monitor, GraphicsCaptureItemGuid, out var itemPointer);
        Marshal.ThrowExceptionForHR(result);
        if (itemPointer == IntPtr.Zero)
        {
            throw new COMException("CreateForMonitor did not return a capture item.");
        }

        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, in Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, in Guid iid, out IntPtr result);
    }
}
