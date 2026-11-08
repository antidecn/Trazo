using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Trazo {

    /// <summary>
    /// Abre el instalador de una actualización sin la ventanita "Windows Installer — Preparando la instalación…"
    /// que msiexec enseña un momento antes del asistente: así solo se ve la pantalla de actualización.
    ///
    /// Esa ventanita no forma parte del instalador (sale antes de que se ejecute nada de él), así que se evita
    /// desde aquí: msiexec arranca "oculto" y, además, se oculta en cuanto la crea. Se reconoce por su barra de
    /// progreso, que no tienen los mensajes de Windows Installer (por ejemplo "Hay otra instalación en curso").
    /// Al abrir el .msi a mano (doble clic) la sigue mostrando Windows un instante: eso no se puede evitar.
    /// </summary>
    internal static class InstallerLauncher {

        /// <summary>
        /// Lanza msiexec y espera a que aparezca el asistente (o a que msiexec termine), como mucho 20 s.
        /// Mientras espera, Trazo sigue atendiendo mensajes, que es por donde llegan los avisos de ventanas nuevas.
        /// </summary>
        public static async Task StartAsync(string msiPath) {
            int installer = 0;
            var hidden = new List<IntPtr>();

            void OnWinEvent(IntPtr hook, uint e, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) {
                if (idObject != OBJID_WINDOW || idChild != 0 || installer == 0) return;
                if (!IsPreparingWindow(hwnd, installer)) return;
                ShowWindow(hwnd, SW_HIDE);
                if (!hidden.Contains(hwnd)) hidden.Add(hwnd);
            }

            WinEventProc callback = OnWinEvent;
            IntPtr hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, callback, 0, 0,
                                          WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            bool wizard = false;
            try {
                // "Oculto" se aplica a la primera ventana que muestra msiexec, que siempre es la ventanita de preparación
                // (sale antes que cualquier otra); el asistente y los mensajes se ven con normalidad. El gancho de arriba
                // la oculta igualmente por si se mostrara.
                using Process process = Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"") {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                installer = process.Id;
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(20) && !process.HasExited && !(wizard = HasWizardWindow(installer))) {
                    await Task.Delay(50);
                }
            } finally {
                if (hook != IntPtr.Zero) UnhookWinEvent(hook);
                GC.KeepAlive(callback);
                // Si el asistente no ha llegado a salir, que al menos se vea que Windows Installer sigue trabajando.
                // Si ha salido no: msiexec deja la ventanita oculta (no la cierra) mientras dura el asistente.
                if (!wizard) {
                    foreach (IntPtr hwnd in hidden) {
                        if (IsWindow(hwnd)) ShowWindow(hwnd, SW_SHOWNA);
                    }
                }
            }
        }

        /// <summary>Ventana de preparación de msiexec: cuadro de diálogo estándar con barra de progreso.</summary>
        private static bool IsPreparingWindow(IntPtr hwnd, int installer) {
            GetWindowThreadProcessId(hwnd, out int pid);
            if (pid != installer || GetAncestor(hwnd, GA_ROOT) != hwnd) return false;
            var name = new StringBuilder(32);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString() == "#32770" && FindWindowEx(hwnd, IntPtr.Zero, "msctls_progress32", null) != IntPtr.Zero;
        }

        /// <summary>Alguna ventana visible del asistente de Trazo (los diálogos de Windows Installer).</summary>
        private static bool HasWizardWindow(int installer) {
            bool found = false;
            EnumWindows((hwnd, _) => {
                GetWindowThreadProcessId(hwnd, out int pid);
                if (pid != installer || !IsWindowVisible(hwnd)) return true;
                var name = new StringBuilder(32);
                GetClassName(hwnd, name, name.Capacity);
                found = name.ToString().StartsWith("MsiDialog", StringComparison.Ordinal);
                return !found;
            }, IntPtr.Zero);
            return found;
        }

        private const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        private const int OBJID_WINDOW = 0;
        private const uint GA_ROOT = 2;
        private const int SW_HIDE = 0, SW_SHOWNA = 8;

        private delegate void WinEventProc(IntPtr hook, uint e, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    }
}
