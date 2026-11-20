using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace HevcPlayer
{
    public class MainForm : Form
    {
        static readonly Color VideoBg = Color.FromArgb(14, 16, 21);
        static readonly Color BarBg   = Color.FromArgb(23, 26, 33);
        static readonly Color Accent  = Color.FromArgb(37, 99, 235);
        static readonly Color Light   = Color.FromArgb(229, 231, 235);
        static readonly Color Muted   = Color.FromArgb(139, 147, 167);

        const string GLYPH = "Segoe MDL2 Assets";
        const string PLAY = "\uE768", PAUSE = "\uE769", PREV = "\uE892", NEXT = "\uE893",
                     VOL = "\uE767", MUTE = "\uE74F", FULL = "\uE740", UNFULL = "\uE73F",
                     OPENF = "\uE8E5", CC = "\uE7F0", VIDEO = "\uE714";

        MpvPlayer mpv;
        readonly bool previewMode;
        readonly string openOnStart;

        Panel video, bar;
        Button btnPlay, btnPrev, btnNext, btnOpen, btnCc, btnMute, btnFull;
        Slider seek, vol;
        Label lblTime;
        System.Windows.Forms.Timer tick;

        bool hasFile, seeking, fullscreen, muted;
        Rectangle restoreBounds;
        FormWindowState restoreState;
        long lastMouseTick;

        public MainForm(string fileToOpen, bool preview)
        {
            previewMode = preview;
            openOnStart = fileToOpen;

            Text = Program.AppName;
            BackColor = VideoBg;
            ForeColor = Light;
            Font = new Font("Segoe UI", 9.75f);
            MinimumSize = new Size(560, 360);
            ClientSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            TryLoadOwnIcon();
            BuildUi();

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => OnFilesDropped((string[])e.Data.GetData(DataFormats.FileDrop));
            KeyDown += OnKey;
        }

        void TryLoadOwnIcon()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) { var ic = Icon.ExtractAssociatedIcon(exe); if (ic != null) Icon = ic; }
            }
            catch { }
        }

        void BuildUi()
        {
            // ----- control bar (docked bottom) -----
            bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = BarBg };
            Controls.Add(bar);

            btnPlay = GlyphButton(PLAY, 15f);
            btnPlay.Location = new Point(16, 17);
            btnPlay.Click += (s, e) => TogglePlay();
            bar.Controls.Add(btnPlay);

            btnPrev = GlyphButton(PREV, 12f);
            btnPrev.Location = new Point(60, 19);
            btnPrev.Click += (s, e) => { if (mpv != null) mpv.PlaylistPrev(); };
            bar.Controls.Add(btnPrev);

            btnNext = GlyphButton(NEXT, 12f);
            btnNext.Location = new Point(96, 19);
            btnNext.Click += (s, e) => { if (mpv != null) mpv.PlaylistNext(); };
            bar.Controls.Add(btnNext);

            lblTime = new Label
            {
                AutoSize = false, Size = new Size(118, 22), Location = new Point(140, 21),
                ForeColor = Muted, BackColor = BarBg, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f), Text = "00:00 / 00:00"
            };
            bar.Controls.Add(lblTime);

            seek = new Slider { Location = new Point(266, 24), Size = new Size(380, 18) };
            seek.Dragged += (s, e) => { seeking = true; };
            seek.Committed += (s, e) =>
            {
                if (mpv != null) { double d = mpv.Duration; if (d > 0) mpv.SeekAbsolute(seek.Value * d); }
                seeking = false;
            };
            bar.Controls.Add(seek);

            btnCc = GlyphButton(CC, 12f);
            btnCc.Click += (s, e) => { if (mpv != null) mpv.CycleSubs(); };
            bar.Controls.Add(btnCc);

            btnMute = GlyphButton(VOL, 12f);
            btnMute.Click += (s, e) => ToggleMute();
            bar.Controls.Add(btnMute);

            vol = new Slider { Size = new Size(96, 18), Value = 1.0 };
            vol.Dragged += (s, e) => ApplyVolume();
            vol.Committed += (s, e) => ApplyVolume();
            bar.Controls.Add(vol);

            btnFull = GlyphButton(FULL, 12f);
            btnFull.Click += (s, e) => ToggleFullscreen();
            bar.Controls.Add(btnFull);

            btnOpen = GlyphButton(OPENF, 12f);
            btnOpen.Click += (s, e) => OpenDialog();
            bar.Controls.Add(btnOpen);

            bar.Resize += (s, e) => LayoutBar();

            // ----- video area (fills the rest) -----
            video = new Panel { Dock = DockStyle.Fill, BackColor = VideoBg, TabStop = false };
            video.Paint += Video_Paint;
            video.MouseDown += (s, e) => { Focus(); };
            video.DoubleClick += (s, e) => ToggleFullscreen();
            Controls.Add(video);
            video.BringToFront();

            LayoutBar();

            tick = new System.Windows.Forms.Timer { Interval = 200 };
            tick.Tick += Tick;
        }

        void LayoutBar()
        {
            int right = bar.Width - 16;
            btnFull.Location = new Point(right - 36, 19); right -= 44;
            btnOpen.Location = new Point(right - 36, 19); right -= 44;
            vol.Location = new Point(right - 96, 24); right -= 104;
            btnMute.Location = new Point(right - 36, 19); right -= 40;
            btnCc.Location = new Point(right - 36, 19); right -= 48;
            int seekLeft = 266;
            seek.Location = new Point(seekLeft, 24);
            seek.Width = Math.Max(80, right - seekLeft);
        }

        // ---- playback ----
        void EnsureMpv()
        {
            if (mpv != null || previewMode) return;
            mpv = new MpvPlayer();
            mpv.Init(video.Handle, headless: false);
            ApplyVolume();
            tick.Start();
        }

        void LoadFiles(IEnumerable<string> files)
        {
            var list = new List<string>();
            foreach (var f in files) if (IsMedia(f)) list.Add(f);
            if (list.Count == 0) return;

            EnsureMpv();
            if (mpv == null) return;

            mpv.LoadFile(list[0]);
            for (int i = 1; i < list.Count; i++) mpv.AppendFile(list[i]);

            hasFile = true;
            Text = Path.GetFileName(list[0]) + "  —  " + Program.AppName;
            video.Invalidate();
        }

        static bool IsMedia(string f)
        {
            try
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                switch (e)
                {
                    case ".mkv": case ".mp4": case ".m4v": case ".mov": case ".avi":
                    case ".webm": case ".ts": case ".m2ts": case ".flv": case ".wmv":
                    case ".hevc": case ".h265": case ".mpg": case ".mpeg": case ".3gp":
                    case ".ogv": case ".mp3": case ".flac": case ".aac": case ".wav":
                    case ".m4a": case ".opus":
                        return true;
                }
            }
            catch { }
            return false;
        }

        void OnFilesDropped(string[] files) { if (files != null) LoadFiles(files); }

        void OpenDialog()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Open video",
                Filter = "Video files|*.mkv;*.mp4;*.m4v;*.mov;*.avi;*.webm;*.ts;*.m2ts;*.hevc;*.h265;*.flv;*.wmv;*.mpg;*.mpeg|All files|*.*",
                Multiselect = true
            })
            {
                if (d.ShowDialog(this) == DialogResult.OK) LoadFiles(d.FileNames);
            }
        }

        void TogglePlay()
        {
            if (mpv == null) { OpenDialog(); return; }
            mpv.TogglePause();
        }

        void ToggleMute()
        {
            muted = !muted;
            if (mpv != null) mpv.Mute = muted;
            btnMute.Text = muted ? MUTE : VOL;
        }

        void ApplyVolume()
        {
            if (mpv != null) mpv.Volume = vol.Value * 100.0;
            if (muted && vol.Value > 0) { muted = false; btnMute.Text = VOL; if (mpv != null) mpv.Mute = false; }
        }

        void Tick(object s, EventArgs e)
        {
            if (mpv == null || !mpv.Ok) return;
            double dur = mpv.Duration, pos = mpv.TimePos;
            if (!seeking && dur > 0) seek.Value = Math.Max(0, Math.Min(1, pos / dur));
            lblTime.Text = Fmt(pos) + " / " + Fmt(dur);
            btnPlay.Text = mpv.Paused ? PLAY : PAUSE;

            if (fullscreen && Environment.TickCount64 - lastMouseTick > 2800 && bar.Visible)
            {
                bar.Visible = false;
                Cursor.Hide();
            }
        }

        static string Fmt(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            int t = (int)sec, h = t / 3600, m = (t % 3600) / 60, s = t % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, s) : string.Format("{0:00}:{1:00}", m, s);
        }

        // ---- fullscreen ----
        void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                restoreBounds = Bounds; restoreState = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                fullscreen = true;
                btnFull.Text = UNFULL;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = restoreState;
                if (restoreState == FormWindowState.Normal) Bounds = restoreBounds;
                fullscreen = false;
                btnFull.Text = FULL;
                ShowBar();
            }
        }

        void ShowBar()
        {
            lastMouseTick = Environment.TickCount64;
            if (!bar.Visible) { bar.Visible = true; Cursor.Show(); }
        }

        protected override void OnMouseMove(MouseEventArgs e) { ShowBar(); base.OnMouseMove(e); }

        void OnKey(object s, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Space: TogglePlay(); e.Handled = true; break;
                case Keys.F: ToggleFullscreen(); e.Handled = true; break;
                case Keys.Escape: if (fullscreen) ToggleFullscreen(); break;
                case Keys.O: OpenDialog(); break;
                case Keys.M: ToggleMute(); break;
                case Keys.Left: if (mpv != null) mpv.SeekRelative(-5); break;
                case Keys.Right: if (mpv != null) mpv.SeekRelative(5); break;
                case Keys.Up: vol.Value = Math.Min(1, vol.Value + 0.05); ApplyVolume(); break;
                case Keys.Down: vol.Value = Math.Max(0, vol.Value - 0.05); ApplyVolume(); break;
                case Keys.J: if (mpv != null) mpv.CycleSubs(); break;
            }
        }

        // ---- placeholder (shown until the first file loads) ----
        void Video_Paint(object sender, PaintEventArgs e)
        {
            if (hasFile) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = video.ClientRectangle;

            using (var badge = new Font(GLYPH, 46f))
            using (var bb = new SolidBrush(Color.FromArgb(70, 120, 200)))
            {
                var sz = g.MeasureString(VIDEO, badge);
                g.DrawString(VIDEO, badge, bb, (r.Width - sz.Width) / 2, r.Height / 2f - 96);
            }
            using (var t1 = new Font("Segoe UI", 16f, FontStyle.Bold))
            using (var t2 = new Font("Segoe UI", 10.5f))
            using (var b1 = new SolidBrush(Light))
            using (var b2 = new SolidBrush(Muted))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("Drag & drop a video here", t1, b1, new RectangleF(0, r.Height / 2f - 20, r.Width, 30), sf);
                g.DrawString("or click Open  ·  plays HEVC / H.265, MKV, 4K, AV1 — no codecs needed",
                    t2, b2, new RectangleF(0, r.Height / 2f + 16, r.Width, 24), sf);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!previewMode && !string.IsNullOrEmpty(openOnStart))
                LoadFiles(new[] { openOnStart });
        }

        // Fills the window with a representative "now playing" state for a store screenshot.
        // Preview/dev only — never touches the mpv engine.
        public void SetDemoState(string framePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(framePath) && File.Exists(framePath))
                {
                    video.BackgroundImage = Image.FromFile(framePath);
                    video.BackgroundImageLayout = ImageLayout.Zoom;
                }
            }
            catch { }
            hasFile = true;                 // suppress the drop-here placeholder
            seek.Value = 0.36;
            vol.Value = 0.80;
            lblTime.Text = "03:12 / 08:47";
            btnPlay.Text = PAUSE;           // glyph shown while playing
            video.Invalidate();
            bar.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { tick?.Stop(); } catch { }
            try { mpv?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }

        // ---- glyph button factory ----
        static Button GlyphButton(string glyph, float size)
        {
            var b = new Button
            {
                Text = glyph,
                Font = new Font(GLYPH, size),
                Size = new Size(36, 30),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Light,
                BackColor = BarBg,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 46, 58);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(52, 60, 76);
            return b;
        }
    }

    // Thin modern slider (0..1): track + accent fill + round knob. Click or drag to set.
    public class Slider : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Value { get => _v; set { _v = Clamp(value); Invalidate(); } }
        public event EventHandler Dragged;    // while moving
        public event EventHandler Committed;  // on mouse up

        double _v;
        bool down;
        static readonly Color Track = Color.FromArgb(60, 68, 84);
        static readonly Color Fill = Color.FromArgb(37, 99, 235);
        static readonly Color Knob = Color.White;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(23, 26, 33);
            Cursor = Cursors.Hand;
        }

        static double Clamp(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        int Pad => 8;

        protected override void OnMouseDown(MouseEventArgs e) { down = true; SetFromX(e.X); Dragged?.Invoke(this, EventArgs.Empty); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (down) { SetFromX(e.X); Dragged?.Invoke(this, EventArgs.Empty); } base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (down) { down = false; SetFromX(e.X); Committed?.Invoke(this, EventArgs.Empty); } base.OnMouseUp(e); }

        void SetFromX(int x) { int usable = Width - 2 * Pad; _v = usable <= 0 ? 0 : Clamp((double)(x - Pad) / usable); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = Height / 2, usable = Width - 2 * Pad;
            int fx = Pad + (int)(usable * _v);

            using (var tp = new Pen(Track, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(tp, Pad, cy, Width - Pad, cy);
            using (var fp = new Pen(Fill, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (fx > Pad) g.DrawLine(fp, Pad, cy, fx, cy);
            using (var kb = new SolidBrush(Knob))
                g.FillEllipse(kb, fx - 6, cy - 6, 12, 12);
        }
    }
}
