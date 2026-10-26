using System.Diagnostics;

namespace Trazo {

    internal sealed class SettingsForm : Form {

        private readonly Func<Keys, bool> tryApply;
        private readonly HotkeyBox hotkeyBox;
        private readonly CheckBox startupCheck;

        public Keys Hotkey => hotkeyBox.Hotkey;
        public bool StartWithWindows => startupCheck.Checked;

        /// <param name="tryApply">Intenta registrar el atajo; devuelve false si está ocupado.</param>
        public SettingsForm(Keys current, bool startWithWindows, Icon icon, Func<Keys, bool> tryApply) {
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

            hotkeyBox = new HotkeyBox(current) { Width = 320, Margin = new Padding(0, 0, 0, 4) };

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
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = save;
            CancelButton = cancel;
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
