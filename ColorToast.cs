using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Aviso flotante con una muestra de color que sobrevive al cierre de la captura.
    /// No roba el foco ni recibe clics, y se cierra solo.
    /// </summary>
    internal sealed class ColorToast : Form {

        private const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        private readonly string message;
        private readonly Color swatchColor;
        private readonly Font font;
        private readonly float scale;
        private readonly System.Windows.Forms.Timer closeTimer = new() { Interval = 2500 };

        public static void Show(string message, Color swatch, Point near) => new ColorToast(message, swatch, near).Show();

        private ColorToast(string message, Color swatch, Point near) {
            this.message = message;
            swatchColor = swatch;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(28, 28, 30);
            Opacity = 235 / 255.0;
            DoubleBuffered = true;

            Rectangle area = Screen.FromPoint(near).Bounds;
            scale = DeviceDpi / 96f;
            font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            Size text = TextRenderer.MeasureText(message, font);
            int padX = (int)(14 * scale), padY = (int)(8 * scale);
            int width = text.Width + SwatchSize + padX / 2 * 2 + padX * 2;
            int height = Math.Max(text.Height, SwatchSize) + padY * 2;
            Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (int)(16 * scale), width, height);

            using (GraphicsPath path = CaptureOverlay.RoundedRect(new Rectangle(0, 0, width, height), (int)(8 * scale))) {
                Region = new Region(path);
            }

            closeTimer.Tick += (_, _) => Close();
            closeTimer.Start();
        }

        private int SwatchSize => (int)(26 * scale);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int padX = (int)(14 * scale);
            var swatch = new Rectangle(padX, (ClientSize.Height - SwatchSize) / 2, SwatchSize, SwatchSize);
            using (GraphicsPath path = CaptureOverlay.RoundedRect(swatch, (int)(5 * scale)))
            using (var fill = new SolidBrush(swatchColor))
            using (var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            var textRect = Rectangle.FromLTRB(swatch.Right + padX / 2, 0, ClientSize.Width - padX / 2, ClientSize.Height);
            TextRenderer.DrawText(g, message, font, textRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            closeTimer.Dispose();
            font.Dispose();
            base.OnFormClosed(e);
            Dispose();
        }
    }
}
