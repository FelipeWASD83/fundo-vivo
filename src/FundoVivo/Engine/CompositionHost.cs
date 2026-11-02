using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using WinRT;

namespace FundoVivo.Engine;

/// <summary>
/// One compositor for the whole engine, living on the UI thread. Video goes straight from the hardware decoder to
/// a composition surface; images are uploaded once into a drawing surface. No frame ever passes through the CPU.
/// </summary>
public sealed unsafe class CompositionHost : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DispatcherQueueOptions o, out IntPtr controller);
    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint numLevels,
        uint sdkVersion, out IntPtr device, out int level, out IntPtr context);

    static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    static readonly Guid IID_ICompositorInterop = new("25297D5C-3AD4-4C9C-B5CF-E36A38512330");
    static readonly Guid IID_ICompositionDrawingSurfaceInterop = new("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE");
    static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public DispatcherQueue Queue { get; }
    public Compositor Compositor { get; }
    readonly object _controller;
    IntPtr _d3dDevice, _d3dContext;
    CompositionGraphicsDevice _graphics;

    public CompositionHost()
    {
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new DispatcherQueueOptions { dwSize = 12, threadType = 2, apartmentType = 2 }, out IntPtr ctrl));
        var controller = DispatcherQueueController.FromAbi(ctrl);
        _controller = controller;
        Queue = controller.DispatcherQueue;
        SynchronizationContext.SetSynchronizationContext(new QueueSyncContext(Queue));
        Compositor = new Compositor();
    }

    public Windows.UI.Composition.Desktop.DesktopWindowTarget CreateTarget(IntPtr hwnd)
    {
        IntPtr interop = QueryInterface(Compositor, IID_ICompositorDesktopInterop);
        try
        {
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)(*(IntPtr**)interop)[3];
            IntPtr target;
            Marshal.ThrowExceptionForHR(create(interop, hwnd, 0, &target));
            // FromAbi takes its own reference, so ours is released here.
            var result = Windows.UI.Composition.Desktop.DesktopWindowTarget.FromAbi(target);
            Marshal.Release(target);
            return result;
        }
        finally { Marshal.Release(interop); }
    }

    /// <summary>Uploads BGRA premultiplied pixels into a new drawing surface. The D3D device is only created the first time.</summary>
    public CompositionDrawingSurface CreateImageSurface(int width, int height, byte[] pixels)
    {
        EnsureGraphics();
        var surface = _graphics.CreateDrawingSurface(new Windows.Foundation.Size(width, height),
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Windows.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
        IntPtr interop = QueryInterface(surface, IID_ICompositionDrawingSurfaceInterop);
        try
        {
            var vtbl = *(IntPtr**)interop;
            var beginDraw = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, Native.POINT*, int>)vtbl[3];
            var endDraw = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[4];
            Guid iid = IID_ID3D11Texture2D;
            IntPtr texture;
            Native.POINT offset;
            Marshal.ThrowExceptionForHR(beginDraw(interop, IntPtr.Zero, &iid, &texture, &offset));
            try
            {
                // ID3D11DeviceContext::UpdateSubresource is slot 48 of the vtable.
                var update = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, D3DBox*, void*, uint, uint, void>)(*(IntPtr**)_d3dContext)[48];
                var box = new D3DBox { left = (uint)offset.X, top = (uint)offset.Y, front = 0, right = (uint)(offset.X + width), bottom = (uint)(offset.Y + height), back = 1 };
                fixed (byte* p = pixels) update(_d3dContext, texture, 0, &box, p, (uint)(width * 4), 0);
            }
            finally
            {
                Marshal.Release(texture);
                Marshal.ThrowExceptionForHR(endDraw(interop));
            }
        }
        finally { Marshal.Release(interop); }
        return surface;
    }

    [StructLayout(LayoutKind.Sequential)] struct D3DBox { public uint left, top, front, right, bottom, back; }

    void EnsureGraphics()
    {
        if (_graphics != null) return;
        const uint BGRA_SUPPORT = 0x20;
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, BGRA_SUPPORT, IntPtr.Zero, 0, 7, out _d3dDevice, out _, out _d3dContext));
        IntPtr interop = QueryInterface(Compositor, IID_ICompositorInterop);
        try
        {
            var createGraphics = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)(*(IntPtr**)interop)[5];
            IntPtr graphics;
            Marshal.ThrowExceptionForHR(createGraphics(interop, _d3dDevice, &graphics));
            _graphics = CompositionGraphicsDevice.FromAbi(graphics);
            Marshal.Release(graphics);
        }
        finally { Marshal.Release(interop); }
    }

    static IntPtr QueryInterface(object winrtObject, Guid iid)
    {
        IntPtr unknown = ((IWinRTObject)winrtObject).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out IntPtr result));
        return result;
    }

    public void Dispose()
    {
        _graphics?.Dispose();
        if (_d3dContext != IntPtr.Zero) Marshal.Release(_d3dContext);
        if (_d3dDevice != IntPtr.Zero) Marshal.Release(_d3dDevice);
        Compositor.Dispose();
        GC.KeepAlive(_controller);
    }

    sealed class QueueSyncContext : SynchronizationContext
    {
        readonly DispatcherQueue _queue;
        public QueueSyncContext(DispatcherQueue q) { _queue = q; }
        public override void Post(SendOrPostCallback d, object state) => _queue.TryEnqueue(() => d(state));
        public override void Send(SendOrPostCallback d, object state)
        {
            if (_queue.HasThreadAccess) { d(state); return; }
            using var done = new ManualResetEventSlim();
            _queue.TryEnqueue(() => { try { d(state); } finally { done.Set(); } });
            done.Wait();
        }
        public override SynchronizationContext CreateCopy() => this;
    }
}
