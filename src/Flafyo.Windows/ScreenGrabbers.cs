using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Flafyo.Core;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace Flafyo.Windows;

/// <summary>Normal capture (GDI). Works for windowed and most borderless games.</summary>
public sealed class GdiScreenGrabber : IScreenGrabber
{
    private long _id;
    public string? LastError { get; private set; }

    public bool TryGrab(System.Drawing.Rectangle r, out Frame frame)
    {
        frame = null!;
        try
        {
            if (r.Width < 8 || r.Height < 8) { LastError = "area too small"; return false; }
            long ticks = Stopwatch.GetTimestamp();
            using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);

            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var raw = new byte[Math.Abs(data.Stride) * r.Height];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                var gray = new byte[r.Width * r.Height];
                for (int y = 0; y < r.Height; y++)
                {
                    int src = y * data.Stride, dst = y * r.Width;
                    for (int x = 0; x < r.Width; x++, src += 4)
                        gray[dst + x] = (byte)((29 * raw[src] + 150 * raw[src + 1] + 77 * raw[src + 2]) >> 8);
                }
                frame = new Frame(++_id, 1, r.Width, r.Height, gray, ticks);
                LastError = null;
                return true;
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public void Dispose() { }
}

/// <summary>
/// Advanced capture (DXGI Desktop Duplication): reads what Windows actually shows, so it also works for most
/// fullscreen / borderless-fullscreen games where normal capture returns a black picture.
/// Returns false (without an error) when the screen has not changed.
/// </summary>
public sealed class DxgiScreenGrabber : IScreenGrabber
{
    private Device? _device;
    private OutputDuplication? _duplication;
    private Texture2D? _staging;
    private System.Drawing.Rectangle _bounds;
    private long _id;

    public string? LastError { get; private set; }

    private bool Init(System.Drawing.Rectangle area)
    {
        ReleaseAll();
        int cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        using var factory = new Factory1();
        for (int a = 0; a < factory.GetAdapterCount1(); a++)
        {
            using var adapter = factory.GetAdapter1(a);
            for (int o = 0; ; o++)
            {
                Output output;
                try { output = adapter.GetOutput(o); } catch (SharpDXException) { break; }
                using (output)
                {
                    var b = output.Description.DesktopBounds;
                    if (cx < b.Left || cx >= b.Right || cy < b.Top || cy >= b.Bottom) continue;
                    var device = new Device(adapter, DeviceCreationFlags.BgraSupport);
                    using var output1 = output.QueryInterface<Output1>();
                    _duplication = output1.DuplicateOutput(device);
                    _device = device;
                    _bounds = new System.Drawing.Rectangle(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top);
                    _staging = new Texture2D(device, new Texture2DDescription
                    {
                        Width = _bounds.Width, Height = _bounds.Height, MipLevels = 1, ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None,
                    });
                    return true;
                }
            }
        }
        LastError = "No screen found for this area";
        return false;
    }

    public bool TryGrab(System.Drawing.Rectangle area, out Frame frame)
    {
        frame = null!;
        try
        {
            if (_duplication == null || !_bounds.Contains(area.X + area.Width / 2, area.Y + area.Height / 2))
                if (!Init(area)) return false;

            SharpDX.DXGI.Resource? resource;
            try { _duplication!.AcquireNextFrame(100, out _, out resource); }
            catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.WaitTimeout) { return false; }      // nothing changed
            catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
            {
                ReleaseAll();                                                                                              // re-created on the next call
                return false;
            }

            var ctx = _device!.ImmediateContext;
            try
            {
                using var tex = resource!.QueryInterface<Texture2D>();
                ctx.CopyResource(tex, _staging);
            }
            finally { resource!.Dispose(); _duplication.ReleaseFrame(); }

            var map = ctx.MapSubresource(_staging, 0, MapMode.Read, MapFlags.None);
            try { return Crop(map, area, out frame); }
            finally { ctx.UnmapSubresource(_staging, 0); }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ReleaseAll();
            return false;
        }
    }

    private bool Crop(DataBox map, System.Drawing.Rectangle area, out Frame frame)
    {
        frame = null!;
        int x0 = Math.Max(0, area.X - _bounds.X), y0 = Math.Max(0, area.Y - _bounds.Y);
        int w = Math.Min(area.Width, _bounds.Width - x0), h = Math.Min(area.Height, _bounds.Height - y0);
        if (w < 8 || h < 8) { LastError = "area outside the screen"; return false; }

        var gray = new byte[w * h];
        var row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(map.DataPointer + (y0 + y) * map.RowPitch + x0 * 4, row, 0, w * 4);
            int dst = y * w;
            for (int x = 0, s = 0; x < w; x++, s += 4)
                gray[dst + x] = (byte)((29 * row[s] + 150 * row[s + 1] + 77 * row[s + 2]) >> 8);
        }
        frame = new Frame(++_id, 1, w, h, gray, Stopwatch.GetTimestamp());
        LastError = null;
        return true;
    }

    private void ReleaseAll()
    {
        _staging?.Dispose(); _staging = null;
        _duplication?.Dispose(); _duplication = null;
        _device?.Dispose(); _device = null;
    }

    public void Dispose() => ReleaseAll();
}
