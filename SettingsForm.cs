using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Configuración con el mismo aspecto que la barra de herramientas de la captura:
    /// fondo oscuro, texto claro, acento azul y controles redondeados dibujados por código.
    /// </summary>
    internal sealed class SettingsForm : Form {

        // Colores compartidos por todos los controles de la ventana
        internal static readonly Color Back = Color.FromArgb(28, 28, 30);
        internal static readonly Color Surface = Color.FromArgb(44, 44, 48);
        internal static readonly Color SurfaceHover = Color.FromArgb(58, 58, 63);
        internal static readonly Color SurfacePressed = Color.FromArgb(70, 70, 76);
        internal static readonly Color Edge = Color.FromArgb(70, 70, 76);
        internal static readonly Color TextColor = Icons.LightInk;
        internal static readonly Color SubText = Color.FromArgb(160, 160, 166);
        // Lo destacado (botón principal, casillas marcadas) va en gris claro, como los botones activos de la barra
        // de la captura, en vez de en un color de acento
        internal static readonly Color Accent = Color.FromArgb(88, 88, 94);
        internal static readonly Color AccentHover = Color.FromArgb(102, 102, 108);
        internal static readonly Color AccentPressed = Color.FromArgb(74, 74, 80);
        /// <summary>Borde de lo elegido o con el foco, y enlaces: gris claro que destaca sobre las superficies.</summary>
        internal static readonly Color AccentLine = Color.FromArgb(185, 185, 190);

        private readonly Func<Keys, bool> tryApply;
        private readonly HotkeyBox hotkeyBox;
        private readonly DarkCheckBox startupCheck;
        private readonly DarkCheckBox guidesCheck;
        private readonly DarkCheckBox updatesCheck;
        private readonly OptionPicker radiusPicker;
        private readonly ToolTip colorTip = new();
        private readonly SwatchButton borderColorButton;
        private readonly OptionPicker borderWidthPicker;
        private readonly List<Font> fonts = new();

        public Keys Hotkey => hotkeyBox.Hotkey;
        public bool StartWithWindows => startupCheck.Checked;
        public bool ShowGuides => guidesCheck.Checked;
        public bool NotifyUpdates => updatesCheck.Checked;
        public bool RoundCorners => radiusPicker.Value > 0;
        public int CornerRadius => radiusPicker.Value;
        public bool AddBorder => borderWidthPicker.Value > 0;
        public Color BorderColor => borderColorButton.Color;
        public int BorderWidth => borderWidthPicker.Value;

        /// <param name="tryApply">Intenta registrar el atajo; devuelve false si está ocupado.</param>
        /// <param name="output">Opciones actuales del aspecto de la captura (solo se leen).</param>
        /// <param name="suspendHotkey">Libera el atajo global mientras se escribe uno nuevo en el cuadro.</param>
        /// <param name="resumeHotkey">Vuelve a activar el atajo guardado al salir del cuadro.</param>
        /// <param name="checkForUpdates">Busca una versión nueva; se completa al terminar.</param>
        public SettingsForm(Keys current, bool startWithWindows, AppSettings output, Icon icon,
                            Func<Keys, bool> tryApply, Action suspendHotkey, Action resumeHotkey,
                            Func<Task> checkForUpdates) {
            this.tryApply = tryApply;

            Text = "Configuración de Trazo";
            Icon = icon;
            Font = NewFont("Segoe UI", 9f);
            BackColor = Back;
            ForeColor = TextColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(LogicalToDeviceUnits(20));

            var layout = new TableLayoutPanel {
                AutoSize = true,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
            };

            // Cabecera: logo, nombre y versión
            int logoSize = LogicalToDeviceUnits(48);
            using Icon bigIcon = Icons.AppIcon(logoSize);
            var logo = new PictureBox {
                Image = bigIcon.ToBitmap(),
                Size = new Size(logoSize, logoSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 0, 12, 0),
            };
            var appName = new Label {
                Text = "Trazo",
                Font = NewFont("Segoe UI Semibold", 16f),
                AutoSize = true,
                Margin = Padding.Empty,
            };
            var version = new Label {
                Text = $"Versión {Application.ProductVersion.Split('+')[0]}",
                ForeColor = SubText,
                AutoSize = true,
                Margin = Padding.Empty,
            };
            var nameStack = new FlowLayoutPanel {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0),
            };
            nameStack.Controls.Add(appName);
            nameStack.Controls.Add(version);
            var header = new FlowLayoutPanel {
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            header.Controls.Add(logo);
            header.Controls.Add(nameStack);

            // Ayuda (los atajos de teclado) y buscar actualizaciones, a la derecha de la cabecera
            var helpButton = new DarkButton("Ayuda", primary: false, IconKind.Help) { Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0) };
            colorTip.SetToolTip(helpButton, "Ver los atajos de teclado de Trazo");
            helpButton.Click += (_, _) => {
                Keys shown = hotkeyBox != null && HotkeyBox.IsValid(hotkeyBox.Hotkey) ? hotkeyBox.Hotkey : current;
                using var shortcuts = new ShortcutsForm(shown, icon);
                shortcuts.ShowDialog(this);
            };
            var updateButton = new DarkButton("Buscar actualizaciones", primary: false, IconKind.Update) { Margin = Padding.Empty };
            updateButton.Click += async (_, _) => {
                updateButton.Enabled = false;
                updateButton.Text = "Buscando…";
                try {
                    await checkForUpdates();
                } finally {
                    if (!updateButton.IsDisposed) {
                        updateButton.Text = "Buscar actualizaciones";
                        updateButton.Enabled = true;
                    }
                }
            };
            var actions = new FlowLayoutPanel {
                AutoSize = true,
                WrapContents = false,
                // A la derecha y centrados en vertical con el logo
                Anchor = AnchorStyles.Right,
                Margin = Padding.Empty,
            };
            actions.Controls.Add(helpButton);
            actions.Controls.Add(updateButton);

            // Logo y nombre a la izquierda, botones a la derecha, de lado a lado de la ventana
            var top = new TableLayoutPanel {
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 22),
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(header, 0, 0);
            top.Controls.Add(actions, 1, 0);

            Label Title(string text) => new() {
                Text = text,
                Font = NewFont("Segoe UI Semibold", 10f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
            };
            Label Hint(string text, Padding margin) => new() {
                Text = text,
                AutoSize = true,
                ForeColor = SubText,
                Margin = margin,
            };

            var title = Title("Atajo para hacer una captura");

            hotkeyBox = new HotkeyBox(current, NewFont("Segoe UI", 11f)) {
                Size = new Size(LogicalToDeviceUnits(340), LogicalToDeviceUnits(36)),
                // Ocupa todo el ancho de su columna
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 0, 6),
            };
            hotkeyBox.GotFocus += (_, _) => suspendHotkey();
            hotkeyBox.LostFocus += (_, _) => resumeHotkey();

            var hint = Hint("Haz clic en el cuadro y pulsa la combinación que quieras.\n" +
                            "Debe incluir Ctrl, Alt o Mayús (salvo Impr Pant, Pausa o F1–F24).", new Padding(0, 0, 0, 12));

            var printScreenNote = new Label {
                Text = "Para usar Impr Pant sola, desactiva antes en Windows la opción\n" +
                       "\"Usar la tecla Impr Pant para abrir la captura de pantalla\".",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2),
            };
            var settingsLink = new LinkLabel {
                Text = "Abrir la configuración de teclado de Windows",
                AutoSize = true,
                LinkColor = AccentLine,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = AccentLine,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0, 0, 0, 18),
            };
            settingsLink.LinkClicked += (_, _) => {
                Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-keyboard") { UseShellExecute = true });
            };

            startupCheck = new DarkCheckBox("Iniciar Trazo al encender el ordenador", startWithWindows) {
                Margin = new Padding(0, 0, 0, 10),
            };

            // Aspecto de la imagen copiada o guardada
            var outputTitle = Title("Aspecto de la captura");

            // Cuatro opciones con icono en cada grupo; la primera lo desactiva
            int optionSize = LogicalToDeviceUnits(34);
            var cornersCaption = Hint("Esquinas", new Padding(0, 0, 0, 6));
            radiusPicker = new OptionPicker(CornerRadii, output.RoundCorners ? output.CornerRadius : 0,
                new[] { "Sin redondear", "Suaves", "Medias", "Muy redondeadas" }, DrawCornerIcon, optionSize) {
                Margin = Padding.Empty,
            };

            var borderCaption = Hint("Borde", new Padding(0, 0, 0, 6));
            borderWidthPicker = new OptionPicker(BorderWidths, output.AddBorder ? output.BorderWidth : 0,
                new[] { "Sin borde", "Fino", "Medio", "Grueso" }, DrawBorderIcon, optionSize) {
                Margin = new Padding(0, 0, 8, 0),
            };
            int swatch = LogicalToDeviceUnits(26);
            borderColorButton = new SwatchButton(output.BorderColorValue) {
                Size = new Size(swatch, swatch),
                Margin = new Padding(0, (optionSize - swatch) / 2, 0, 0),
            };
            colorTip.SetToolTip(borderColorButton, "Color del borde");
            borderColorButton.Click += (_, _) => {
                using var dialog = new ColorDialog { Color = borderColorButton.Color, FullOpen = true, AnyColor = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) borderColorButton.Color = dialog.Color;
            };
            var borderRow = new FlowLayoutPanel {
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            borderRow.Controls.AddRange(new Control[] { borderWidthPicker, borderColorButton });

            // Los dos grupos uno al lado del otro para que la ventana quede compacta
            FlowLayoutPanel Group(Control caption, Control options, Padding margin) {
                var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = margin };
                group.Controls.Add(caption);
                group.Controls.Add(options);
                return group;
            }
            var outputOptions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            outputOptions.Controls.Add(Group(cornersCaption, radiusPicker, new Padding(0, 0, LogicalToDeviceUnits(24), 0)));
            outputOptions.Controls.Add(Group(borderCaption, borderRow, Padding.Empty));

            var outputHint = Hint("Se aplica al copiar y al guardar. Las esquinas redondeadas\n" +
                                  "son transparentes en PNG y blancas en JPEG y BMP.", new Padding(0, 10, 0, 20));

            // Reglas y guías de alineación
            var drawingTitle = Title("Al dibujar");
            guidesCheck = new DarkCheckBox("Mostrar reglas y guías de alineación", output.ShowGuides) {
                Margin = new Padding(0, 0, 0, 4),
            };
            var guidesHint = Hint("Reglas en los bordes de la zona y líneas que ayudan a centrar\n" +
                                  "y alinear los elementos. En la captura, Ctrl+R las activa o desactiva.", new Padding(0, 0, 0, 20));

            // Aviso de versiones nuevas (una comprobación al día como mucho): solo avisa, nunca instala nada por su cuenta
            updatesCheck = new DarkCheckBox("Comprobar actualizaciones", output.NotifyUpdates) {
                Margin = new Padding(0, 0, 0, 20),
            };

            // El color solo tiene sentido con borde
            borderColorButton.Enabled = borderWidthPicker.Value > 0;
            borderWidthPicker.ValueChanged += (_, _) => borderColorButton.Enabled = borderWidthPicker.Value > 0;

            var save = new DarkButton("Guardar", primary: true) { DialogResult = DialogResult.None };
            var cancel = new DarkButton("Cancelar", primary: false) { DialogResult = DialogResult.Cancel };
            save.Click += (_, _) => Save();

            var buttons = new FlowLayoutPanel {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            cancel.Margin = Padding.Empty;
            save.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);

            // Dos columnas para que la ventana quepa en pantallas con escalado: a la izquierda lo general
            // (atajo, inicio y actualizaciones) y a la derecha lo de la captura (aspecto y dibujo)
            TableLayoutPanel Column(params Control[] controls) {
                var column = new TableLayoutPanel {
                    AutoSize = true,
                    ColumnCount = 1,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                };
                column.Controls.AddRange(controls);
                return column;
            }
            var divider = new Panel {
                BackColor = Edge,
                Width = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(LogicalToDeviceUnits(24), 0, LogicalToDeviceUnits(24), LogicalToDeviceUnits(20)),
            };
            var columns = new TableLayoutPanel {
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1 + LogicalToDeviceUnits(48)));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            columns.Controls.Add(Column(title, hotkeyBox, hint, printScreenNote, settingsLink, startupCheck,
                                        updatesCheck), 0, 0);
            columns.Controls.Add(divider, 1, 0);
            columns.Controls.Add(Column(outputTitle, outputOptions, outputHint, drawingTitle, guidesCheck, guidesHint), 2, 0);

            layout.Controls.Add(top);
            layout.Controls.Add(columns);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private Font NewFont(string family, float size) {
            var font = new Font(family, size);
            fonts.Add(font);
            return font;
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            // Barra de título oscura y del mismo color que el fondo (Windows 11; en Windows 10 solo oscura)
            int on = 1;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            int caption = ColorTranslator.ToWin32(Back);
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            // Sin icono ni título en la barra: ya se sabe que es Trazo (siguen en la barra de tareas)
            NativeMethods.HideCaption(Handle);
            int border = ColorTranslator.ToWin32(Edge);
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            // Sin el foco en el cuadro del atajo: así el atajo global sigue funcionando hasta que se haga clic en él
            ActiveControl = null;
        }

        /// <summary>Opciones de radio de las esquinas y de grosor del borde, en píxeles de la captura (0 = sin ellos).</summary>
        public static readonly int[] CornerRadii = { 0, 6, 12, 24 };
        public static readonly int[] BorderWidths = { 0, 1, 3, 6 };

        /// <summary>Esquina superior izquierda de una tarjeta: en ángulo recto (sin redondear) o cada vez más curva.</summary>
        private static void DrawCornerIcon(Graphics g, int index, Color ink) {
            float r = new[] { 0f, 3f, 7f, 13f }[index];
            using var pen = new Pen(ink, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Miter };
            using var path = new GraphicsPath();
            path.AddLine(5, 21, 5, 5 + r);
            if (r > 0) path.AddArc(5, 5, r * 2, r * 2, 180, 90);
            path.AddLine(5 + r, 5, 21, 5);
            g.DrawPath(pen, path);
        }

        /// <summary>Marco cuadrado con el grosor de la opción; sin borde, un marco tenue tachado.</summary>
        private static void DrawBorderIcon(Graphics g, int index, Color ink) {
            if (index == 0) {
                using var faint = new Pen(Color.FromArgb(130, 130, 136), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(faint, 5.5f, 5.5f, 13, 13);
                using var slash = new Pen(Color.FromArgb(235, 70, 70), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(slash, 4, 20, 20, 4);
                return;
            }
            float w = new[] { 0f, 1f, 2.5f, 4.5f }[index];
            using var pen = new Pen(ink, w);
            g.DrawRectangle(pen, 5 + w / 2, 5 + w / 2, 14 - w, 14 - w);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                colorTip.Dispose();
                foreach (Font f in fonts) f.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Save() {
            Keys hotkey = hotkeyBox.Hotkey;
            if (!HotkeyBox.IsValid(hotkey)) {
                MessageBox.Show(this, "Elige un atajo que incluya Ctrl, Alt o Mayús.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!tryApply(hotkey)) {
                MessageBox.Show(this, $"El atajo {HotkeyText.Format(hotkey)} lo está usando otra aplicación.\nElige otro.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        // ───────────────────────────── Controles con el estilo de Trazo ─────────────────────────────

        private static float Radius(Control c, float logical) => logical * c.DeviceDpi / 96f;

        /// <summary>Fondo del contenedor, para que las esquinas redondeadas no dejen restos.</summary>
        private static void PaintParentBack(Control c, Graphics g) => g.Clear(c.Parent?.BackColor ?? Back);

        private static RectangleF Inner(Control c, float inset = 0.5f) =>
            new(inset, inset, c.Width - 1 - inset * 2 + 0.5f, c.Height - 1 - inset * 2 + 0.5f);

        /// <summary>Botón redondeado: gris claro el principal y gris oscuro el resto.</summary>
        internal sealed class DarkButton : Button {

            private readonly bool primary;
            private readonly IconKind? iconKind;
            private Bitmap? icon;
            private bool hover, pressed;

            /// <param name="icon">Icono claro a la izquierda del texto (opcional).</param>
            public DarkButton(string text, bool primary, IconKind? icon = null) {
                this.primary = primary;
                iconKind = icon;
                Text = text;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                ForeColor = primary ? Color.White : TextColor;
            }

            protected override void OnCreateControl() {
                base.OnCreateControl();
                Size textSize = TextRenderer.MeasureText(Text, Font);
                if (iconKind is IconKind kind) icon = Icons.Create(kind, IconSize, ForeColor);
                int content = textSize.Width + (icon != null ? IconSize + IconGap : 0);
                Size = new Size(Math.Max(LogicalToDeviceUnits(92), content + LogicalToDeviceUnits(28)), LogicalToDeviceUnits(32));
            }

            private int IconSize => LogicalToDeviceUnits(16);
            private int IconGap => LogicalToDeviceUnits(8);

            protected override void Dispose(bool disposing) {
                if (disposing) icon?.Dispose();
                base.Dispose(disposing);
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e) {
                Graphics g = e.Graphics;
                PaintParentBack(this, g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = primary
                    ? pressed ? AccentPressed : hover ? AccentHover : Accent
                    : pressed ? SurfacePressed : hover ? SurfaceHover : Surface;
                using (GraphicsPath path = CaptureOverlay.RoundedRect(Inner(this), Radius(this, 6))) {
                    using var brush = new SolidBrush(Enabled ? fill : Surface);
                    g.FillPath(brush, path);
                    if (!primary || Focused) {
                        using var pen = new Pen(Focused && ShowFocusCues ? AccentLine : Edge, 1f);
                        if (!primary || ShowFocusCues) g.DrawPath(pen, path);
                    }
                }
                Color ink = Enabled ? ForeColor : SubText;
                if (icon == null) {
                    TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }
                // Icono y texto centrados juntos
                Size textSize = TextRenderer.MeasureText(Text, Font);
                int x = (Width - (IconSize + IconGap + textSize.Width)) / 2;
                using (var attrs = new System.Drawing.Imaging.ImageAttributes()) {
                    attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = Enabled ? 1f : 0.45f });
                    g.DrawImage(icon, new Rectangle(x, (Height - IconSize) / 2, IconSize, IconSize), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attrs);
                }
                var textRect = new Rectangle(x + IconSize + IconGap, 0, textSize.Width + 2, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>Casilla redondeada: azul con una marca blanca cuando está activada.</summary>
        private sealed class DarkCheckBox : CheckBox {

            private bool hover;

            public DarkCheckBox(string text, bool isChecked) {
                Text = text;
                Checked = isChecked;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                AutoSize = false;
                Cursor = Cursors.Hand;
            }

            private int BoxSize => LogicalToDeviceUnits(18);
            private int Gap => LogicalToDeviceUnits(10);

            protected override void OnCreateControl() {
                base.OnCreateControl();
                Size text = TextRenderer.MeasureText(Text, Font);
                Size = new Size(BoxSize + Gap + text.Width + 2, Math.Max(BoxSize, text.Height) + 2);
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e) {
                Graphics g = e.Graphics;
                PaintParentBack(this, g);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int size = BoxSize;
                var box = new RectangleF(0.5f, (Height - size) / 2f + 0.5f, size - 1, size - 1);
                using (GraphicsPath path = CaptureOverlay.RoundedRect(box, Radius(this, 4))) {
                    using var brush = new SolidBrush(Checked ? (hover ? AccentHover : Accent) : hover ? SurfaceHover : Surface);
                    g.FillPath(brush, path);
                    if (!Checked || (Focused && ShowFocusCues)) {
                        using var pen = new Pen(Focused && ShowFocusCues ? Color.White : hover ? SubText : Edge, 1f);
                        g.DrawPath(pen, path);
                    }
                }
                if (Checked) {
                    float s = size / 18f;
                    using var check = new Pen(Color.White, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                    g.DrawLines(check, new[] {
                        new PointF(box.X + 4.5f * s, box.Y + 9 * s),
                        new PointF(box.X + 7.5f * s, box.Y + 12 * s),
                        new PointF(box.X + 13 * s, box.Y + 5.5f * s),
                    });
                }
                var textRect = new Rectangle(size + Gap, 0, Width - size - Gap, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        /// <summary>Muestra de color redondeada que abre el selector de color.</summary>
        private sealed class SwatchButton : Button {

            private Color color;
            private bool hover;

            public Color Color {
                get => color;
                set { color = value; Invalidate(); }
            }

            public SwatchButton(Color color) {
                this.color = color;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

            protected override void OnPaint(PaintEventArgs e) {
                Graphics g = e.Graphics;
                PaintParentBack(this, g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath path = CaptureOverlay.RoundedRect(Inner(this, 1), Radius(this, 5));
                // Desactivado (sin borde): el color se apaga
                using (var brush = new SolidBrush(Enabled ? color : Color.FromArgb(70, color))) g.FillPath(brush, path);
                using var pen = new Pen(Focused && ShowFocusCues ? AccentLine : hover && Enabled ? Color.White : Color.FromArgb(200, 255, 255, 255), 1.5f);
                g.DrawPath(pen, path);
            }
        }

        /// <summary>Botones con icono de los que solo uno queda marcado, con el resaltado de la barra de herramientas.</summary>
        private sealed class OptionPicker : FlowLayoutPanel {

            private readonly int[] values;
            private readonly OptionButton[] buttons;
            private readonly ToolTip tips = new();

            public int Value => values[Array.FindIndex(buttons, b => b.Checked) is var i and >= 0 ? i : 0];

            public event EventHandler? ValueChanged;

            public OptionPicker(int[] values, int current, string[] names, Action<Graphics, int, Color> drawIcon, int size) {
                this.values = values;
                AutoSize = true;
                WrapContents = false;
                Padding = Padding.Empty;

                // La opción guardada o, si no coincide con ninguna, la más cercana
                int selected = Enumerable.Range(0, values.Length).OrderBy(i => Math.Abs(values[i] - current)).First();
                buttons = new OptionButton[values.Length];
                for (int i = 0; i < values.Length; i++) {
                    var b = new OptionButton {
                        Size = new Size(size, size),
                        Margin = new Padding(0, 0, 4, 0),
                        Image = CreateIcon(drawIcon, i, size),
                        Checked = i == selected,
                    };
                    tips.SetToolTip(b, values[i] > 0 ? $"{names[i]} ({values[i]} px)" : names[i]);
                    b.CheckedChanged += (_, _) => { if (b.Checked) ValueChanged?.Invoke(this, EventArgs.Empty); };
                    buttons[i] = b;
                    Controls.Add(b);
                }
            }

            private static Bitmap CreateIcon(Action<Graphics, int, Color> draw, int index, int buttonSize) {
                int size = buttonSize * 2 / 3;
                var bmp = new Bitmap(size, size);
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size / 24f, size / 24f);
                draw(g, index, TextColor);
                return bmp;
            }

            protected override void Dispose(bool disposing) {
                if (disposing) {
                    tips.Dispose();
                    foreach (OptionButton b in buttons) b.Image?.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class OptionButton : RadioButton {

            private bool hover;

            public OptionButton() {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Appearance = Appearance.Button;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e) {
                Graphics g = e.Graphics;
                PaintParentBack(this, g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = CaptureOverlay.RoundedRect(Inner(this), Radius(this, 6))) {
                    // Como los botones de la barra: blanco translúcido al pasar por encima y más marcado si está elegido
                    int alpha = Checked ? 55 : hover ? 30 : 0;
                    using (var brush = new SolidBrush(alpha > 0 ? Color.FromArgb(alpha, 255, 255, 255) : Surface)) g.FillPath(brush, path);
                    using var pen = new Pen(Checked ? AccentLine : Edge, Checked ? 1.5f : 1f);
                    g.DrawPath(pen, path);
                    if (Focused && ShowFocusCues && !Checked) {
                        using var focus = new Pen(AccentLine, 1f) { DashStyle = DashStyle.Dot };
                        g.DrawPath(focus, path);
                    }
                }
                if (Image != null) g.DrawImage(Image, (Width - Image.Width) / 2, (Height - Image.Height) / 2, Image.Width, Image.Height);
            }
        }

        /// <summary>
        /// Cuadro que muestra la combinación de teclas pulsada en lugar de escribir texto.
        /// </summary>
        private sealed class HotkeyBox : Control {

            private string display = "";

            public Keys Hotkey { get; private set; }

            public HotkeyBox(Keys initial, Font font) {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                Font = font;
                SetHotkey(initial);
            }

            public static bool IsValid(Keys hotkey) {
                Keys key = hotkey & Keys.KeyCode;
                if (key == Keys.None) return false;
                if ((hotkey & Keys.Modifiers) != 0) return true;
                return key is Keys.PrintScreen or Keys.Pause or Keys.Scroll || key is >= Keys.F1 and <= Keys.F24;
            }

            private void SetHotkey(Keys hotkey) {
                Hotkey = hotkey;
                SetDisplay(HotkeyText.Format(hotkey));
            }

            private void SetDisplay(string text) {
                display = text;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e) {
                Focus();
                base.OnMouseDown(e);
            }

            protected override void OnGotFocus(EventArgs e) {
                base.OnGotFocus(e);
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e) {
                base.OnLostFocus(e);
                SetDisplay(HotkeyText.Format(Hotkey));
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
                Keys key = keyData & Keys.KeyCode;

                // Esc, Tab e Intro sin modificadores siguen funcionando con normalidad en el diálogo
                if ((keyData & Keys.Modifiers) == 0 && key is Keys.Escape or Keys.Tab or Keys.Enter) {
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) {
                    SetDisplay(HotkeyText.Format(keyData & Keys.Modifiers) + " + …");
                    return true;
                }
                SetHotkey(keyData);
                return true;
            }

            protected override void OnKeyUp(KeyEventArgs e) {
                // Windows no envía KeyDown para Impr Pant, solo KeyUp
                if (e.KeyCode == Keys.PrintScreen) {
                    SetHotkey(Keys.PrintScreen | e.Modifiers);
                } else if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu && Focused) {
                    SetDisplay(HotkeyText.Format(Hotkey));
                }
                base.OnKeyUp(e);
            }

            protected override void OnPaint(PaintEventArgs e) {
                Graphics g = e.Graphics;
                PaintParentBack(this, g);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = CaptureOverlay.RoundedRect(Inner(this, Focused ? 1 : 0.5f), Radius(this, 7))) {
                    using (var brush = new SolidBrush(Focused ? SurfaceHover : Surface)) g.FillPath(brush, path);
                    using var pen = new Pen(Focused ? AccentLine : Edge, Focused ? 2f : 1f);
                    g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, display, Font, ClientRectangle, TextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }
}
