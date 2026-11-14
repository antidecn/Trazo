namespace Trazo {

    /// <summary>
    /// Aviso que sobrevive al cierre de la captura (color copiado, texto leído), arriba y centrado en la pantalla.
    /// No roba el foco, no recibe clics y se cierra solo. Solo hay uno a la vez: el nuevo sustituye al anterior.
    /// </summary>
    internal sealed class Toast : Form {

        private const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        private static Toast? current;

        private readonly CardStyle style;
        private readonly Action<Graphics, CardStyle, Rectangle> paint;
        private readonly System.Windows.Forms.Timer closeTimer = new();

        public static void ShowMessage(string message, Point near, Color? swatch = null) =>
            new Toast(near, s => s.MeasureMessage(message, swatch != null), 2500, 235,
                (g, s, r) => s.DrawMessage(g, r, message, swatch, CardStyle.Back)).ShowAlone();

        public static void ShowOcr(string text, Point near) =>
            // Más tiempo cuanto más largo es el texto; casi opaca para que lo de debajo no se mezcle con él
            new Toast(near, s => s.MeasureOcr(text), Math.Max(3500, Math.Min(8000, 2500 + text.Length * 25)), 250,
                (g, s, r) => s.DrawOcr(g, r, text, null, CardStyle.Back)).ShowAlone();

        /// <summary>Cierra el aviso visible, si lo hay. Devuelve true si había uno.</summary>
        public static bool CloseCurrent() {
            Toast? toast = current;
            current = null;
            if (toast == null || toast.IsDisposed) return false;
            toast.Close();
            return true;
        }

        private Toast(Point near, Func<CardStyle, Size> measure, int visibleMs, int opacity, Action<Graphics, CardStyle, Rectangle> paint) {
            this.paint = paint;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = CardStyle.Back;
            Opacity = opacity / 255.0;
            DoubleBuffered = true;

            style = new CardStyle(DeviceDpi / 96f);
            Bounds = style.TopCenter(Screen.FromPoint(near).Bounds, measure(style));
            // La tarjeta se dibuja con las esquinas redondeadas; la ventana se recorta igual
            using (var path = Ui.RoundedRect(new Rectangle(Point.Empty, Size), (int)(10 * DeviceDpi / 96f))) {
                Region = new Region(path);
            }

            closeTimer.Interval = visibleMs;
            closeTimer.Tick += (_, _) => Close();
        }

        private void ShowAlone() {
            CloseCurrent();
            current = this;
            closeTimer.Start();
            Show();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e) => paint(e.Graphics, style, ClientRectangle);

        protected override void OnFormClosed(FormClosedEventArgs e) {
            if (current == this) current = null;
            closeTimer.Dispose();
            style.Dispose();
            base.OnFormClosed(e);
            Dispose();
        }
    }
}
