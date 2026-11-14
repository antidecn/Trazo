namespace Trazo {

    /// <summary>
    /// Mensaje con el aspecto de Trazo (fondo oscuro, logo, texto claro y botones redondeados) en lugar
    /// del cuadro de mensaje de Windows. No es modal: mientras está abierto se puede seguir haciendo capturas,
    /// y el teclado de la captura no se lo queda el mensaje.
    /// </summary>
    internal sealed class TrazoMessage : DarkDialog {

        private readonly TaskCompletionSource<bool> result = new();
        private readonly Icon windowIcon;
        private readonly Bitmap logoImage;
        private bool accepted;

        /// <summary>No es modal: devuelve una tarea que termina al cerrarse el mensaje.</summary>
        /// <param name="accept">Texto del botón principal.</param>
        /// <param name="cancel">Texto del botón secundario; null para un mensaje que solo se acepta.</param>
        /// <param name="link">Línea con un enlace tras el mensaje (opcional): la frase, la parte de ella que es el enlace y su dirección.</param>
        /// <param name="footer">Texto tras el enlace (opcional), por ejemplo la pregunta a la que responden los botones.</param>
        /// <returns>True si se ha pulsado el botón principal.</returns>
        public static Task<bool> ShowAsync(string title, string message, string accept = "Aceptar", string? cancel = null,
                                           (string Text, string LinkText, string Url)? link = null, string? footer = null) {
            var form = new TrazoMessage(title, message, accept, cancel, link, footer);
            form.Show();
            form.Activate();
            return form.result.Task;
        }

        private TrazoMessage(string title, string message, string acceptText, string? cancelText,
                             (string Text, string LinkText, string Url)? link, string? footer) {
            Text = "Trazo";
            windowIcon = Icons.AppIcon(SystemInformation.SmallIconSize.Width);
            Icon = windowIcon;
            Font = NewFont("Segoe UI", 9.5f);
            ShowInTaskbar = true;
            // Encima del resto para que no quede escondido, pero por debajo de una captura que se abra después
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(LogicalToDeviceUnits(22), LogicalToDeviceUnits(20), LogicalToDeviceUnits(22), LogicalToDeviceUnits(18));

            int logoSize = LogicalToDeviceUnits(40);
            var logo = new PictureBox {
                Size = new Size(logoSize, logoSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 2, LogicalToDeviceUnits(16), 0),
            };
            using (Icon big = Icons.AppIcon(logoSize)) logoImage = big.ToBitmap();
            logo.Image = logoImage;

            int textWidth = LogicalToDeviceUnits(380);
            var titleLabel = new Label {
                Text = title,
                Font = NewFont("Segoe UI Semibold", 12f),
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6)),
                UseMnemonic = false,
            };
            var messageLabel = new Label {
                Text = message,
                ForeColor = SettingsForm.SubText,
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Margin = Padding.Empty,
                UseMnemonic = false,
            };
            var texts = new FlowLayoutPanel {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            texts.Controls.Add(titleLabel);
            texts.Controls.Add(messageLabel);

            // Enlace (por ejemplo, a la página de la versión en GitHub): solo la parte indicada es clicable
            if (link is { } l) {
                int start = Math.Max(0, l.Text.IndexOf(l.LinkText, StringComparison.Ordinal));
                var linkLabel = new LinkLabel {
                    Text = l.Text,
                    LinkArea = new LinkArea(start, l.LinkText.Length),
                    ForeColor = SettingsForm.SubText,
                    LinkColor = SettingsForm.AccentLine,
                    ActiveLinkColor = Color.White,
                    VisitedLinkColor = SettingsForm.AccentLine,
                    LinkBehavior = LinkBehavior.AlwaysUnderline,
                    AutoSize = true,
                    MaximumSize = new Size(textWidth, 0),
                    Margin = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
                    UseMnemonic = false,
                };
                linkLabel.LinkClicked += (_, _) => Ui.OpenUrl(l.Url);
                texts.Controls.Add(linkLabel);
            }
            if (footer != null) {
                texts.Controls.Add(new Label {
                    Text = footer,
                    ForeColor = SettingsForm.SubText,
                    AutoSize = true,
                    MaximumSize = new Size(textWidth, 0),
                    Margin = new Padding(0, LogicalToDeviceUnits(14), 0, 0),
                    UseMnemonic = false,
                });
            }

            var acceptButton = new SettingsForm.DarkButton(acceptText, primary: true) { Margin = Padding.Empty };
            acceptButton.Click += (_, _) => {
                accepted = true;
                Close();
            };
            var buttons = new FlowLayoutPanel {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, LogicalToDeviceUnits(22), 0, 0),
            };
            if (cancelText != null) {
                var cancelButton = new SettingsForm.DarkButton(cancelText, primary: false) { Margin = Padding.Empty };
                cancelButton.Click += (_, _) => Close();
                buttons.Controls.Add(cancelButton);
                acceptButton.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
                CancelButton = cancelButton;
            } else {
                CancelButton = acceptButton;
            }
            buttons.Controls.Add(acceptButton);
            AcceptButton = acceptButton;

            var layout = new TableLayoutPanel {
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Fill,
            };
            layout.Controls.Add(logo, 0, 0);
            layout.Controls.Add(texts, 1, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            // Sin el borde de foco en el botón al abrir
            ActiveControl = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            base.OnFormClosed(e);
            result.TrySetResult(accepted);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                windowIcon.Dispose();
                logoImage.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
