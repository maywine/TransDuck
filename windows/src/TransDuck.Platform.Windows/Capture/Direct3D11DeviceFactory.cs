using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace TransDuck.Platform.Windows.Capture;

/// <summary>
/// Creates the Direct3D11 device required by Windows.Graphics.Capture frame pools.
/// </summary>
internal static class Direct3D11DeviceFactory
{
    private const uint D3d11SdkVersion = 7;
    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    private static readonly Guid DxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public static IDirect3DDevice Create()
    {
        try
        {
            return Create(D3dDriverType.Hardware);
        }
        catch (COMException)
        {
            return Create(D3dDriverType.Warp);
        }
    }

    private static IDirect3DDevice Create(D3dDriverType driverType)
    {
        var result = D3D11CreateDevice(
            IntPtr.Zero,
            driverType,
            IntPtr.Zero,
            D3d11CreateDeviceBgraSupport,
            IntPtr.Zero,
            0,
            D3d11SdkVersion,
            out var nativeDevice,
            out _,
            out var immediateContext);
        if (immediateContext != IntPtr.Zero)
        {
            Marshal.Release(immediateContext);
        }

        Marshal.ThrowExceptionForHR(result);
        try
        {
            var iid = DxgiDeviceGuid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(nativeDevice, in iid, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(nativeDevice);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        D3dDriverType driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out uint featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);
}

internal enum D3dDriverType
{
    Hardware = 1,
    Warp = 5,
}
