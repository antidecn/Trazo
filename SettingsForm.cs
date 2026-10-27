using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Trazo {

    internal sealed class SettingsForm : Form {

        private readonly Func<Keys, bool> tryApply;
        private readonly HotkeyBox hotkeyBox;
        private readonly CheckBox startupCheck;
        private readonly OptionPicker radiusPicker;
        private readonly ToolTip colorTip = new();
        private readonly Button borderColorButton;
        private readonly OptionPicker borderWidthPicker;

        public Keys Hotkey => hotkeyBox.Hotkey;
        public bool StartWithWindows => startupCheck.Checked;
        public bool RoundCorners => radiusPicker.Value > 0;
        public int CornerRadius => radiusPicker.Value;
        public bool AddBorder => borderWidthPicker.Value > 0;
        public Color BorderColor => borderColorButton.BackColor;
        public int BorderWidth => borderWidthPicker.Value;

        /// <param name="tryApply">Intenta registrar el atajo; devuelve false si está ocupado.</param>
        /// <param name="output">Opciones actuales del aspecto de la captura (solo se leen).</param>
        public SettingsForm(Keys current, bool startWithWindows, AppSettings output, Icon icon, Func<Keys, bool> tryApply) {
            this.tryApply = tryApply;

            Text = "Configuración de Trazo";
            Icon = icon;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

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
                Font = new Font("Segoe UI Semibold", 16f),
                AutoSize = true,
                Margin = Padding.Empty,
            };
            var version = new Label {
                Text = $"Versión {Application.ProductVersion.Split('+')[0]}",
                ForeColor = SystemColors.GrayText,
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
                Margin = new Padding(0, 0, 0, 18),
            };
            header.Controls.Add(logo);
            header.Controls.Add(nameStack);

            var title = new Label {
                Text = "Atajo para hacer una captura",
                Font = new Font("Segoe UI Semibold", 10f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };

            hotkeyBox = new HotkeyBox(current) {
                Width = 320,
                // Ocupa todo el ancho de la ventana
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 0, 4),
            };

            var hint = new Label {
                Text = "Haz clic en el cuadro y pulsa la combinación que quieras.\n" +
                       "Debe incluir Ctrl, Alt o Mayús (salvo Impr Pant, Pausa o F1–F24).",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 0, 12),
            };

            var printScreenNote = new Label {
                Text = "Para usar Impr Pant sola, desactiva antes en Windows la opción\n" +
                       "\"Usar la tecla Impr Pant para abrir la captura de pantalla\".",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2),
            };
            var settingsLink = new LinkLabel {
                Text = "Abrir la configuración de teclado de Windows",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16),
            };
            settingsLink.LinkClicked += (_, _) => {
                Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-keyboard") { UseShellExecute = true });
            };

            startupCheck = new CheckBox {
                Text = "Iniciar Trazo al encender el ordenador",
                Checked = startWithWindows,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16),
            };

            // Aspecto de la imagen copiada o guardada
            var outputTitle = new Label {
                Text = "Aspecto de la captura",
                Font = new Font("Segoe UI Semibold", 10f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };

            // Cuatro opciones con icono en cada grupo; la primera lo desactiva
            int optionSize = LogicalToDeviceUnits(32);
            var cornersCaption = new Label { Text = "Esquinas", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            radiusPicker = new OptionPicker(CornerRadii, output.RoundCorners ? output.CornerRadius : 0,
                new[] { "Sin redondear", "Suaves", "Medias", "Muy redondeadas" }, DrawCornerIcon, optionSize) {
                Margin = Padding.Empty,
            };

            var borderCaption = new Label { Text = "Borde", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            borderWidthPicker = new OptionPicker(BorderWidths, output.AddBorder ? output.BorderWidth : 0,
                new[] { "Sin borde", "Fino", "Medio", "Grueso" }, DrawBorderIcon, optionSize) {
                Margin = new Padding(0, 0, 6, 0),
            };
            int swatch = LogicalToDeviceUnits(22);
            borderColorButton = new Button {
                BackColor = output.BorderColorValue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(swatch, swatch),
                Margin = new Padding(0, (optionSize - swatch) / 2, 0, 0),
                Cursor = Cursors.Hand,
            };
            borderColorButton.FlatAppearance.BorderColor = SystemColors.ControlDark;
            colorTip.SetToolTip(borderColorButton, "Color del borde");
            borderColorButton.Click += (_, _) => {
                using var dialog = new ColorDialog { Color = borderColorButton.BackColor, FullOpen = true, AnyColor = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) borderColorButton.BackColor = dialog.Color;
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

            var outputHint = new Label {
                Text = "Se aplica al copiar y al guardar. Las esquinas redondeadas\n" +
                       "son transparentes en PNG y blancas en JPEG y BMP.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 16),
            };

            // El color solo tiene sentido con borde
            borderColorButton.Enabled = borderWidthPicker.Value > 0;
            borderWidthPicker.ValueChanged += (_, _) => borderColorButton.Enabled = borderWidthPicker.Value > 0;

            var save = new Button { Text = "Guardar", AutoSize = true, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += (_, _) => Save();

            var buttons = new FlowLayoutPanel {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);

            layout.Controls.Add(header);
            layout.Controls.Add(title);
            layout.Controls.Add(hotkeyBox);
            layout.Controls.Add(hint);
            layout.Controls.Add(printScreenNote);
            layout.Controls.Add(settingsLink);
            layout.Controls.Add(startupCheck);
            layout.Controls.Add(outputTitle);
            layout.Controls.Add(outputOptions);
            layout.Controls.Add(outputHint);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = save;
            CancelButton = cancel;
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
                using var faint = new Pen(Color.FromArgb(150, 150, 150), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(faint, 5.5f, 5.5f, 13, 13);
                using var slash = new Pen(Color.FromArgb(220, 40, 40), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(slash, 4, 20, 20, 4);
                return;
            }
            float w = new[] { 0f, 1f, 2.5f, 4.5f }[index];
            using var pen = new Pen(ink, w);
            g.DrawRectangle(pen, 5 + w / 2, 5 + w / 2, 14 - w, 14 - w);
        }

        /// <summary>Botones con icono de los que solo uno queda marcado.</summary>
        private sealed class OptionPicker : FlowLayoutPanel {

            private readonly int[] values;
            private readonly RadioButton[] buttons;
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
                buttons = new RadioButton[values.Length];
                for (int i = 0; i < values.Length; i++) {
                    var b = new RadioButton {
                        Appearance = Appearance.Button,
                        FlatStyle = FlatStyle.Flat,
                        Size = new Size(size, size),
                        Margin = new Padding(0, 0, 4, 0),
                        Image = CreateIcon(drawIcon, i, size),
                        Checked = i == selected,
                        Cursor = Cursors.Hand,
                    };
                    b.FlatAppearance.BorderColor = SystemColors.ControlDark;
                    b.FlatAppearance.CheckedBackColor = Color.FromArgb(204, 228, 247);
                    b.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 241, 251);
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
                draw(g, index, Color.FromArgb(45, 45, 45));
                return bmp;
            }

            protected override void Dispose(bool disposing) {
                if (disposing) {
                    tips.Dispose();
                    foreach (RadioButton b in buttons) b.Image?.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) colorTip.Dispose();
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

        /// <summary>
        /// Cuadro que muestra la combinación de teclas pulsada en lugar de escribir texto.
        /// </summary>
        private sealed class HotkeyBox : TextBox {

            public Keys Hotkey { get; private set; }

            public HotkeyBox(Keys initial) {
                ReadOnly = true;
                BackColor = SystemColors.Window;
                Cursor = Cursors.Hand;
                ShortcutsEnabled = false;
                TextAlign = HorizontalAlignment.Center;
                Font = new Font("Segoe UI", 11f);
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
                Text = HotkeyText.Format(hotkey);
            }

            protected override void OnGotFocus(EventArgs e) {
                base.OnGotFocus(e);
                BackColor = Color.FromArgb(225, 240, 255);
            }

            protected override void OnLostFocus(EventArgs e) {
                base.OnLostFocus(e);
                BackColor = SystemColors.Window;
                Text = HotkeyText.Format(Hotkey);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
                Keys key = keyData & Keys.KeyCode;

                // Esc, Tab e Intro sin modificadores siguen funcionando con normalidad en el diálogo
                if ((keyData & Keys.Modifiers) == 0 && key is Keys.Escape or Keys.Tab or Keys.Enter) {
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) {
                    Text = HotkeyText.Format(keyData & Keys.Modifiers) + " + …";
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
                    Text = HotkeyText.Format(Hotkey);
                }
                base.OnKeyUp(e);
            }
        }
    }
}
