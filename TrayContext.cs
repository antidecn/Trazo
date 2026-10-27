using System.Runtime;
using Microsoft.Win32;

namespace Trazo {

    /// <summary>
    /// Vive en la bandeja del sistema sin ventana visible y abre el editor al pulsar el atajo.
    /// </summary>
    internal sealed class TrayContext : ApplicationContext {

        private readonly NotifyIcon trayIcon;
        private readonly HotkeyWindow hotkeys;
        private readonly Icon icon;
        private Icon trayImage;
        private bool lightTaskbar;
        private readonly AppSettings settings;
        private CaptureOverlay? overlay;
        private SettingsForm? settingsForm;
        private readonly ToolStripMenuItem updateItem;

        public TrayContext() {
            // Mismo tamaño para las dos: el de la bandeja y el de la barra de título con el escalado actual
            int iconSize = SystemInformation.SmallIconSize.Width;
            icon = Icons.AppIcon(iconSize);
            lightTaskbar = IsTaskbarLight();
            trayImage = Icons.TrayIcon(iconSize, lightTaskbar);
            settings = AppSettings.Load();

            // Una actualización del instalador vuelve a crear el inicio automático: se respeta lo elegido
            if (!settings.StartWithWindows) Autostart.Set(false);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Configuración…", null, (_, _) => OpenSettings());
            updateItem = (ToolStripMenuItem)menu.Items.Add("Buscar actualizaciones…", null, (_, _) => CheckForUpdates());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (_, _) => ExitThread());

            trayIcon = new NotifyIcon {
                Icon = trayImage,
                ContextMenuStrip = menu,
                Visible = true,
            };
            trayIcon.MouseDoubleClick += (_, e) => {
                if (e.Button == MouseButtons.Left) OpenSettings();
            };
            trayIcon.BalloonTipClicked += (_, _) => OpenSettings();
            // Llega en este hilo (el del menú ya creado) al cambiar el tema de Windows
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += StartCapture;

            string name = HotkeyText.Format(settings.Hotkey);
            if (hotkeys.Register(settings.Hotkey)) {
                UpdateTrayText();
                // Solo la primera vez: con el inicio automático saldría en cada arranque de Windows
                if (settings.IsFirstRun) {
                    trayIcon.ShowBalloonTip(4000, "Trazo", $"Trazo está en la bandeja. Pulsa {name} para capturar.", ToolTipIcon.None);
                    settings.Save();
                }
            } else {
                trayIcon.Text = "Trazo (atajo no disponible)";
                trayIcon.ShowBalloonTip(5000, "Trazo",
                    $"El atajo {name} lo está usando otra aplicación. Haz clic aquí para elegir otro.", ToolTipIcon.Warning);
            }

            // Lo usado al arrancar (cargar WinForms, crear el icono…) ya no hace falta en reposo
            var startupTrim = new System.Windows.Forms.Timer { Interval = 3000 };
            startupTrim.Tick += (_, _) => {
                startupTrim.Dispose();
                ReleaseMemory();
            };
            startupTrim.Start();
        }

        /// <summary>Modo claro u oscuro de la barra de tareas (independiente del de las aplicaciones).</summary>
        private static bool IsTaskbarLight() {
            try {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                // Sin el valor (Windows antiguos), la barra de tareas es oscura
                return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            } catch {
                return false;
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
            if (e.Category != UserPreferenceCategory.General) return;
            bool light = IsTaskbarLight();
            if (light == lightTaskbar) return;

            lightTaskbar = light;
            Icon old = trayImage;
            trayImage = Icons.TrayIcon(SystemInformation.SmallIconSize.Width, light);
            trayIcon.Icon = trayImage;
            old.Dispose();
        }

        private void UpdateTrayText() {
            string text = $"Trazo ({HotkeyText.Format(settings.Hotkey)})";
            trayIcon.Text = text.Length <= 127 ? text : "Trazo";
        }

        private void StartCapture() {
            if (settingsForm != null) return;
            if (overlay != null) {
                overlay.Activate();
                return;
            }

            Bitmap shot = ScreenCapture.CaptureVirtualScreen(out Rectangle bounds);
            overlay = new CaptureOverlay(shot, bounds, settings);
            overlay.FormClosed += (_, _) => overlay = null;
            // Disposed llega después de liberar las imágenes de la captura (FormClosed llega antes)
            overlay.Disposed += (_, _) => ReleaseMemory();
            overlay.Show();
        }

        /// <summary>
        /// Devuelve al sistema la memoria usada durante la captura para quedar en reposo
        /// con el mínimo posible (la captura y el OCR pueden ocupar decenas de MB).
        /// </summary>
        private static void ReleaseMemory() {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            // Los objetos WinRT del OCR se liberan en sus finalizadores
            GC.WaitForPendingFinalizers();
            GC.Collect();
            NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }

        private void OpenSettings() {
            if (settingsForm != null) {
                settingsForm.Activate();
                return;
            }
            overlay?.Close();

            // Se libera el atajo mientras se elige otro, para poder pulsarlo en el cuadro
            hotkeys.Unregister();
            using (settingsForm = new SettingsForm(settings.Hotkey, Autostart.IsEnabled, settings, icon, hotkeys.Register)) {
                if (settingsForm.ShowDialog() == DialogResult.OK) {
                    settings.Hotkey = settingsForm.Hotkey;
                    settings.StartWithWindows = settingsForm.StartWithWindows;
                    settings.RoundCorners = settingsForm.RoundCorners;
                    settings.CornerRadius = settingsForm.CornerRadius;
                    settings.AddBorder = settingsForm.AddBorder;
                    settings.BorderColorValue = settingsForm.BorderColor;
                    settings.BorderWidth = settingsForm.BorderWidth;
                    Autostart.Set(settings.StartWithWindows);
                    settings.Save();
                    UpdateTrayText();
                } else if (!hotkeys.Register(settings.Hotkey)) {
                    trayIcon.Text = "Trazo (atajo no disponible)";
                }
            }
            settingsForm = null;
            ReleaseMemory();
        }

        /// <summary>Solo a petición del usuario: Trazo nunca busca actualizaciones por su cuenta.</summary>
        private async void CheckForUpdates() {
            if (!updateItem.Enabled) return;
            updateItem.Enabled = false;
            updateItem.Text = "Buscando actualizaciones…";
            bool installing = false;
            try {
                installing = await Updater.CheckAsync();
            } finally {
                updateItem.Text = "Buscar actualizaciones…";
                updateItem.Enabled = true;
            }
            // El instalador sustituye Trazo.exe: hay que cerrarse
            if (installing) ExitThread();
            else ReleaseMemory();
        }

        protected override void ExitThreadCore() {
            overlay?.Close();
            settingsForm?.Close();
            hotkeys.Dispose();
            // Evento estático: sin quitarlo, mantendría vivo este objeto
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            trayIcon.Visible = false;
            trayIcon.Dispose();
            icon.Dispose();
            trayImage.Dispose();
            base.ExitThreadCore();
        }
    }
}
