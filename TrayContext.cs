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
        private ShortcutsForm? shortcutsForm;
        private bool checkingUpdates;
        /// <summary>Trazo se está cerrando (por ejemplo, para instalar una actualización).</summary>
        private bool exiting;

        public TrayContext() {
            // Mismo tamaño para las dos: el de la bandeja y el de la barra de título con el escalado actual
            int iconSize = SystemInformation.SmallIconSize.Width;
            icon = Icons.AppIcon(iconSize);
            lightTaskbar = IsTaskbarLight();
            trayImage = Icons.TrayIcon(iconSize, lightTaskbar);
            settings = AppSettings.Load();

            // Una actualización del instalador vuelve a crear el inicio automático: se respeta lo elegido
            if (!settings.StartWithWindows) Autostart.Set(false);
            ApplyInstallerUpdateChoice();

            // Mismo estilo oscuro que la configuración y la barra de la captura
            ContextMenuStrip menu = DarkMenu.Create();
            using (Icon logo = Icons.AppIcon(DarkMenu.TrailingSize(menu))) {
                // Con el logo de Trazo a la derecha
                DarkMenu.AddItem(menu, "Configuración…", OpenSettings, logo.ToBitmap());
            }
            DarkMenu.AddItem(menu, "Atajos de teclado", ShowShortcuts);
            updateItem = DarkMenu.AddItem(menu, "Buscar actualizaciones…", () => _ = CheckForUpdatesAsync());
            DarkMenu.AddSeparator(menu);
            DarkMenu.AddItem(menu, "Salir", ExitThread);

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

            updateTimer.Tick += OnUpdateTimer;
            updateTimer.Start();
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
            // Con la configuración abierta también se puede capturar (por ejemplo, la propia ventana)
            if (overlay != null) {
                overlay.Activate();
                return;
            }

            // Un aviso de la captura anterior que siga en pantalla saldría en la imagen: se cierra antes
            // y se da tiempo a Windows para redibujar lo que tapaba
            if (Toast.CloseCurrent()) {
                Application.DoEvents();
                Thread.Sleep(60);
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

            // El atajo sigue activo con la ventana abierta; solo se libera mientras se escribe uno nuevo en el cuadro,
            // para poder pulsarlo allí
            void Suspend() => hotkeys.Unregister();
            void Resume() {
                if (hotkeys.Register(settings.Hotkey)) UpdateTrayText();
                else trayIcon.Text = "Trazo (atajo no disponible)";
            }
            using (settingsForm = new SettingsForm(settings.Hotkey, Autostart.IsEnabled, settings, icon, hotkeys.Register, Suspend, Resume,
                                                       CheckForUpdatesAsync)) {
                DialogResult result = settingsForm.ShowDialog();
                // Se ha cerrado para instalar una actualización: ya no queda nada que guardar ni atajo que recuperar
                if (exiting) return;
                if (result == DialogResult.OK) {
                    settings.Hotkey = settingsForm.Hotkey;
                    settings.StartWithWindows = settingsForm.StartWithWindows;
                    settings.RoundCorners = settingsForm.RoundCorners;
                    settings.CornerRadius = settingsForm.CornerRadius;
                    settings.AddBorder = settingsForm.AddBorder;
                    settings.BorderColorValue = settingsForm.BorderColor;
                    settings.BorderWidth = settingsForm.BorderWidth;
                    settings.ShowGuides = settingsForm.ShowGuides;
                    settings.NotifyUpdates = settingsForm.NotifyUpdates;
                    Autostart.Set(settings.StartWithWindows);
                    settings.Save();
                }
                // Al cerrar, el cuadro del atajo pierde el foco y puede haber vuelto a poner el anterior:
                // queda el que corresponde (el nuevo al guardar, el de antes al cancelar)
                Resume();
            }
            settingsForm = null;
            ReleaseMemory();
        }

        /// <summary>Solo a petición del usuario: Trazo nunca busca actualizaciones por su cuenta.</summary>
        /// <returns>Se completa al terminar la búsqueda (y, si la hay, la pregunta de si instalarla).</returns>
        private async Task CheckForUpdatesAsync() {
            if (checkingUpdates) return;
            checkingUpdates = true;
            updateItem.Enabled = false;
            updateItem.Text = "Buscando actualizaciones…";
            bool installing = false;
            try {
                installing = await Updater.CheckAsync();
            } finally {
                checkingUpdates = false;
                updateItem.Text = "Buscar actualizaciones…";
                updateItem.Enabled = true;
            }
            // El instalador sustituye Trazo.exe: hay que cerrarse
            if (installing) ExitThread();
            else ReleaseMemory();
        }

        // ───────────────────────────── Aviso de versión nueva ─────────────────────────────

        /// <summary>Primera comprobación: unos minutos después de arrancar, para no competir con el inicio de Windows.</summary>
        private const int FirstCheckDelayMs = 5 * 60 * 1000;
        /// <summary>Después, cada hora se mira si ya toca (como mucho una comprobación al día).</summary>
        private const int RecheckIntervalMs = 60 * 60 * 1000;
        private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

        private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = FirstCheckDelayMs };
        private UpdateToast? updateToast;

        /// <summary>
        /// El instalador pregunta si avisar de las versiones nuevas y deja la respuesta en el registro.
        /// Se pasa a la configuración y se borra: a partir de ahí manda lo que se elija en Configuración.
        /// </summary>
        private void ApplyInstallerUpdateChoice() {
            try {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Trazo", writable: true);
                if (key?.GetValue("NotifyUpdates") is not string choice) return;
                settings.NotifyUpdates = choice == "1";
                settings.Save();
                key.DeleteValue("NotifyUpdates", throwOnMissingValue: false);
            } catch (Exception) {
                // Sin permisos o configuración ilegible: se queda lo que hubiera
            }
        }

        /// <summary>
        /// Cada hora (la primera vez, a los 5 minutos de arrancar): si están activados los avisos y hace más de un día
        /// de la última comprobación, consulta en segundo plano la última versión y avisa si es nueva.
        /// No descarga ni instala nada.
        /// </summary>
        private async void OnUpdateTimer(object? sender, EventArgs e) {
            updateTimer.Interval = RecheckIntervalMs;
            if (!settings.NotifyUpdates || checkingUpdates || exiting) return;
            // Con una captura abierta se deja para la próxima vez
            if (overlay != null) return;
            if (DateTime.TryParse(settings.LastUpdateCheck, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)
                && DateTime.UtcNow - last < CheckEvery) return;

            checkingUpdates = true;
            Version? latest;
            try {
                latest = await Updater.GetLatestVersionQuietlyAsync();
            } finally {
                checkingUpdates = false;
            }
            ReleaseMemory();
            // Sin conexión: se vuelve a intentar dentro de una hora
            if (latest == null || exiting) return;

            settings.LastUpdateCheck = DateTime.UtcNow.ToString("o");
            bool isNew = latest > Updater.CurrentVersion && settings.NotifiedVersion != latest.ToString();
            if (isNew) settings.NotifiedVersion = latest.ToString();
            try {
                settings.Save();
            } catch (Exception) {
                // Sin poder guardar, como mucho se vuelve a comprobar o avisar en el próximo arranque
            }
            if (isNew) ShowUpdateToast(latest);
        }

        private void ShowUpdateToast(Version latest) {
            updateToast?.Close();
            updateToast = new UpdateToast(latest, Updater.CurrentVersion);
            // "Ver la versión": el mismo camino que "Buscar actualizaciones…", que pregunta antes de descargar nada
            updateToast.ViewRequested += () => _ = CheckForUpdatesAsync();
            updateToast.FormClosed += (_, _) => updateToast = null;
            updateToast.Show();
        }

        /// <summary>Ventana con los atajos de teclado; una sola aunque se pida varias veces.</summary>
        private void ShowShortcuts() {
            if (shortcutsForm != null) {
                shortcutsForm.Activate();
                return;
            }
            shortcutsForm = new ShortcutsForm(settings.Hotkey, icon);
            // Abierta con Show: al cerrarla se libera sola
            shortcutsForm.FormClosed += (_, _) => {
                shortcutsForm = null;
                ReleaseMemory();
            };
            shortcutsForm.Show();
        }

        protected override void ExitThreadCore() {
            exiting = true;
            overlay?.Close();
            shortcutsForm?.Close();
            settingsForm?.Close();
            updateTimer.Dispose();
            updateToast?.Close();
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
