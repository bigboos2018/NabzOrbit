using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nabz
{
internal static class O
{
    internal static Color H(string s)
    {
        return ColorTranslator.FromHtml(s);
    }
    internal static readonly Color Ground = H("#15121C"), Panel = H("#241F2D"), Panel2 = H("#292333"),
                                   Edge = H("#473C52"), Ink = H("#FFF2E8"), Soft = H("#C3B6CC"), Amber = H("#FFB27C"), Lilac = H("#C8ADFF"),
                                   Blue = H("#A7BBFF"), Rose = H("#FFA0AC"), Muted = H("#9F91AB"), Selection = H("#44334D");
    static Dictionary<string, Font> cache = new Dictionary<string, Font>();
    static PrivateFontCollection fonts;
    static readonly List<IntPtr> storage = new List<IntPtr>();
    static bool tried;
    [DllImport("gdi32.dll")]static extern IntPtr AddFontMemResourceEx(IntPtr bytes, uint len, IntPtr reserved,
            ref uint count);
    static void Init()
    {
        if (tried)
            return;
        tried = true;
        if (!Adapters.IsWindows)
            return;
        try {
            fonts = new PrivateFontCollection();
            foreach (string k in new[] {"Orbit.Regular", "Orbit.Bold"})
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(k)) {
                    using (var m = new MemoryStream()) {
                        s.CopyTo(m);
                        var bytes = m.ToArray();
                        var p = Marshal.AllocCoTaskMem(bytes.Length);
                        storage.Add(p);
                        Marshal.Copy(bytes, 0, p, bytes.Length);
                        uint n = 0;
                        AddFontMemResourceEx(p, (uint)bytes.Length, IntPtr.Zero, ref n);
                        fonts.AddMemoryFont(p, bytes.Length);
                    }
                }
        } catch {
            fonts = null;
        }
    }
    internal static Font F(float size, bool bold = false, bool mono = false)
    {
        Init();
        string key = size + "/" + bold + "/" + mono;
        Font f;
        if (cache.TryGetValue(key, out f))
            return f;
        var st = bold ? FontStyle.Bold : FontStyle.Regular;
        try {
            f = !mono && fonts != null
                && fonts.Families.Length > 0 ? new Font(fonts.Families[0], size,
                        st) : new Font(mono ? (Adapters.IsWindows ? "Consolas" : "DejaVu Sans Mono") : "Vazirmatn", size, st);
        } catch {
            f = new Font(mono ? FontFamily.GenericMonospace : FontFamily.GenericSansSerif, size, st);
        }
        return cache[key] = f;
    }
    internal static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(2 * radius, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
    internal static void Box(Graphics g, Rectangle r, Color fill, Color edge, int radius)
    {
        if (r.Width < 2 || r.Height < 2)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Round(r, radius))
            using (var b = new SolidBrush(fill))
                using (var pen = new Pen(edge)) {
                    g.FillPath(b, path);
                    g.DrawPath(pen, path);
                }
    }
    internal static void Txt(Graphics g, string text, Rectangle r, Font f, Color c, bool rtl = false, bool center = false)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        if (rtl)
            flags |= TextFormatFlags.RightToLeft;
        flags |= center ? TextFormatFlags.HorizontalCenter : rtl ? TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(g, text, f, r, c, flags);
    }
    internal static string N(int n)
    {
        return String.Concat(n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(x => x >= '0'
                             && x <= '9' ? (char)('۰' + x - '0') : x));
    }
    internal static string Ms(double ? ms)
    {
        return !ms.HasValue
               || Double.IsInfinity(ms.Value) ? "—" : ms.Value.ToString(ms.Value < 10 ? "0.0" : "0",
                       System.Globalization.CultureInfo.InvariantCulture) + " ms";
    }
}
internal sealed class KeyButton: Button
{
    internal Color Fill, ColorText, Stroke;
    bool over, down;
    internal KeyButton(string s, Color b, Color c, Color edge)
    {
        Text = s;
        Fill = b;
        ColorText = c;
        Stroke = edge;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Font = O.F(10, true);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e)
    {
        over = true;
        Invalidate();
        base.OnMouseEnter(e);
    } protected override void OnMouseLeave(EventArgs e)
    {
        over = false;
        down = false;
        Invalidate();
        base.OnMouseLeave(e);
    } protected override void OnMouseDown(MouseEventArgs e)
    {
        down = true;
        Invalidate();
        base.OnMouseDown(e);
    } protected override void OnMouseUp(MouseEventArgs e)
    {
        down = false;
        Invalidate();
        base.OnMouseUp(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent == null ? O.Ground : Parent.BackColor);
        var b = !Enabled ? O.Panel2 : down ? ControlPaint.Dark(Fill, .12f) : over ? ControlPaint.Light(Fill, .09f) : Fill;
        Color c = Enabled ? ColorText : O.Muted;
        O.Box(g, new Rectangle(0, 0, Width - 1, Height - 1), b, Enabled ? Stroke : O.Edge, Math.Max(5, Height / 5));
        O.Txt(g, Text, new Rectangle(4, 0, Width - 8, Height), Font, c, true, true);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8), c, b);
    }
}
internal sealed class Selector: ComboBox
{
    internal Selector()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = O.Panel2;
        ForeColor = O.Ink;
        Font = O.F(10.5f);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? O.Selection : O.Panel2))
            e.Graphics.FillRectangle(b, e.Bounds);
        O.Txt(e.Graphics, GetItemText(Items[e.Index]), Rectangle.Inflate(e.Bounds, -6, 0), Font, Enabled ? O.Ink : O.Muted);
    }
}
internal sealed class Table: DataGridView
{
    internal Table()
    {
        DoubleBuffered = true;
    }
}
internal sealed class Cover: Control
{
    static Image art;
    internal bool Watching;
    internal int Done = -1, Total = 1;
    internal string Head = "رصدخانهٔ اتصال",
                    Sub = "کیفیت DNS و دسترسی به مقصد را جداگانه ببین.", Stamp = "STANDBY";
    internal Cover()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        if (art == null)
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orbit.Cover"))
                if (s != null)
                    using (var im = Image.FromStream(s))
                        art = new Bitmap(im);
        AccessibleName = "وضعیت رصدخانه";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float d = g.DpiX / 96f;
        int pad = (int)(18 * d);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        g.Clear(O.Ground);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = O.Round(bounds, (int)(14 * d))) {
            g.SetClip(path);
            g.Clear(O.H("#130D1E"));
            if (art != null) {
                int h = (int)(Height * 1.95f), w = (int)(h * art.Width / (float)art.Height);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(art, new Rectangle(0, (Height - h) / 2, w, h));
                using (var fade = new LinearGradientBrush(new Rectangle(Math.Max(0, w - 111), 0, 110, Height), Color.FromArgb(0,
                        O.H("#130D1E")), O.H("#130D1E"), 0f))
                    g.FillRectangle(fade, new Rectangle(Math.Max(0, w - 111), 0, 112, Height));
            }
            using (var pen = new Pen(Color.FromArgb(85, O.Lilac), 1)) {
                g.DrawLine(pen, pad, Height - pad, (int)(56 * d), Height - pad);
                g.DrawLine(pen, Width - pad - (int)(58 * d), pad, Width - pad, pad);
            }
            O.Txt(g, "O R B I T   /   2.1", new Rectangle(pad, (int)(6 * d), Width / 3, (int)(25 * d)), O.F(9, true, true),
                  O.Lilac);
            O.Txt(g, Stamp, new Rectangle((int)(65 * d), Height - (int)(34 * d), Width / 3, (int)(27 * d)), O.F(9, true, true),
                  O.Ink);
            int x = Width * 39 / 100, r = Width - pad - x;
            O.Txt(g, Watching ? "FIELD RECORDER     /     LIVE DIAGNOSTICS" : "NABZ LAB     /     RESOLVER OBSERVATORY",
                  new Rectangle(x, (int)(8 * d), r - (int)(72 * d), (int)(22 * d)), O.F(8.4f, false, true), O.Lilac, false, true);
            O.Txt(g, Head, new Rectangle(x, (int)(28 * d), r, (int)(54 * d)), O.F(23, true), O.Ink, true);
            O.Txt(g, Sub, new Rectangle(x, Height - (int)(38 * d), r, (int)(30 * d)), O.F(10.2f), O.Soft, true);
            if (Done >= 0) {
                using (var b = new SolidBrush(O.Amber))
                    g.FillRectangle(b, 0, Height - (int)(3 * d), Math.Max(2, Width * Done / Math.Max(1, Total)), (int)(3 * d));
            }
            g.ResetClip();
            using (var pen = new Pen(O.Edge))
                g.DrawPath(pen, path);
        }
    }
}
internal sealed class Scope: Control
{
    internal readonly List<Frame> Frames = new List<Frame>();
    internal Scope()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "نمودار مودم، مسیر اینترنت و DNS فعلی";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float d = g.DpiX / 96f;
        int u = (int)(12 * d), rhs = (int)(139 * d), left = (int)(65 * d), right = Width - rhs;
        g.Clear(O.Ground);
        O.Box(g, new Rectangle(0, 0, Width - 1, Height - 1), O.Panel, O.Edge, u);
        int lane = (Height - 3 * u) / 3;
        var colors = new[] {O.Lilac, O.Amber, O.Blue};
        var names = new[] {"مودم", "مسیر اینترنت", "DNS فعلی"};
        var caps = new[] {"ICMP  /  01", "TCP 443  /  02", "DNS 53  /  03"};
        var data = Frames.ToArray();
        var values = data.SelectMany(f => new[] {f.Gateway, f.Internet, f.Dns}).Where(v => v.HasValue).Select(v => v.Value);
        double scale = Math.Max(50, Math.Ceiling(values.DefaultIfEmpty(0).Max() / 25) * 25);
        DateTimeOffset first = data.Length == 0 ? DateTimeOffset.Now : data[0].At,
                       last = data.Length == 0 ? first : data[data.Length - 1].At;
        double secs = Math.Max(10, (last - first).TotalSeconds);
        Func<DateTimeOffset, float> x = at => left + (float)(Math.Max(0, Math.Min(1,
                                        (at - first).TotalSeconds / secs)) * (right - left));
        for (int k = 0; k < 3; k++) {
            int y = u + k * lane, bottom = y + lane - u;
            var c = colors[k];
            O.Txt(g, names[k], new Rectangle(right + u, y, Width - right - 2 * u, lane / 2), O.F(10.5f, true), c, true);
            O.Txt(g, caps[k], new Rectangle(right + u, y + lane / 2, Width - right - 2 * u, lane / 3), O.F(8, false, true),
                  O.Muted, false, true);
            O.Txt(g, ((int)scale).ToString() + " ms", new Rectangle(u, y, left - u, (int)(18 * d)), O.F(8, false, true), O.Soft);
            if (data.Length > 0) {
                var f = data[data.Length - 1];
                double ? value = k == 0 ? f.Gateway : k == 1 ? f.Internet : f.Dns;
                O.Txt(g, O.Ms(value), new Rectangle(u, bottom - (int)(17 * d), left - u, (int)(18 * d)), O.F(8.3f, true, true), c);
            }
            using (var grid = new Pen(O.Edge)) {
                grid.DashStyle = DashStyle.Dot;
                for (int t = 0; t < 3; t++) {
                    int yy = y + (bottom - y) * t / 2;
                    g.DrawLine(grid, left, yy, right, yy);
                }
            }
            PointF ? prev = null;
            using (var line = new Pen(c, Math.Max(1.6f, d * 2)))
                using (var dot = new SolidBrush(c))
                    using (var miss = new Pen(O.Rose, 1.7f)) {
                        foreach (var f in data) {
                            double ? val = k == 0 ? f.Gateway : k == 1 ? f.Internet : f.Dns;
                            float xx = x(f.At);
                            if (!val.HasValue) {
                                if (k == 2 && f.Resolvers == 0) {
                                    prev = null;
                                    continue;
                                }
                                g.DrawLine(miss, xx - 2, bottom - 4, xx + 2, bottom);
                                g.DrawLine(miss, xx + 2, bottom - 4, xx - 2, bottom);
                                prev = null;
                                continue;
                            }
                            var pt = new PointF(xx, bottom - (float)(val.Value / scale) * (bottom - y));
                            if (prev.HasValue)
                                g.DrawLine(line, prev.Value, pt);
                            g.FillEllipse(dot, pt.X - 2, pt.Y - 2, 4, 4);
                            prev = pt;
                        }
                    }
        }
        using (var p = new Pen(O.Amber)) {
            p.DashStyle = DashStyle.Dash;
            foreach (var f in data.Where(f => f.Mark.HasValue))
                g.DrawLine(p, x(f.Mark.Value), u, x(f.Mark.Value), Height - 2 * u);
        }
        O.Txt(g, data.Length > 0 ? first.ToLocalTime().ToString("HH:mm:ss") + "    /    " +
              last.ToLocalTime().ToString("HH:mm:ss") : "NO SAMPLES", new Rectangle(left, Height - 2 * u, Width / 2, 2 * u), O.F(8,
                      false, true), O.Soft);
        if (data.Length == 0)
            O.Txt(g, "نمودار بعد از اولین نمونه رسم می‌شود", new Rectangle(left, Height / 2 - 20,
                    right - left, 40), O.F(11, true), O.Ink, true, true);
    }
}
}
