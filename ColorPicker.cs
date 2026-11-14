using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Selector de color con el aspecto de Trazo: cuadro de saturación y brillo, barra de tono,
    /// código hexadecimal editable, paleta de colores habituales y los últimos colores usados.
    /// </summary>
    internal sealed class ColorPicker : Form {

        public const int MaxRecent = 10;

        private static readonly Color[] Palette = {
            Color.FromArgb(230, 30, 30), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 200, 0),
            Color.FromArgb(40, 180, 80), Color.FromArgb(0, 150, 255), Color.FromArgb(40, 60, 200),
            Color.FromArgb(150, 60, 220), Color.FromArgb(240, 80, 160), Color.White, Color.Black,
        };

        private readonly Color original;
        private readonly IList<string> recent;
        private readonly List<Color> recentColors = new();

        // El color se guarda en HSV para no perder el tono al llevarlo al gris, al blanco o al negro
        private float hue, sat, val;

        private readonly TextBox hexBox;
        private readonly SettingsForm.DarkButton okButton;
        private readonly SettingsForm.DarkButton cancelButton;
        private bool updatingHex;

        private float scale = 1;
        private Font? captionFont;
        private Font? buttonFont;
        private Font? hexFont;
        private Rectangle svRect, hueRect, previewRect, hexFrame;
        private Rectangle paletteCaption, recentCaption;
        private readonly List<(Rectangle Rect, Color Color)> swatches = new();
        private Bitmap? svBitmap;
        private float svBitmapHue = -1;
        private enum Drag { None, SatVal, Hue }
        private Drag dragging;
        private int hoverSwatch = -1;

        public Color Color => FromHsv(hue, sat, val);

        /// <param name="current">Color con el que se abre.</param>
        /// <param name="recent">Últimos colores (#RRGGBB, el más reciente primero); al aceptar se añade el elegido.</param>
        public ColorPicker(Color current, IList<string> recent) {
            original = Color.FromArgb(current.R, current.G, current.B);
            this.recent = recent;
            foreach (string hex in recent) {
                if (Ui.TryParseHex(hex, out Color c)) recentColors.Add(c);
            }
            ToHsv(original, out hue, out sat, out val);

            Text = "Elegir color";
            // Sin barra de título ni botón de cerrar: basta con Aceptar y Cancelar (y Esc)
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = SettingsForm.Back;
            ForeColor = SettingsForm.TextColor;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            hexBox = new TextBox {
                BorderStyle = BorderStyle.None,
                BackColor = SettingsForm.Surface,
                ForeColor = SettingsForm.TextColor,
                MaxLength = 7,
                CharacterCasing = CharacterCasing.Upper,
            };
            hexBox.TextChanged += (_, _) => HexEdited();
            hexBox.Leave += (_, _) => UpdateHex();
            hexBox.GotFocus += (_, _) => Invalidate();
            hexBox.LostFocus += (_, _) => Invalidate();
            hexBox.KeyPress += (_, e) => {
                // Solo cifras hexadecimales (y la almohadilla); se deja pasar lo de control (borrar, pegar…)
                if (!char.IsControl(e.KeyChar) && !Uri.IsHexDigit(e.KeyChar) && e.KeyChar != '#') e.Handled = true;
            };

            okButton = new SettingsForm.DarkButton("Aceptar", primary: true);
            okButton.Click += (_, _) => Accept();
            cancelButton = new SettingsForm.DarkButton("Cancelar", primary: false) { DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { hexBox, okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
            UpdateHex();
        }

        /// <summary>Abre el selector junto al cursor y devuelve el color elegido, o null si se cancela.</summary>
        public static Color? Pick(IWin32Window owner, Color current, IList<string> recent) {
            using var picker = new ColorPicker(current, recent);
            // Junto al cursor, dentro de la pantalla en la que está (la captura cubre todos los monitores)
            Point cursor = Cursor.Position;
            picker.Location = new Point(cursor.X + 12, cursor.Y + 12);
            return picker.ShowDialog(owner) == DialogResult.OK ? picker.Color : null;
        }

        /// <summary>Añade el color al principio de la lista de recientes, sin repetirlo.</summary>
        public static void Remember(IList<string> recent, Color color) {
            string hex = Ui.ToHex(color);
            for (int i = recent.Count - 1; i >= 0; i--) {
                if (string.Equals(recent[i], hex, StringComparison.OrdinalIgnoreCase)) recent.RemoveAt(i);
            }
            recent.Insert(0, hex);
            while (recent.Count > MaxRecent) recent.RemoveAt(recent.Count - 1);
        }

        private void Accept() {
            if (!Ui.TryParseHex(hexBox.Text, out _)) UpdateHex();
            Remember(recent, Color);
            DialogResult = DialogResult.OK;
        }

        // ───────────────────────────── Disposición ─────────────────────────────

        protected override CreateParams CreateParams {
            get {
                const int CS_DROPSHADOW = 0x20000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Ui.StylePopup(Handle);
        }

        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);
            ArrangeControls();
            KeepOnScreen();
            hexBox.Focus();
            hexBox.SelectAll();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e) {
            base.OnDpiChanged(e);
            ArrangeControls();
        }

        private int S(float logical) => (int)Math.Round(logical * scale);

        private void ArrangeControls() {
            scale = DeviceDpi / 96f;
            captionFont?.Dispose();
            captionFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            hexFont?.Dispose();
            hexBox.Font = hexFont = new Font("Consolas", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            buttonFont?.Dispose();
            okButton.Font = cancelButton.Font = buttonFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            int pad = S(16), gap = S(10);
            int swatch = S(22), swatchGap = S(6);
            int contentWidth = Palette.Length * swatch + (Palette.Length - 1) * swatchGap;
            int hueWidth = S(16);

            svRect = new Rectangle(pad, pad, contentWidth - hueWidth - gap, S(170));
            hueRect = new Rectangle(svRect.Right + gap, pad, hueWidth, svRect.Height);

            // Muestra (nuevo arriba, original abajo) y código hexadecimal
            int rowY = svRect.Bottom + S(14);
            int rowH = S(34);
            previewRect = new Rectangle(pad, rowY, S(56), rowH);
            hexFrame = new Rectangle(previewRect.Right + gap, rowY, contentWidth - previewRect.Width - gap, rowH);
            int boxH = hexBox.PreferredHeight;
            hexBox.SetBounds(hexFrame.X + S(10), hexFrame.Y + (rowH - boxH) / 2, hexFrame.Width - S(20), boxH);

            // Paleta y recientes
            swatches.Clear();
            int y = previewRect.Bottom + S(14);
            int captionH = S(20);
            paletteCaption = new Rectangle(pad, y, contentWidth, captionH);
            y += captionH;
            for (int i = 0; i < Palette.Length; i++) {
                swatches.Add((new Rectangle(pad + i * (swatch + swatchGap), y, swatch, swatch), Palette[i]));
            }
            y += swatch;
            if (recentColors.Count > 0) {
                y += S(10);
                recentCaption = new Rectangle(pad, y, contentWidth, captionH);
                y += captionH;
                for (int i = 0; i < recentColors.Count; i++) {
                    swatches.Add((new Rectangle(pad + i * (swatch + swatchGap), y, swatch, swatch), recentColors[i]));
                }
                y += swatch;
            } else {
                recentCaption = Rectangle.Empty;
            }

            // Botones abajo a la derecha (se miden solos al crearse)
            y += S(18);
            okButton.CreateControl();
            cancelButton.CreateControl();
            int buttonH = S(32);
            okButton.Size = new Size(Math.Max(okButton.Width, S(92)), buttonH);
            cancelButton.Size = new Size(Math.Max(cancelButton.Width, S(92)), buttonH);
            okButton.Location = new Point(pad + contentWidth - okButton.Width, y);
            cancelButton.Location = new Point(okButton.Left - S(8) - cancelButton.Width, y);

            ClientSize = new Size(pad * 2 + contentWidth, y + buttonH + pad);
            svBitmapHue = -1;
            Invalidate();
        }

        private void KeepOnScreen() {
            Rectangle area = Screen.FromPoint(Location).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            Location = new Point(x, y);
        }

        // ───────────────────────────── Dibujo ─────────────────────────────

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = 6 * scale;

            // Cuadro de saturación (horizontal) y brillo (vertical) para el tono actual
            if (svBitmap == null || svBitmapHue != hue || svBitmap.Size != svRect.Size) RenderSatVal();
            using (GraphicsPath path = Ui.RoundedRect(svRect, radius)) {
                g.SetClip(path);
                g.DrawImageUnscaled(svBitmap!, svRect.Location);
                g.ResetClip();
                using var edge = new Pen(SettingsForm.Edge, 1f);
                g.DrawPath(edge, path);
            }
            float mx = svRect.X + sat * (svRect.Width - 1);
            float my = svRect.Y + (1 - val) * (svRect.Height - 1);
            DrawRing(g, new PointF(mx, my), 7 * scale, Color);

            // Barra de tono
            using (GraphicsPath path = Ui.RoundedRect(hueRect, hueRect.Width / 2f)) {
                using var brush = new LinearGradientBrush(hueRect, Color.Red, Color.Red, LinearGradientMode.Vertical);
                var blend = new ColorBlend(7);
                for (int i = 0; i < 7; i++) {
                    blend.Colors[i] = FromHsv(i * 60 % 360, 1, 1);
                    blend.Positions[i] = i / 6f;
                }
                blend.Colors[6] = Color.Red;
                brush.InterpolationColors = blend;
                g.FillPath(brush, path);
            }
            float hy = hueRect.Y + hue / 360f * (hueRect.Height - 1);
            DrawRing(g, new PointF(hueRect.X + hueRect.Width / 2f, hy), hueRect.Width / 2f + 1 * scale, FromHsv(hue, 1, 1));

            // Muestra: arriba el color nuevo, abajo el original
            using (GraphicsPath path = Ui.RoundedRect(previewRect, radius)) {
                g.SetClip(path);
                int half = previewRect.Height / 2;
                using (var b = new SolidBrush(Color)) g.FillRectangle(b, previewRect.X, previewRect.Y, previewRect.Width, half);
                using (var b = new SolidBrush(original)) g.FillRectangle(b, previewRect.X, previewRect.Y + half, previewRect.Width, previewRect.Height - half);
                g.ResetClip();
                using var edge = new Pen(SettingsForm.Edge, 1f);
                g.DrawPath(edge, path);
            }

            // Marco del código hexadecimal
            using (GraphicsPath path = Ui.RoundedRect(hexFrame, radius)) {
                using var back = new SolidBrush(SettingsForm.Surface);
                g.FillPath(back, path);
                using var edge = new Pen(hexBox.Focused ? SettingsForm.AccentLine : SettingsForm.Edge, 1f);
                g.DrawPath(edge, path);
            }

            TextRenderer.DrawText(g, "Colores", captionFont, paletteCaption, SettingsForm.SubText, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            if (!recentCaption.IsEmpty) {
                TextRenderer.DrawText(g, "Recientes", captionFont, recentCaption, SettingsForm.SubText, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            Color current = Color;
            for (int i = 0; i < swatches.Count; i++) {
                (Rectangle rect, Color c) = swatches[i];
                using GraphicsPath path = Ui.RoundedRect(new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1, rect.Height - 1), 5 * scale);
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
                bool chosen = c.ToArgb() == current.ToArgb();
                Color ink = chosen || i == hoverSwatch ? Color.White : Color.FromArgb(70, 255, 255, 255);
                using var pen = new Pen(ink, chosen ? 2f * scale : 1f);
                g.DrawPath(pen, path);
            }
        }

        /// <summary>Anillo blanco con contorno oscuro, relleno del color que marca.</summary>
        private static void DrawRing(Graphics g, PointF c, float r, Color fill) {
            var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, rect);
            using (var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 3.5f)) g.DrawEllipse(shadow, rect);
            using (var ring = new Pen(Color.White, 2f)) g.DrawEllipse(ring, rect);
        }

        private void RenderSatVal() {
            svBitmap?.Dispose();
            svBitmap = new Bitmap(Math.Max(1, svRect.Width), Math.Max(1, svRect.Height));
            using Graphics g = Graphics.FromImage(svBitmap);
            var r = new Rectangle(Point.Empty, svBitmap.Size);
            using (var b = new SolidBrush(FromHsv(hue, 1, 1))) g.FillRectangle(b, r);
            // Rectángulo un píxel más grande: así el degradado no se repite en el último píxel
            var grow = new Rectangle(-1, -1, r.Width + 2, r.Height + 2);
            using (var white = new LinearGradientBrush(grow, Color.White, Color.FromArgb(0, Color.White), LinearGradientMode.Horizontal)) g.FillRectangle(white, r);
            using (var black = new LinearGradientBrush(grow, Color.FromArgb(0, Color.Black), Color.Black, LinearGradientMode.Vertical)) g.FillRectangle(black, r);
            svBitmapHue = hue;
        }

        // ───────────────────────────── Ratón ─────────────────────────────

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Rectangle svHit = Rectangle.Inflate(svRect, S(4), S(4));
            Rectangle hueHit = Rectangle.Inflate(hueRect, S(4), S(4));
            if (svHit.Contains(e.Location)) {
                dragging = Drag.SatVal;
                Capture = true;
                DragTo(e.Location);
            } else if (hueHit.Contains(e.Location)) {
                dragging = Drag.Hue;
                Capture = true;
                DragTo(e.Location);
            } else {
                int i = SwatchAt(e.Location);
                if (i < 0) return;
                SetColor(swatches[i].Color);
                // Doble clic en una muestra: se elige y se cierra
                if (e.Clicks >= 2) Accept();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (dragging != Drag.None) {
                DragTo(e.Location);
                return;
            }
            int i = SwatchAt(e.Location);
            if (i != hoverSwatch) {
                hoverSwatch = i;
                Invalidate();
            }
            Cursor = i >= 0 || svRect.Contains(e.Location) || hueRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            dragging = Drag.None;
            Capture = false;
        }

        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            if (hoverSwatch >= 0) {
                hoverSwatch = -1;
                Invalidate();
            }
        }

        private int SwatchAt(Point p) => swatches.FindIndex(s => s.Rect.Contains(p));

        private void DragTo(Point p) {
            if (dragging == Drag.SatVal) {
                sat = Clamp01((p.X - svRect.X) / (float)(svRect.Width - 1));
                val = 1 - Clamp01((p.Y - svRect.Y) / (float)(svRect.Height - 1));
            } else if (dragging == Drag.Hue) {
                hue = Clamp01((p.Y - hueRect.Y) / (float)(hueRect.Height - 1)) * 360f;
                if (hue >= 360) hue = 359.9f;
            }
            UpdateHex();
            Invalidate();
            Update();
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ───────────────────────────── Código hexadecimal ─────────────────────────────

        /// <param name="updateHex">False mientras se escribe en el cuadro del código, para no pisar lo escrito.</param>
        private void SetColor(Color c, bool updateHex = true) {
            float oldHue = hue;
            ToHsv(c, out hue, out sat, out val);
            // Grises: se conserva el tono que hubiera para que la barra no salte
            if (sat == 0) hue = oldHue;
            if (updateHex) UpdateHex();
            Invalidate();
        }

        private void UpdateHex() {
            updatingHex = true;
            hexBox.Text = Ui.ToHex(Color);
            hexBox.SelectionStart = hexBox.TextLength;
            updatingHex = false;
        }

        private void HexEdited() {
            if (!updatingHex && Ui.TryParseHex(hexBox.Text, out Color c)) SetColor(c, updateHex: false);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            // Ctrl+V desde cualquier parte: pega un código copiado (p. ej. con el cuentagotas)
            if (e.KeyData == (Keys.Control | Keys.V) && !hexBox.Focused && Clipboard.ContainsText()
                && Ui.TryParseHex(Clipboard.GetText().Trim(), out Color c)) {
                SetColor(c);
                e.Handled = true;
            }
        }

        // ───────────────────────────── HSV ─────────────────────────────

        private static void ToHsv(Color c, out float h, out float s, out float v) {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            v = max;
            s = max == 0 ? 0 : d / max;
            if (d == 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d + 6) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }

        private static Color FromHsv(float h, float s, float v) {
            float c = v * s;
            float x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            float m = v - c;
            float r, g, b;
            switch ((int)(h / 60) % 6) {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }
            return Color.FromArgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static int ToByte(float f) => Math.Max(0, Math.Min(255, (int)Math.Round(f * 255)));

        protected override void Dispose(bool disposing) {
            if (disposing) {
                svBitmap?.Dispose();
                captionFont?.Dispose();
                buttonFont?.Dispose();
                hexFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
