// 桌面宠物 —— 单文件 WinForms 桌宠
// 采用分层窗口(UpdateLayeredWindow)做真正的逐像素透明，而不是 TransparencyKey 色键，
// 这样角色抗锯齿边缘能保留，且完全透明的像素会自动穿透鼠标点击。
//
// 编译：csc /target:winexe /unsafe /resource:sprite.png,sprite.png ...
// 目标框架 .NET Framework 4.x（Windows 自带，无需安装任何运行时）。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshPet
{
    internal static class Nat
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER hdr;
            public int colors;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi,
            uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0;
        public const byte AC_SRC_ALPHA = 1;
        public const uint DIB_RGB_COLORS = 0;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
    }

    /// <summary>一块可以逐像素带 alpha 贴到窗口上的 GDI 表面（预乘 BGRA）。</summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _memDc = IntPtr.Zero;
        private IntPtr _dib = IntPtr.Zero;
        private IntPtr _old = IntPtr.Zero;
        private IntPtr _bits = IntPtr.Zero;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public LayeredSurface(int w, int h) { Create(w, h); }

        private void Create(int w, int h)
        {
            IntPtr screen = Nat.GetDC(IntPtr.Zero);
            _memDc = Nat.CreateCompatibleDC(screen);
            Nat.ReleaseDC(IntPtr.Zero, screen);

            Nat.BITMAPINFO bmi = new Nat.BITMAPINFO();
            bmi.hdr.biSize = Marshal.SizeOf(typeof(Nat.BITMAPINFOHEADER));
            bmi.hdr.biWidth = w;
            bmi.hdr.biHeight = -h;      // 负数 = 自上而下
            bmi.hdr.biPlanes = 1;
            bmi.hdr.biBitCount = 32;
            bmi.hdr.biCompression = 0;  // BI_RGB

            _dib = Nat.CreateDIBSection(_memDc, ref bmi, Nat.DIB_RGB_COLORS,
                                        out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            _old = Nat.SelectObject(_memDc, _dib);
            Width = w; Height = h;
        }

        public void Dispose()
        {
            if (_old != IntPtr.Zero) Nat.SelectObject(_memDc, _old);
            if (_dib != IntPtr.Zero) Nat.DeleteObject(_dib);
            if (_memDc != IntPtr.Zero) Nat.DeleteDC(_memDc);
            _old = _dib = _memDc = _bits = IntPtr.Zero;
        }

        /// <summary>把一张 32bppArgb 位图预乘后写进 DIB，并贴到窗口上。</summary>
        public unsafe void Present(IntPtr hwnd, int x, int y, Bitmap src)
        {
            BitmapData d = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                                        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte* sp0 = (byte*)d.Scan0;
                byte* dp0 = (byte*)_bits;
                for (int row = 0; row < Height; row++)
                {
                    byte* sp = sp0 + row * d.Stride;
                    byte* dp = dp0 + row * Width * 4;
                    for (int col = 0; col < Width; col++)
                    {
                        byte a = sp[3];
                        if (a == 255)
                        {
                            dp[0] = sp[0]; dp[1] = sp[1]; dp[2] = sp[2]; dp[3] = 255;
                        }
                        else if (a == 0)
                        {
                            *(int*)dp = 0;
                        }
                        else
                        {
                            dp[0] = (byte)(sp[0] * a / 255);
                            dp[1] = (byte)(sp[1] * a / 255);
                            dp[2] = (byte)(sp[2] * a / 255);
                            dp[3] = a;
                        }
                        sp += 4; dp += 4;
                    }
                }
            }
            finally { src.UnlockBits(d); }

            Nat.POINT dst = new Nat.POINT(); dst.X = x; dst.Y = y;
            Nat.SIZE size = new Nat.SIZE(); size.cx = Width; size.cy = Height;
            Nat.POINT srcPt = new Nat.POINT(); srcPt.X = 0; srcPt.Y = 0;
            Nat.BLENDFUNCTION bf = new Nat.BLENDFUNCTION();
            bf.BlendOp = Nat.AC_SRC_OVER;
            bf.BlendFlags = 0;
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = Nat.AC_SRC_ALPHA;

            IntPtr screen = Nat.GetDC(IntPtr.Zero);
            Nat.UpdateLayeredWindow(hwnd, screen, ref dst, ref size, _memDc, ref srcPt,
                                    0, ref bf, Nat.ULW_ALPHA);
            Nat.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    internal sealed class PetForm : Form, IMessageFilter
    {
        // ---- 台词 ----
        private static readonly string[] Lines = new string[]
        {
            "摸鱼中～", "主人好呀！", "咕噜咕噜…", "再戳就生气啦",
            "今天也元气满满！", "别戳啦，痒！", "要不要休息一下？", "我一直看着你哦",
            "水里好凉快～", "哼，才没在偷懒", "记得喝水呀", "元气补充完毕！",
            "又见面啦～", "我在数你的头发", "有点困了…", "来啦来啦！",
            "戳我干嘛呀", "今天也要加油哦", "悄悄冒个泡", "别走嘛～"
        };

        private const float MinScale = 0.45f;
        private const float MaxScale = 2.6f;
        private const int BaseHeight = 260;   // 缩放 1.0 时角色的显示高度

        private readonly Random _rng = new Random();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        private Bitmap _sprite;       // 原始分辨率（已去背）
        private Bitmap _scaled;       // 按当前缩放缓存
        private Bitmap _canvas;       // 每帧绘制目标
        private LayeredSurface _surface;

        private float _scale = 1.0f;
        private int _baseW, _baseH;
        private int _dispW, _dispH;
        private int _winW, _winH;
        private int _drawX, _drawY;
        private int _jumpH, _bubbleReserve;

        // 动画
        private int _animKind = -1;   // -1 空闲, 0 跳跃, 1 压扁, 2 抖动
        private double _animStart;
        private double _animDur;
        private int _cycle;

        // 气泡
        private string _bubble;
        private double _bubbleStart;
        private double _bubbleDur = 2.6;

        // 拖动 / 点击判定
        private bool _pressing;
        private bool _moved;
        private Point _pressCursor;
        private Point _pressWindow;
        private double _pressTime;

        private ContextMenuStrip _menu;
        private ToolStripMenuItem _topItem;
        private Font _font;
        private int _lastInterval = -1;

        // ---- 临时诊断日志（排查完可删） ----
        private static readonly string LogPath =
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "pet-debug.log");

        private static void Log(string s)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine);
            }
            catch { }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try { Frame(); }
            catch (Exception ex) { Log("TICK EXCEPTION: " + ex); }
        }

        public PetForm()
        {
            _sprite = LoadSprite();
            _baseH = BaseHeight;
            _baseW = (int)Math.Round(_sprite.Width * (double)BaseHeight / _sprite.Height);

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.Text = "桌宠";
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.Opaque |
                          ControlStyles.UserPaint, true);

            BuildMenu();

            _font = PickFont(13f);

            _scale = 1.0f;
            _dispW = _baseW; _dispH = _baseH;
            Measure();
            this.ClientSize = new Size(_winW, _winH);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(wa.Right - _winW - 40, wa.Bottom - _winH - 10);

            RebuildScaled();
            RebuildCanvas();

            _timer.Interval = 33;
            _timer.Tick += OnTick;
            _clock.Start();
            _timer.Start();

            Application.AddMessageFilter(this);   // 没有焦点也能收到滚轮
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Nat.WS_EX_LAYERED;      // 逐像素透明
                cp.ExStyle |= Nat.WS_EX_TOOLWINDOW;   // 不出现在 Alt+Tab
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // ------------------------------------------------------------------
        // 资源
        // ------------------------------------------------------------------
        private static Bitmap LoadSprite()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string found = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith("sprite.png", StringComparison.OrdinalIgnoreCase)) { found = n; break; }
            if (found == null) throw new InvalidOperationException("嵌入资源 sprite.png 未找到");
            using (Stream s = asm.GetManifestResourceStream(found))
            using (Bitmap raw = new Bitmap(s))
            {
                // 统一成 32bppArgb，SourceCopy 保证 alpha 原样带过来
                Bitmap bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(raw, new Rectangle(0, 0, raw.Width, raw.Height));
                }
                return bmp;
            }
        }

        private static Font PickFont(float size)
        {
            string[] want = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun" };
            foreach (string name in want)
            {
                try
                {
                    Font f = new Font(name, size, FontStyle.Regular, GraphicsUnit.Pixel);
                    if (f.Name == name) return f;
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ------------------------------------------------------------------
        // 布局
        // ------------------------------------------------------------------
        private void Measure()
        {
            _dispW = Math.Max(24, _dispW);
            _dispH = Math.Max(24, _dispH);
            _jumpH = (int)(_dispH * 0.16);
            float fs = Math.Max(0.8f, Math.Min(1.6f, _scale));
            _bubbleReserve = (int)(72 * fs);
            int side = 22 + (int)(_dispW * 0.10);
            _winW = _dispW + side * 2;
            _winH = _dispH + _jumpH + _bubbleReserve + 30;
            _drawX = (_winW - _dispW) / 2;
            _drawY = _winH - 20 - _dispH;
        }

        private void RebuildScaled()
        {
            if (_scaled != null) _scaled.Dispose();
            _scaled = new Bitmap(_dispW, _dispH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_scaled))
            {
                g.Clear(Color.Transparent);
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(_sprite, new Rectangle(0, 0, _dispW, _dispH));
            }
        }

        private void RebuildCanvas()
        {
            if (_canvas != null) _canvas.Dispose();
            _canvas = new Bitmap(_winW, _winH, PixelFormat.Format32bppArgb);
            if (_surface != null) _surface.Dispose();
            _surface = new LayeredSurface(_winW, _winH);
            this.ClientSize = new Size(_winW, _winH);
        }

        private void ApplyScale(float s)
        {
            s = Math.Max(MinScale, Math.Min(MaxScale, s));
            if (Math.Abs(s - _scale) < 0.0005f) return;

            Point anchor = new Point(this.Left + _drawX + _dispW / 2,
                                     this.Top + _drawY + _dispH);
            _scale = s;
            _dispW = Math.Max(24, (int)Math.Round(_baseW * _scale));
            _dispH = Math.Max(24, (int)Math.Round(_baseH * _scale));
            Measure();
            RebuildScaled();
            RebuildCanvas();
            this.Location = new Point(anchor.X - (_drawX + _dispW / 2),
                                      anchor.Y - (_drawY + _dispH));
            Frame();
        }

        // ------------------------------------------------------------------
        // 菜单
        // ------------------------------------------------------------------
        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("调整大小");
            sizeMenu.DropDownItems.Add(SizeItem("小   (60%)", 0.6f));
            sizeMenu.DropDownItems.Add(SizeItem("中   (100%)", 1.0f));
            sizeMenu.DropDownItems.Add(SizeItem("大   (140%)", 1.4f));
            sizeMenu.DropDownItems.Add(SizeItem("特大 (180%)", 1.8f));
            _menu.Items.Add(sizeMenu);

            _topItem = new ToolStripMenuItem("窗口置顶");
            _topItem.Click += delegate { SetTopMost(!this.TopMost); };
            _menu.Items.Add(_topItem);
            _topItem.Checked = this.TopMost;

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem about = new ToolStripMenuItem("互动一下");
            about.Click += delegate { Interact(); };
            _menu.Items.Add(about);

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { _timer.Stop(); Application.Exit(); };
            _menu.Items.Add(quit);
        }

        private ToolStripMenuItem SizeItem(string text, float s)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += delegate { ApplyScale(s); };
            return it;
        }

        private void SetTopMost(bool on)
        {
            this.TopMost = on;
            _topItem.Checked = on;
        }

        // ------------------------------------------------------------------
        // 鼠标：拖动 / 点击 / 滚轮
        // ------------------------------------------------------------------
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                Point p = Cursor.Position;
                if (this.Bounds.Contains(p))
                {
                    int delta = (short)((long)m.WParam >> 16);
                    ApplyScale(_scale * (delta > 0 ? 1.10f : 1f / 1.10f));
                    return true;
                }
            }
            return false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressing = true;
                _moved = false;
                _pressCursor = Cursor.Position;
                _pressWindow = this.Location;
                _pressTime = _clock.Elapsed.TotalSeconds;
            }
            else if (e.Button == MouseButtons.Right)
            {
                _topItem.Checked = this.TopMost;
                try
                {
                    // 菜单必须挂在本窗口上并先取得激活，否则弹窗会因为拿不到
                    // 激活而立刻自行关闭（表现就是"闪一下不见了"）。
                    Activate();
                    _menu.Show(this, new Point(e.X, e.Y));
                }
                catch (Exception ex) { Log("MENU EXCEPTION: " + ex); }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressing) return;
            Point now = Cursor.Position;
            int dx = now.X - _pressCursor.X;
            int dy = now.Y - _pressCursor.Y;
            if (!_moved && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)) _moved = true;
            if (_moved)
            {
                this.Location = new Point(_pressWindow.X + dx, _pressWindow.Y + dy);
                Frame();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_pressing) return;
            _pressing = false;
            double held = _clock.Elapsed.TotalSeconds - _pressTime;
            if (!_moved && held < 0.6) Interact();     // 视为点击
        }

        // ------------------------------------------------------------------
        // 互动
        // ------------------------------------------------------------------
        private void Interact()
        {
            _animKind = _cycle % 3;
            _cycle++;
            if (_animKind == 0) _animDur = 0.62;
            else if (_animKind == 1) _animDur = 0.55;
            else _animDur = 0.70;
            _animStart = _clock.Elapsed.TotalSeconds;
            ShowBubble();
            Frame();
        }

        private void ShowBubble()
        {
            _bubble = Lines[_rng.Next(Lines.Length)];
            _bubbleStart = _clock.Elapsed.TotalSeconds;
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------
        private void Frame()
        {
            double now = _clock.Elapsed.TotalSeconds;

            float sx = 1f, sy = 1f, rot = 0f, ox = 0f, oy = 0f;

            // 呼吸
            double bt = now * 1.7;
            sy *= (float)(1.0 + 0.012 * Math.Sin(bt));
            sx *= (float)(1.0 - 0.008 * Math.Sin(bt));

            if (_animKind >= 0)
            {
                double u = (now - _animStart) / _animDur;
                if (u >= 1.0) { _animKind = -1; }
                else
                {
                    if (_animKind == 0)              // 跳跃
                    {
                        oy = -(float)(Math.Sin(Math.PI * u) * _jumpH);
                        double sq = 0;
                        if (u < 0.15) sq = 1.0 - u / 0.15;
                        else if (u > 0.85) sq = (u - 0.85) / 0.15;
                        if (sq > 0)
                        {
                            sy *= (float)(1.0 - 0.18 * sq);
                            sx *= (float)(1.0 + 0.16 * sq);
                        }
                    }
                    else if (_animKind == 1)         // 压扁回弹
                    {
                        double k = Math.Sin(Math.PI * u);
                        sy *= (float)(1.0 - 0.30 * k);
                        sx *= (float)(1.0 + 0.26 * k);
                    }
                    else                             // 左右抖动
                    {
                        double damp = 1.0 - u;
                        rot = (float)(Math.Sin(u * Math.PI * 6.0) * 9.0 * damp);
                        ox = (float)(Math.Sin(u * Math.PI * 5.0) * 9.0 * damp);
                    }
                }
            }

            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                GraphicsState st = g.Save();
                float pivotX = _drawX + _dispW / 2f;
                float pivotY = _drawY + _dispH;
                g.TranslateTransform(pivotX + ox, pivotY + oy);
                g.ScaleTransform(sx, sy);
                if (rot != 0f) g.RotateTransform(rot);
                g.TranslateTransform(-pivotX, -pivotY);
                g.DrawImage(_scaled, _drawX, _drawY, _dispW, _dispH);
                g.Restore(st);

                DrawBubble(g, now);
            }

            _surface.Present(this.Handle, this.Left, this.Top, _canvas);

            // 有动画就快一点，空闲就慢一点，省 CPU
            int want = (_animKind >= 0 || _bubble != null) ? 20 : 50;
            if (want != _lastInterval) { _timer.Interval = want; _lastInterval = want; }
        }

        // ------------------------------------------------------------------
        // 气泡：固定在角色最高点之上，保证任何互动都不遮挡角色
        // ------------------------------------------------------------------
        private void DrawBubble(Graphics g, double now)
        {
            if (_bubble == null) return;
            double u = (now - _bubbleStart) / _bubbleDur;
            if (u >= 1.0) { _bubble = null; return; }

            float alpha;
            if (u < 0.12) alpha = (float)(u / 0.12);
            else if (u > 0.80) alpha = (float)((1.0 - u) / 0.20);
            else alpha = 1f;
            if (alpha <= 0.01f) return;

            float pop = u < 0.14 ? (float)(0.80 + 0.20 * (u / 0.14)) : 1f;

            float fs = Math.Max(11f, Math.Min(19f, 13.5f * Math.Max(0.85f, _scale)));
            using (Font f = new Font(_font.FontFamily, fs, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoClip;

                float maxW = Math.Max(80f, _winW * 0.80f);
                SizeF ts = g.MeasureString(_bubble, f, (int)maxW, sf);

                float padX = 13f, padY = 8f;
                float bw = ts.Width + padX * 2f;
                float bh = ts.Height + padY * 2f;
                if (bw > _winW - 8f) bw = _winW - 8f;

                // 角色跳跃最高点的上方，永远不会压到角色
                float bottom = _drawY - _jumpH - 6f;
                float cx = _winW / 2f;
                float left = cx - bw / 2f;
                float top = bottom - bh;

                GraphicsState st = g.Save();
                g.TranslateTransform(cx, bottom);
                g.ScaleTransform(pop, pop);
                g.TranslateTransform(-cx, -bottom);

                RectangleF box = new RectangleF(left, top, bw, bh);
                using (GraphicsPath path = RoundRect(box, 12f))
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb((int)(alpha * 246), 255, 255, 255)))
                        g.FillPath(fill, path);
                    using (Pen pen = new Pen(Color.FromArgb((int)(alpha * 190), 108, 140, 196), 1.6f))
                        g.DrawPath(pen, path);
                }

                // 小尾巴指向角色（长度随留白自适应，最长 26px）
                float gap = bottom - (_drawY - 2f);
                float tail = Math.Max(6f, Math.Min(26f, gap));
                using (GraphicsPath tp = new GraphicsPath())
                {
                    tp.AddPolygon(new PointF[]
                    {
                        new PointF(cx - 8f, bottom - 1f),
                        new PointF(cx + 8f, bottom - 1f),
                        new PointF(cx, bottom + tail)
                    });
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(alpha * 246), 255, 255, 255)))
                        g.FillPath(tb, tp);
                }

                using (SolidBrush ink = new SolidBrush(Color.FromArgb((int)(alpha * 255), 38, 52, 84)))
                    g.DrawString(_bubble, f, ink, box, sf);
                g.Restore(st);
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            if (_surface != null) _surface.Dispose();
            if (_canvas != null) _canvas.Dispose();
            if (_scaled != null) _scaled.Dispose();
            if (_sprite != null) _sprite.Dispose();
            if (_font != null) _font.Dispose();
            base.OnFormClosed(e);
        }

        [STAThread]
        private static void Main()
        {
            try { Nat.SetProcessDPIAware(); }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs a)
            { Log("THREAD EXCEPTION: " + a.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs a)
            { Log("DOMAIN EXCEPTION: " + a.ExceptionObject); };
            try
            {
                Application.Run(new PetForm());
            }
            catch (Exception ex)
            {
                Log("FATAL: " + ex);
                MessageBox.Show(ex.ToString(), "桌宠启动失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
