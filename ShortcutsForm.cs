using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Ayuda: todos los atajos de Trazo en dos columnas, con el mismo aspecto oscuro que la configuración
    /// y cada tecla dibujada como una tecla.
    /// </summary>
    internal sealed class ShortcutsForm : DarkDialog {

        /// <summary>
        /// Una fila: descripción y teclas. En las teclas, "+" une las que se pulsan a la vez y "|" separa alternativas.
        /// </summary>
        private sealed class Row {
            public Row(string text, string keys) { Text = text; Keys = keys; }
            public string Text { get; }
            public string Keys { get; }
        }

        private sealed class Section {
            public Section(string title, Row[] rows) { Title = title; Rows = rows; }
            public string Title { get; }
            public Row[] Rows { get; }
        }

        private readonly Section[][] columns;
        private readonly Font textFont;
        private readonly Font titleFont;
        private readonly Font keyFont;
        private readonly Font headerFont;

        public ShortcutsForm(Keys captureHotkey, Icon icon) {
            string capture = HotkeyText.Format(captureHotkey).Replace(" + ", "+");
            columns = new[] {
                new[] {
                    new Section("Captura", new[] {
                        new Row("Hacer una captura", capture),
                        new Row("Elegir el monitor entero", "Clic sin arrastrar"),
                        new Row("Leer el texto de una zona y copiarlo", "Arrastrar con clic derecho"),
                        new Row("Copiar el color bajo el cursor (antes de elegir zona)", "Ctrl+C"),
                        new Row("Cerrar sin guardar", "Esc|Clic derecho"),
                    }),
                    new Section("Herramientas", new[] {
                        new Row("Rectángulo", "R"),
                        new Row("Elipse", "E"),
                        new Row("Flecha", "A"),
                        new Row("Pintar flecha (otra vez: recto o redondeado)", "F"),
                        new Row("Línea", "L"),
                        new Row("Texto", "T"),
                        new Row("Pixelar", "P"),
                        new Row("Cuentagotas", "I"),
                        new Row("Leer texto (OCR)", "O"),
                    }),
                },
                new[] {
                    new Section("Edición", new[] {
                        new Row("Deshacer", "Ctrl+Z"),
                        new Row("Duplicar el elemento seleccionado", "Ctrl+D"),
                        new Row("Borrar el elemento seleccionado", "Supr"),
                        new Row("Mover el elemento seleccionado", "Flechas"),
                        new Row("Cambiar su tamaño (borde derecho o inferior)", "Mayús+Flechas"),
                        new Row("Grosor, o tamaño del texto", "Ctrl+Rueda"),
                        new Row("Recto, cuadrado o círculo al dibujar", "Mayús"),
                        new Row("Mostrar u ocultar reglas y guías", "Ctrl+R"),
                        new Row("Mover sin ajustarse a las guías", "Alt"),
                        new Row("Seleccionar un elemento para moverlo", "Doble clic"),
                    }),
                    new Section("Terminar", new[] {
                        new Row("Copiar la imagen y cerrar", "Ctrl+C|Intro"),
                        new Row("Guardar como imagen", "Ctrl+S"),
                    }),
                },
            };

            Text = "Atajos de teclado de Trazo";
            Icon = icon;
            ShowInTaskbar = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            float s = DeviceDpi / 96f;
            textFont = NewFont("Segoe UI", 9.5f * 96f / 72f * s, GraphicsUnit.Pixel);
            titleFont = NewFont("Segoe UI Semibold", 10f * 96f / 72f * s, GraphicsUnit.Pixel);
            keyFont = NewFont("Segoe UI", 8.5f * 96f / 72f * s, GraphicsUnit.Pixel);
            headerFont = NewFont("Segoe UI Semibold", 14f * 96f / 72f * s, GraphicsUnit.Pixel);

            ClientSize = LayoutContent(null);
        }

        private float UiScale => DeviceDpi / 96f;
        private int Px(float logical) => (int)Math.Round(logical * UiScale);

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.Escape) {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor);
            LayoutContent(e.Graphics);
        }

        /// <summary>Coloca (y si hay <paramref name="g"/>, dibuja) todo el contenido; devuelve el tamaño que ocupa.</summary>
        private Size LayoutContent(Graphics? g) {
            int pad = Px(24), columnGap = Px(36), rowHeight = Px(30), descGap = Px(16);
            int y0 = pad;

            if (g != null) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                TextRenderer.DrawText(g, "Atajos de teclado", headerFont, new Point(pad - Px(2), y0), SettingsForm.TextColor, TextFormatFlags.NoPadding);
            }
            y0 += TextRenderer.MeasureText("Atajos de teclado", headerFont).Height + Px(10);
            string hint = "Funcionan en la captura después de elegir la zona, salvo los indicados.";
            if (g != null) TextRenderer.DrawText(g, hint, textFont, new Point(pad, y0), SettingsForm.SubText, TextFormatFlags.NoPadding);
            y0 += TextRenderer.MeasureText(hint, textFont).Height + Px(18);

            int x = pad, bottom = y0, right = pad + TextRenderer.MeasureText(hint, textFont).Width;
            foreach (Section[] column in columns) {
                // Ancho de la columna: la descripción más larga más las teclas más anchas
                int descWidth = column.SelectMany(c => c.Rows).Max(r => TextRenderer.MeasureText(r.Text, textFont).Width);
                int keysWidth = column.SelectMany(c => c.Rows).Max(r => KeysWidth(r.Keys));
                int width = descWidth + descGap + keysWidth;
                int y = y0;

                foreach (Section section in column) {
                    if (g != null) TextRenderer.DrawText(g, section.Title.ToUpperInvariant(), titleFont, new Point(x, y), SettingsForm.SubText, TextFormatFlags.NoPadding);
                    y += TextRenderer.MeasureText(section.Title, titleFont).Height + Px(6);

                    foreach (Row row in section.Rows) {
                        if (g != null) {
                            var desc = new Rectangle(x, y, descWidth, rowHeight);
                            TextRenderer.DrawText(g, row.Text, textFont, desc, SettingsForm.TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                            DrawKeys(g, row.Keys, x + width - KeysWidth(row.Keys), y, rowHeight);
                            // Línea fina entre filas
                            using var line = new Pen(Color.FromArgb(40, 255, 255, 255));
                            g.DrawLine(line, x, y + rowHeight, x + width, y + rowHeight);
                        }
                        y += rowHeight + 1;
                    }
                    y += Px(18);
                }
                bottom = Math.Max(bottom, y);
                x += width + columnGap;
                right = Math.Max(right, x - columnGap);
            }
            return new Size(right + pad, bottom - Px(18) + pad);
        }

        private int KeyHeight => Px(22);
        private int KeyPad => Px(7);
        private int KeyGap => Px(4);

        /// <summary>Partes de las teclas: cada alternativa es una lista de teclas.</summary>
        private static IEnumerable<string[]> Alternatives(string keys) => keys.Split('|').Select(a => a.Split('+'));

        private int KeyWidth(string key) => Math.Max(KeyHeight, Measure(key) + KeyPad * 2);

        private int SeparatorWidth(string text) => Measure(text);

        private int Measure(string text) => TextRenderer.MeasureText(text, keyFont, Size.Empty, TextFormatFlags.NoPadding).Width;

        private int KeysWidth(string keys) {
            int w = 0;
            bool firstAlt = true;
            foreach (string[] alt in Alternatives(keys)) {
                if (!firstAlt) w += SeparatorWidth("o") + KeyGap * 2;
                firstAlt = false;
                for (int i = 0; i < alt.Length; i++) {
                    if (i > 0) w += SeparatorWidth("+") + KeyGap * 2;
                    w += KeyWidth(alt[i]);
                }
            }
            return w;
        }

        private void DrawKeys(Graphics g, string keys, int x, int rowY, int rowHeight) {
            int y = rowY + (rowHeight - KeyHeight) / 2;
            bool firstAlt = true;
            foreach (string[] alt in Alternatives(keys)) {
                if (!firstAlt) x = DrawSeparator(g, "o", x, rowY, rowHeight);
                firstAlt = false;
                for (int i = 0; i < alt.Length; i++) {
                    if (i > 0) x = DrawSeparator(g, "+", x, rowY, rowHeight);
                    int w = KeyWidth(alt[i]);
                    var r = new RectangleF(x + 0.5f, y + 0.5f, w - 1, KeyHeight - 1);
                    using (GraphicsPath path = Ui.RoundedRect(r, 5 * UiScale)) {
                        using var fill = new SolidBrush(SettingsForm.Surface);
                        g.FillPath(fill, path);
                        using var edge = new Pen(SettingsForm.Edge);
                        g.DrawPath(edge, path);
                    }
                    // Sombra inferior: aspecto de tecla
                    using (var shadow = new Pen(Color.FromArgb(90, 0, 0, 0), 1.5f)) {
                        float r5 = 5 * UiScale;
                        g.DrawLine(shadow, r.Left + r5, r.Bottom + 0.75f, r.Right - r5, r.Bottom + 0.75f);
                    }
                    TextRenderer.DrawText(g, alt[i], keyFont, new Rectangle(x, y, w, KeyHeight), SettingsForm.TextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += w;
                }
            }
        }

        private int DrawSeparator(Graphics g, string text, int x, int rowY, int rowHeight) {
            x += KeyGap;
            int w = SeparatorWidth(text);
            TextRenderer.DrawText(g, text, keyFont, new Rectangle(x, rowY, w, rowHeight), SettingsForm.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return x + w + KeyGap;
        }
    }
}
