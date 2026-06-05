// Lumos - A "night-mode filter" for your REAL desktop.
//
//   Run:    Lumos.exe
//   Quit:   ESC
//   Adjust: + / -   (halo radius, 50..1500 px)
//
// Implementation: a per-pixel-alpha layered window (WS_EX_LAYERED +
// UpdateLayeredWindow). The window's content is a top-down 32-bit DIB
// section (BGRA in memory). Inside the halo every pixel is fully
// transparent (alpha=0) so the actual desktop shows through; outside
// is fully opaque black (alpha=255). A smoothstep falloff band gives
// the halo a soft edge.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lumos;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new LumosForm());
    }
}

internal sealed class LumosForm : Form
{
    // ---------- Win32 styles ----------
    private const int  WS_EX_LAYERED     = 0x00080000;
    private const int  WS_EX_TRANSPARENT = 0x00000020;
    private const int  WS_EX_TOOLWINDOW  = 0x00000080;
    private const uint ULW_ALPHA         = 0x00000002;
    private const byte AC_SRC_OVER       = 0x00;
    private const byte AC_SRC_ALPHA      = 0x01;

    // ---------- Lumos params ----------
    private const int MinRadius   = 50;
    private const int MaxRadius   = 1500;
    private const int RadiusStep  = 30;

    // ---------- P/Invoke ----------
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDest,
        IntPtr pptDest,
        IntPtr psize,
        IntPtr hdcSrc,
        IntPtr pptSrc,
        uint   crKey,
        IntPtr pblend,
        uint   dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO bmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int   biSize;
        public int   biWidth;
        public int   biHeight;
        public short biPlanes;
        public short biBitCount;
        public int   biCompression;
        public int   biSizeImage;
        public int   biXPelsPerMeter;
        public int   biYPelsPerMeter;
        public int   biClrUsed;
        public int   biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint             bmiColors;
    }

    // ---------- State ----------
    private readonly int    _width;
    private readonly int    _height;
    private Point _mouse;
    private int   _radius = 280;

    // DIB section handles
    private readonly IntPtr _hdcSrc;
    private readonly IntPtr _hDib;
    private readonly IntPtr _pBits;   // raw pixel pointer (BGRA, top-down)

    // Pre-allocated, marshalled structs for UpdateLayeredWindow
    private readonly IntPtr _blendPtr;
    private readonly IntPtr _sizePtr;
    private readonly IntPtr _srcPointPtr;

    public LumosForm()
    {
        var screen = Screen.PrimaryScreen.Bounds;
        _width  = screen.Width;
        _height = screen.Height;

        FormBorderStyle   = FormBorderStyle.None;
        StartPosition     = FormStartPosition.Manual;
        Bounds            = screen;
        TopMost           = true;
        ShowInTaskbar     = false;
        Text              = "Lumos";

        _mouse = Cursor.Position;

        // Create a top-down 32-bit DIB section.
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize          = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth         = _width,
                biHeight        = -_height,            // negative = top-down DIB
                biPlanes        = 1,
                biBitCount      = 32,
                biCompression   = 0,                    // BI_RGB
                biSizeImage     = 0,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed       = 0,
                biClrImportant  = 0,
            },
            bmiColors = 0,
        };

        IntPtr screenDc = GetDC(IntPtr.Zero);
        _hdcSrc = CreateCompatibleDC(screenDc);
        _hDib   = CreateDIBSection(screenDc, ref bmi, 0, out _pBits, IntPtr.Zero, 0);
        SelectObject(_hdcSrc, _hDib);
        ReleaseDC(IntPtr.Zero, screenDc);

        // Pre-allocate BLENDFUNCTION, SIZE, POINT for UpdateLayeredWindow.
        _blendPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BLENDFUNCTION>());
        Marshal.StructureToPtr(new BLENDFUNCTION
        {
            BlendOp             = AC_SRC_OVER,
            BlendFlags          = 0,
            SourceConstantAlpha = 255,
            AlphaFormat         = AC_SRC_ALPHA,
        }, _blendPtr, false);

        _sizePtr = Marshal.AllocHGlobal(Marshal.SizeOf<SIZE>());
        Marshal.StructureToPtr(new SIZE { cx = _width, cy = _height }, _sizePtr, false);

        _srcPointPtr = Marshal.AllocHGlobal(Marshal.SizeOf<POINT>());
        Marshal.StructureToPtr(new POINT { X = 0, Y = 0 }, _srcPointPtr, false);

        Shown      += OnShown;
        KeyDown    += OnKeyDown;
        FormClosed += OnClosed;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private void OnShown(object sender, EventArgs e)
    {
        BringToFront();
        Activate();

        var timer = new Timer { Interval = 16 };
        timer.Tick += (_, _) =>
        {
            var p = Cursor.Position;
            if (p != _mouse) { _mouse = p; Render(); }
        };
        timer.Start();
        Render();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:           Close(); break;
            case Keys.Oemplus:
            case Keys.Add:              _radius = Math.Min(_radius + RadiusStep, MaxRadius); Render(); break;
            case Keys.OemMinus:
            case Keys.Subtract:         _radius = Math.Max(_radius - RadiusStep, MinRadius); Render(); break;
        }
    }

    private void OnClosed(object sender, FormClosedEventArgs e)
    {
        DeleteObject(_hDib);
        DeleteDC(_hdcSrc);
        Marshal.FreeHGlobal(_blendPtr);
        Marshal.FreeHGlobal(_sizePtr);
        Marshal.FreeHGlobal(_srcPointPtr);
    }

    // ---------- Rendering ----------
    // Pixels are 32-bit BGRA in memory (top-down, since biHeight < 0).
    private void Render()
    {
        int width        = _width;
        int height       = _height;
        int radius       = _radius;
        int radiusSq     = radius * radius;
        int featherStart = radius / 2;
        int featherStartSq = featherStart * featherStart;
        int featherRange = radius - featherStart;
        int mouseX       = _mouse.X;
        int mouseY       = _mouse.Y;

        // Write directly to the DIB pixel memory.
        // Stride = width * 4 (no padding for 32-bit).
        // Format in memory (little-endian, "ARGB"): B, G, R, A
        int    strideBytes = width * 4;
        byte[] pixels = new byte[strideBytes * height];

        for (int y = 0; y < height; y++)
        {
            int dy     = y - mouseY;
            int dySq   = dy * dy;
            int rowOff = y * strideBytes;

            for (int x = 0; x < width; x++)
            {
                int dx     = x - mouseX;
                int distSq = dx * dx + dySq;
                int off    = rowOff + (x << 2);
                byte alpha;

                if (distSq < featherStartSq)
                {
                    alpha = 0;     // Fully transparent -> real desktop shows.
                }
                else if (distSq < radiusSq)
                {
                    int tNorm = ((distSq - featherStartSq) << 10) / (radiusSq - featherStartSq);
                    if (tNorm > 1024) tNorm = 1024;
                    float t = tNorm / 1024.0f;
                    t = t * t * (3f - 2f * t);
                    alpha = (byte)(255 * t);
                }
                else
                {
                    alpha = 255;   // Fully opaque black.
                }

                // BGRA in memory, low byte first:
                pixels[off    ] = 0;       // B
                pixels[off + 1] = 0;       // G
                pixels[off + 2] = 0;       // R
                pixels[off + 3] = alpha;   // A
            }
        }

        Marshal.Copy(pixels, 0, _pBits, pixels.Length);

        UpdateLayeredWindow(
            Handle,
            IntPtr.Zero,    // hdcDest
            IntPtr.Zero,    // pptDest
            _sizePtr,       // size
            _hdcSrc,        // hdcSrc (our DIB section's DC)
            _srcPointPtr,   // pptSrc
            0,              // crKey
            _blendPtr,      // blend function
            ULW_ALPHA);     // flags
    }
}
