using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MemoryCapture;

public static class WindowCapture
{
    public static async Task<BitmapSource> CaptureAsync(nint window)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new InvalidOperationException("Window capture is unavailable on this device. Try region capture.");
        if (window == 0 || Native.IsIconic(window)) throw new InvalidOperationException("Restore the window before capturing it.");
        var item = CreateItem(window);
        if (item.Size.Width <= 0 || item.Size.Height <= 0 || (long)item.Size.Width * item.Size.Height * 4 > 256L * 1024 * 1024)
            throw new InvalidOperationException("The window is unavailable or too large to capture.");
        using var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void FrameArrived(Direct3D11CaptureFramePool sender, object args) => arrived.TrySetResult(true);
        pool.FrameArrived += FrameArrived;
        try
        {
            session.StartCapture();
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var frame = pool.TryGetNextFrame() ?? throw new InvalidOperationException("The window did not provide an image. Try region capture.");
            using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
            using var converted = SoftwareBitmap.Convert(software, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
            var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(converted.PixelWidth * converted.PixelHeight * 4)));
            converted.CopyToBuffer(buffer);
            byte[] pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
            var bitmap = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, checked(converted.PixelWidth * 4));
            bitmap.Freeze();
            return bitmap;
        }
        catch (TimeoutException) { throw new InvalidOperationException("The window did not respond. It may block screenshots. Try region capture."); }
        finally { pool.FrameArrived -= FrameArrived; }
    }

    private static GraphicsCaptureItem CreateItem(nint window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var className));
        nint factory = 0, item = 0;
        try
        {
            var interopId = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref interopId, out factory));
            var vtable = Marshal.ReadIntPtr(factory);
            var create = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(vtable, 3 * nint.Size));
            var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            Marshal.ThrowExceptionForHR(create(factory, window, ref itemId, out item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(className);
        }
    }
    private static IDirect3DDevice CreateDevice()
    {
        nint native = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            int hr = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out native, out _, out context);
            if (hr < 0) hr = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out native, out _, out context);
            Marshal.ThrowExceptionForHR(hr);
            var id = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in id, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            foreach (var pointer in new[] { inspectable, dxgi, context, native }) if (pointer != 0) Marshal.Release(pointer);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateForWindow(nint self, nint window, ref Guid iid, out nint result);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, int length, out nint result);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int driver, nint software, uint flags, nint levels, uint count, uint version, out nint device, out int level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
}
