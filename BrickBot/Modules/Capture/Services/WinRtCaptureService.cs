using System.Runtime.InteropServices;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace BrickBot.Modules.Capture.Services;

/// <summary>
/// Primary capture path. <see cref="GraphicsCaptureSession"/> reads the target window's
/// framebuffer directly (DWM-side), so the result is the actual rendered window content
/// regardless of occluding windows. Works for GPU-composited apps, DirectX-windowed games,
/// browsers, and any app that doesn't explicitly opt out of capture.
///
/// Falls back to <see cref="BitBltCaptureService"/> when:
///   - The OS doesn't support GraphicsCapture (Windows &lt; 10 1809).
///   - The target opts out via <c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c>.
///   - WinRT init fails for any reason.
///
/// Threading: uses a per-instance <c>SemaphoreSlim</c> + cached single-window session so
/// repeated Grab() calls reuse the framepool. Any handle change tears down + rebuilds.
/// </summary>
public sealed class WinRtCaptureService : ICaptureService, IDisposable
{
    private const int FrameWaitMs = 250;

    private readonly BitBltCaptureService _fallback;
    private readonly ILogger<WinRtCaptureService> _logger;
    private readonly bool _isSupported;
    private readonly object _lock = new();

    private long _frameCounter;
    private nint _activeHandle;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;
    private nint _d3dDevicePtr;
    private nint _d3dContextPtr;
    /// <summary>Cached staging texture — reused across Grab() calls. Reallocated only when
    /// the captured window's resolution changes (rare). Saves ~3-5ms per frame vs allocating
    /// a fresh GPU texture every call.</summary>
    private nint _stagingPtr;
    private int _stagingWidth;
    private int _stagingHeight;
    private bool _disposed;
    /// <summary>True once a WinRT capture has succeeded — flips the path back on after a transient
    /// failure caused a GDI fallback.</summary>
    private bool _winrtKnownGood;

    public WinRtCaptureService(BitBltCaptureService fallback, ILogger<WinRtCaptureService> logger)
    {
        _fallback = fallback;
        _logger = logger;
        _isSupported = TryIsSupported();
        if (!_isSupported)
        {
            _logger.LogWarning("GraphicsCaptureSession.IsSupported() returned false — falling back to GDI capture for the entire session.");
        }
    }

    public CaptureFrame Grab(nint windowHandle)
    {
        if (!_isSupported)
        {
            return _fallback.Grab(windowHandle);
        }

        if (windowHandle == nint.Zero || !Native.IsWindow(windowHandle))
        {
            throw new OperationException("CAPTURE_INVALID_WINDOW");
        }

        try
        {
            lock (_lock)
            {
                EnsureSession(windowHandle);
                var frame = GrabInternal();
                _winrtKnownGood = true;
                return frame;
            }
        }
        catch (OperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // WinRT failed (window opt-out via WDA_EXCLUDEFROMCAPTURE, transient COM failure,
            // window resize mid-capture, etc.). Surface the reason — silent fallback to GDI
            // makes "wrong content" symptoms impossible to diagnose. The fallback frame can
            // include occluding windows since GDI can only see what's on screen.
            _logger.LogWarning(ex, "WinRT capture failed for handle 0x{Handle:X} ({Reason}); falling back to GDI screen capture.",
                windowHandle, ex.GetType().Name);
            TearDownSession();
            return _fallback.Grab(windowHandle);
        }
    }

    private CaptureFrame GrabInternal()
    {
        // Drain ALL queued frames and keep only the latest. The framepool buffers at the
        // window's render rate (~60 Hz); if we just take TryGetNextFrame()[0] the caller
        // sees a stale image from N ticks ago. After draining, if nothing was queued, wait
        // briefly for a fresh one.
        Direct3D11CaptureFrame? frame = null;
        Direct3D11CaptureFrame? next;
        while ((next = _pool!.TryGetNextFrame()) is not null)
        {
            frame?.Dispose();
            frame = next;
        }
        if (frame is null)
        {
            var deadline = Environment.TickCount64 + FrameWaitMs;
            while (Environment.TickCount64 < deadline)
            {
                frame = _pool!.TryGetNextFrame();
                if (frame is not null) break;
                Thread.Sleep(1);
            }
        }
        if (frame is null)
        {
            throw new OperationException("CAPTURE_FRAME_TIMEOUT", null,
                $"WinRT capture timed out after {FrameWaitMs}ms waiting for a frame.");
        }

        try
        {
            using var sourceTex = GetD3DTextureFromSurface(frame.Surface);
            var (width, height) = GetTextureSize(sourceTex.Pointer);
            var stagingPtr = AcquireStagingTexture(width, height);
            CopyResource(stagingPtr, sourceTex.Pointer);
            var mat = MapAndCopyToMat(stagingPtr, width, height);
            var frameNumber = Interlocked.Increment(ref _frameCounter);
            return new CaptureFrame(mat, frameNumber, DateTimeOffset.UtcNow);
        }
        finally
        {
            frame.Dispose();
        }
    }

    /// <summary>Returns a staging texture sized to <paramref name="width"/>×<paramref name="height"/>,
    /// reusing the cached one when dimensions match. Reallocates if the window was resized.</summary>
    private nint AcquireStagingTexture(int width, int height)
    {
        if (_stagingPtr != nint.Zero && _stagingWidth == width && _stagingHeight == height)
            return _stagingPtr;

        if (_stagingPtr != nint.Zero)
        {
            Marshal.Release(_stagingPtr);
            _stagingPtr = nint.Zero;
        }

        _stagingPtr = CreateStagingTexture(width, height);
        _stagingWidth = width;
        _stagingHeight = height;
        return _stagingPtr;
    }

    private void EnsureSession(nint windowHandle)
    {
        if (_session is not null && _activeHandle == windowHandle) return;
        TearDownSession();

        // 1. D3D11 device + DXGI → IDirect3DDevice.
        InitD3DDevice();

        // 2. GraphicsCaptureItem from HWND via the COM interop interface. CsWinRT doesn't
        //    project IGraphicsCaptureItemInterop, so we resolve the activation factory via
        //    RoGetActivationFactory (with the interop IID, which returns the QI'd interface
        //    directly), then call CreateForWindow.
        //
        // Hardcode IID_IGraphicsCaptureItem rather than relying on typeof(GraphicsCaptureItem).GUID:
        // CsWinRT projects WinRT runtime classes as plain C# classes without a [Guid] attribute,
        // so reflection returns Guid.Empty, and CreateForWindow rejects it with E_NOINTERFACE.
        var itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
        var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Native.WindowsCreateString(runtimeClass, runtimeClass.Length, out var classNameHstring);
        IGraphicsCaptureItemInterop interop;
        nint interopFactoryPtr;
        try
        {
            Native.RoGetActivationFactory(classNameHstring, interopIid, out interopFactoryPtr);
        }
        finally
        {
            Native.WindowsDeleteString(classNameHstring);
        }
        try
        {
            interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(interopFactoryPtr);
        }
        finally
        {
            Marshal.Release(interopFactoryPtr);
        }
        var createHr = interop.CreateForWindow(windowHandle, in itemGuid, out var itemPtr);
        if (createHr < 0 || itemPtr == nint.Zero)
        {
            ThrowD3D("CreateForWindow", createHr);
        }
        try
        {
            _item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }

        // 3. Frame pool sized to the current window dimensions, BGRA8 so we can hand the
        //    bytes straight to OpenCV. PixelFormat.B8G8R8A8UIntNormalized is what nearly
        //    every game backbuffer uses.
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _item.Size);

        _session = _pool.CreateCaptureSession(_item);

        // Strip the yellow capture border + cursor for cleaner template matching.
        TrySetBool(_session, "IsBorderRequired", false);
        TrySetBool(_session, "IsCursorCaptureEnabled", false);

        _session.StartCapture();
        _activeHandle = windowHandle;
    }

    private static void TrySetBool(GraphicsCaptureSession session, string name, bool value)
    {
        // These properties only exist on Windows 11 / recent Win10 builds; reflection-set
        // so we degrade gracefully on older OSes instead of throwing MissingMethodException.
        try
        {
            var prop = session.GetType().GetProperty(name);
            prop?.SetValue(session, value);
        }
        catch
        {
            // Older OS; not fatal.
        }
    }

    private void InitD3DDevice()
    {
        // Hardware driver, BGRA support so it interops with the framepool's pixel format.
        // First try with the default driver (HARDWARE). If that fails (no GPU / sandboxed
        // session / driver issue) retry with WARP (software D3D11 implementation) which
        // works in any environment that can run d3d11.dll.
        var hr = Native.D3D11CreateDevice(
            nint.Zero, Native.D3D_DRIVER_TYPE_HARDWARE, nint.Zero,
            Native.D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0,
            Native.D3D11_SDK_VERSION,
            out _d3dDevicePtr, out _, out _d3dContextPtr);
        if (hr < 0 || _d3dDevicePtr == nint.Zero)
        {
            _logger.LogWarning("D3D11CreateDevice(HARDWARE) failed 0x{Hr:X8}; retrying with WARP.", hr);
            hr = Native.D3D11CreateDevice(
                nint.Zero, Native.D3D_DRIVER_TYPE_WARP, nint.Zero,
                Native.D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0,
                Native.D3D11_SDK_VERSION,
                out _d3dDevicePtr, out _, out _d3dContextPtr);
        }
        if (hr < 0 || _d3dDevicePtr == nint.Zero)
        {
            ThrowD3D("D3D11CreateDevice", hr);
        }

        // QI the device for IDXGIDevice → wrap as WinRT IDirect3DDevice.
        var dxgiGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        var dxgiHr = Marshal.QueryInterface(_d3dDevicePtr, ref dxgiGuid, out var dxgiDevicePtr);
        if (dxgiHr < 0)
        {
            ThrowD3D("QueryInterface IDXGIDevice", dxgiHr);
        }
        try
        {
            var d3dHr = Native.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out var graphicsDevicePtr);
            if (d3dHr < 0)
            {
                ThrowD3D("CreateDirect3D11DeviceFromDXGIDevice", d3dHr);
            }
            try
            {
                _device = MarshalInspectable<IDirect3DDevice>.FromAbi(graphicsDevicePtr);
            }
            finally
            {
                Marshal.Release(graphicsDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(dxgiDevicePtr);
        }
    }

    private void ThrowD3D(string step, int hr)
    {
        var detail = $"0x{hr:X8} ({step})";
        var fallbackMessage = $"WinRT/D3D11 capture init failed: {detail}";
        _logger.LogError("WinRT capture init failed at {Step} hr={Hr:X8}", step, hr);
        // Pass the formatted message as the third arg so callers that only see Exception.Message
        // (i.e. the frontend before i18n resolves) still get the step + HRESULT.
        throw new OperationException("CAPTURE_D3D_INIT_FAILED",
            new() { ["hr"] = detail }, fallbackMessage);
    }

    /// <summary>
    /// QI the IDirect3DSurface coming out of WinRT for the underlying ID3D11Texture2D.
    /// We can't use a managed cast to <see cref="IDirect3DDxgiInterfaceAccess"/> — CsWinRT's
    /// IInspectable RCW doesn't recognize our locally-declared ComImport interface, so the
    /// cast throws InvalidCastException even though COM identity supports the interface.
    /// Drop to raw COM: get the ABI pointer, QI for the interop IID, call GetInterface
    /// through the vtable. Slot 3 (right after IUnknown 0/1/2 — interop has only one method).
    /// </summary>
    private SafeComHandle GetD3DTextureFromSurface(IDirect3DSurface surface)
    {
        var surfacePtr = MarshalInspectable<IDirect3DSurface>.FromManaged(surface);
        if (surfacePtr == nint.Zero)
        {
            ThrowD3D("MarshalInspectable(IDirect3DSurface)", unchecked((int)0x80004003)); // E_POINTER
        }
        try
        {
            var accessIid = typeof(IDirect3DDxgiInterfaceAccess).GUID;
            var qiHr = Marshal.QueryInterface(surfacePtr, ref accessIid, out var accessPtr);
            if (qiHr < 0 || accessPtr == nint.Zero)
            {
                ThrowD3D("QI IDirect3DDxgiInterfaceAccess", qiHr);
            }
            try
            {
                var texIid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); // ID3D11Texture2D
                unsafe
                {
                    var vtable = *(nint**)accessPtr;
                    var getInterfaceFn = (delegate* unmanaged[Stdcall]<nint, in Guid, out nint, int>)vtable[3];
                    var hr = getInterfaceFn(accessPtr, in texIid, out var texPtr);
                    if (hr < 0 || texPtr == nint.Zero)
                    {
                        ThrowD3D("GetInterface(ID3D11Texture2D)", hr);
                    }
                    return new SafeComHandle(texPtr);
                }
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }
        finally
        {
            Marshal.Release(surfacePtr);
        }
    }

    private static (int width, int height) GetTextureSize(nint texturePtr)
    {
        // Avoid pulling in a full ID3D11Texture2D wrapper — call GetDesc through the
        // vtable directly. ID3D11Texture2D::GetDesc is at vtable slot 9 (0-based: 0-2 IUnknown,
        // 3-7 ID3D11DeviceChild::GetDevice + privatedata, 8 ID3D11Resource::GetType,
        // 9 ID3D11Texture2D::GetDesc).
        unsafe
        {
            var vtable = *(nint**)texturePtr;
            // Slot indexing varies by SDK header revision; use the canonical layout where
            // GetDesc is the FIRST method past ID3D11Resource (slot 10 in some headers).
            // Safer: call GetType (slot 7) to confirm 2D then GetDesc (slot 10).
            var getDescFn = (delegate* unmanaged[Stdcall]<nint, out D3D11_TEXTURE2D_DESC, void>)vtable[10];
            getDescFn(texturePtr, out var desc);
            return ((int)desc.Width, (int)desc.Height);
        }
    }

    private nint CreateStagingTexture(int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
            SampleDescCount = 1,
            SampleDescQuality = 0,
            Usage = 3, // D3D11_USAGE_STAGING
            BindFlags = 0,
            CPUAccessFlags = 0x20000, // D3D11_CPU_ACCESS_READ
            MiscFlags = 0,
        };

        unsafe
        {
            var vtable = *(nint**)_d3dDevicePtr;
            // ID3D11Device::CreateTexture2D is slot 5.
            var createFn = (delegate* unmanaged[Stdcall]<nint, in D3D11_TEXTURE2D_DESC, nint, out nint, int>)vtable[5];
            var hr = createFn(_d3dDevicePtr, in desc, nint.Zero, out var texPtr);
            if (hr < 0 || texPtr == nint.Zero)
            {
                ThrowD3D("CreateTexture2D", hr);
            }
            return texPtr;
        }
    }

    private void CopyResource(nint dst, nint src)
    {
        unsafe
        {
            var vtable = *(nint**)_d3dContextPtr;
            // ID3D11DeviceContext::CopyResource is slot 47.
            var copyFn = (delegate* unmanaged[Stdcall]<nint, nint, nint, void>)vtable[47];
            copyFn(_d3dContextPtr, dst, src);
        }
    }

    private Mat MapAndCopyToMat(nint stagingPtr, int width, int height)
    {
        unsafe
        {
            var ctxVtable = *(nint**)_d3dContextPtr;
            // ID3D11DeviceContext::Map is slot 14, Unmap is slot 15.
            var mapFn = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, out D3D11_MAPPED_SUBRESOURCE, int>)ctxVtable[14];
            var unmapFn = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)ctxVtable[15];

            const uint MAP_READ = 1;
            var hr = mapFn(_d3dContextPtr, stagingPtr, 0, MAP_READ, 0, out var mapped);
            if (hr < 0)
            {
                ThrowD3D("Map", hr);
            }
            try
            {
                // Wrap the mapped GPU buffer as a 4-channel Mat WITHOUT copying — the Mat
                // shares storage with the staging texture for the duration of this call.
                // Then run native OpenCV BGRA→BGR conversion (vectorized, ~10x faster than
                // a per-pixel C# loop). Clone() so the result owns its memory before Unmap.
                var bgraView = Mat.FromPixelData(height, width, MatType.CV_8UC4,
                    (nint)mapped.Data, (long)mapped.RowPitch);
                var bgr = new Mat();
                Cv2.CvtColor(bgraView, bgr, ColorConversionCodes.BGRA2BGR);
                bgraView.Dispose();
                return bgr;
            }
            finally
            {
                unmapFn(_d3dContextPtr, stagingPtr, 0);
            }
        }
    }

    private void TearDownSession()
    {
        try { _session?.Dispose(); } catch { /* ignore */ }
        try { _pool?.Dispose(); } catch { /* ignore */ }
        _session = null;
        _pool = null;
        _item = null;
        _activeHandle = nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TearDownSession();
        if (_stagingPtr != nint.Zero) { Marshal.Release(_stagingPtr); _stagingPtr = nint.Zero; }
        if (_d3dContextPtr != nint.Zero) { Marshal.Release(_d3dContextPtr); _d3dContextPtr = nint.Zero; }
        if (_d3dDevicePtr != nint.Zero) { Marshal.Release(_d3dDevicePtr); _d3dDevicePtr = nint.Zero; }
    }

    private static bool TryIsSupported()
    {
        try { return GraphicsCaptureSession.IsSupported(); }
        catch { return false; }
    }

    private static void ComRelease(nint p)
    {
        if (p != nint.Zero) Marshal.Release(p);
    }

    // ---------------- Native interop ----------------

    private static class Native
    {
        public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        public const uint D3D_DRIVER_TYPE_HARDWARE = 1;
        /// <summary>Software D3D11 implementation; works in any environment that can load d3d11.dll.</summary>
        public const uint D3D_DRIVER_TYPE_WARP = 5;
        public const uint D3D11_SDK_VERSION = 7;

        [DllImport("user32.dll")]
        public static extern bool IsWindow(nint hWnd);

        [DllImport("d3d11.dll")]
        public static extern int D3D11CreateDevice(
            nint adapter,
            uint driverType,
            nint software,
            uint flags,
            uint[]? featureLevels,
            uint featureLevelsCount,
            uint sdkVersion,
            out nint device,
            out uint featureLevel,
            out nint context);

        [DllImport("d3d11.dll")]
        public static extern int CreateDirect3D11DeviceFromDXGIDevice(
            nint dxgiDevice,
            out nint graphicsDevice);

        [DllImport("combase.dll", PreserveSig = false)]
        public static extern void WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length,
            out nint hstring);

        [DllImport("combase.dll", PreserveSig = false)]
        public static extern void WindowsDeleteString(nint hstring);

        [DllImport("combase.dll", PreserveSig = false)]
        public static extern void RoGetActivationFactory(
            nint activatableClassId,
            in Guid iid,
            out nint factory);
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        // PreserveSig is critical — default ComImport semantics would treat the HRESULT
        // as a thrown exception and swallow the out-pointer. Both interop methods here
        // return HRESULT and write the result via the trailing out param.
        [PreserveSig]
        int CreateForWindow(nint window, in Guid iid, out nint result);
        [PreserveSig]
        int CreateForMonitor(nint monitor, in Guid iid, out nint result);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(in Guid iid, out nint ppv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleDescCount;
        public uint SampleDescQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    private sealed class SafeComHandle : IDisposable
    {
        public nint Pointer { get; private set; }
        public SafeComHandle(nint p) { Pointer = p; }
        public void Dispose()
        {
            if (Pointer != nint.Zero) { Marshal.Release(Pointer); Pointer = nint.Zero; }
        }
    }
}
