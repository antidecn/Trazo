using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Aviso discreto de versión nueva, con el aspecto de Trazo: tarjeta oscura en la esquina inferior derecha,
    /// encima de la bandeja, con el logo, la versión y dos botones. No roba el foco, aparece con un fundido
    /// y se cierra sola al cabo de un rato (no mientras el ratón está encima). Solo avisa: no descarga nada.
    /// </summary>
    internal sealed class UpdateToast : Form {

        private const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        private const int VisibleMs = 20000, FadeMs = 200;

        /// <summary>El usuario quiere ver la versión nueva (y decidir si instalarla).</summary>
        public event Action? ViewRequested;

        private readonly string title = "Hay una versión nueva de Trazo";
        private readonly string body;
        private readonly Font titleFont, bodyFont, buttonFont;
        private readonly Bitmap logo;
        private readonly float scale;
        private readonly System.Windows.Forms.Timer closeTimer = new() { Interval = VisibleMs };
        private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 15 };
        private readonly System.Diagnostics.Stopwatch fadeClock = new();
        private bool fadingOut;

        // Zonas con clic: los dos botones y la cruz de cerrar
        private Rectangle viewButton, laterButton, closeButton;
        private Rectangle hover = Rectangle.Empty;

        public UpdateToast(Version latest, Version current) {
            body = $"La versión {latest} ya está disponible (tienes la {current}).\nTú decides si instalarla.";

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = SettingsForm.Back;
            Opacity = 0;
            Text = "Trazo";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            scale = DeviceDpi / 96f;
            titleFont = new Font("Segoe UI Semibold", 10.5f * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            bodyFont = new Font("Segoe UI", 9f * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            buttonFont = new Font("Segoe UI", 9f * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using (Icon icon = Icons.AppIcon(LogoSize)) logo = icon.ToBitmap();

            LayoutCard();

            closeTimer.Tick += (_, _) => FadeOut();
            fadeTimer.Tick += (_, _) => OnFade();
        }

        private int Px(float logical) => (int)Math.Round(logical * scale);
        private int LogoSize => Px(36);

        /// <summary>Coloca el contenido, calcula el tamaño y pone la tarjeta sobre la bandeja.</summary>
        private void LayoutCard() {
            int pad = Px(16), gap = Px(12);
            int textLeft = pad + LogoSize + gap;
            Size titleSize = TextRenderer.MeasureText(title, titleFont);
            Size bodySize = TextRenderer.MeasureText(body, bodyFont);
            int textWidth = Math.Max(titleSize.Width, bodySize.Width);
            int width = Math.Max(Px(340), textLeft + textWidth + pad + Px(16));

            int y = pad + titleSize.Height + Px(4) + bodySize.Height + Px(14);
            int buttonHeight = Px(30);
            int viewWidth = TextRenderer.MeasureText("Ver la versión", buttonFont).Width + Px(28);
            int laterWidth = TextRenderer.MeasureText("Ahora no", buttonFont).Width + Px(28);
            viewButton = new Rectangle(textLeft, y, viewWidth, buttonHeight);
            laterButton = new Rectangle(viewButton.Right + Px(8), y, laterWidth, buttonHeight);
            int height = y + buttonHeight + pad;
            int close = Px(24);
            closeButton = new Rectangle(width - Px(8) - close, Px(8), close, close);

            // Esquina inferior derecha del área de trabajo (encima de la bandeja si la barra está abajo)
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int margin = Px(12);
            Bounds = new Rectangle(area.Right - width - margin, area.Bottom - height - margin, width, height);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            // Windows 11: esquinas redondeadas y borde fino, como el menú de la bandeja
            int corners = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            int border = ColorTranslator.ToWin32(SettingsForm.Edge);
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            fadeClock.Restart();
            fadeTimer.Start();
            closeTimer.Start();
        }

        private void FadeOut() {
            if (fadingOut) return;
            closeTimer.Stop();
            fadingOut = true;
            fadeClock.Restart();
            fadeTimer.Start();
        }

        private void OnFade() {
            double t = Math.Min(1, fadeClock.Elapsed.TotalMilliseconds / FadeMs);
            Opacity = fadingOut ? 1 - t : t;
            if (t < 1) return;
            fadeTimer.Stop();
            if (fadingOut) Close();
        }

        protected override void OnMouseEnter(EventArgs e) {
            base.OnMouseEnter(e);
            // Mientras se lee no se cierra
            closeTimer.Stop();
            if (fadingOut) {
                fadingOut = false;
                fadeTimer.Stop();
                Opacity = 1;
            }
        }

        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            SetHover(Rectangle.Empty);
            closeTimer.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            Rectangle over = new[] { viewButton, laterButton, closeButton }.FirstOrDefault(r => r.Contains(e.Location));
            SetHover(over);
            Cursor = over.IsEmpty ? Cursors.Default : Cursors.Hand;
        }

        private void SetHover(Rectangle r) {
            if (r == hover) return;
            hover = r;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e) {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (viewButton.Contains(e.Location)) {
                Close();
                ViewRequested?.Invoke();
            } else if (laterButton.Contains(e.Location) || closeButton.Contains(e.Location)) {
                FadeOut();
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Borde fino en Windows 10 (en Windows 11 lo pone el sistema)
            if (Environment.OSVersion.Version.Build < 22000) {
                using var edge = new Pen(SettingsForm.Edge);
                g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
            }

            int pad = Px(16);
            g.DrawImage(logo, new Rectangle(pad, pad, LogoSize, LogoSize));

            int textLeft = viewButton.Left;
            Size titleSize = TextRenderer.MeasureText(title, titleFont);
            TextRenderer.DrawText(g, title, titleFont, new Point(textLeft - Px(2), pad), SettingsForm.TextColor, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, body, bodyFont, new Point(textLeft - Px(2), pad + titleSize.Height + Px(4)), SettingsForm.SubText, TextFormatFlags.NoPrefix);

            DrawButton(g, viewButton, "Ver la versión", primary: true);
            DrawButton(g, laterButton, "Ahora no", primary: false);

            // Cruz de cerrar
            if (hover == closeButton) {
                using var back = new SolidBrush(SettingsForm.SurfaceHover);
                using GraphicsPath circle = CaptureOverlay.RoundedRect(closeButton, closeButton.Width / 2f);
                g.FillPath(back, circle);
            }
            float c = Px(4.5f);
            float cx = closeButton.X + closeButton.Width / 2f, cy = closeButton.Y + closeButton.Height / 2f;
            using (var x = new Pen(hover == closeButton ? SettingsForm.TextColor : SettingsForm.SubText, 1.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                g.DrawLine(x, cx - c, cy - c, cx + c, cy + c);
                g.DrawLine(x, cx + c, cy - c, cx - c, cy + c);
            }
        }

        /// <summary>Botón redondeado como los de la configuración: azul el principal y gris oscuro el otro.</summary>
        private void DrawButton(Graphics g, Rectangle r, string text, bool primary) {
            bool over = hover == r;
            Color fill = primary ? (over ? SettingsForm.AccentHover : SettingsForm.Accent) : (over ? SettingsForm.SurfaceHover : SettingsForm.Surface);
            var rect = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            using (GraphicsPath path = CaptureOverlay.RoundedRect(rect, 6 * scale)) {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                if (!primary) {
                    using var pen = new Pen(SettingsForm.Edge);
                    g.DrawPath(pen, path);
                }
            }
            TextRenderer.DrawText(g, text, buttonFont, r, primary ? Color.White : SettingsForm.TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                closeTimer.Dispose();
                fadeTimer.Dispose();
                titleFont.Dispose();
                bodyFont.Dispose();
                buttonFont.Dispose();
                logo.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
