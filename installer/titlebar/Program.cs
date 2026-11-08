using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TrazoTitleBar {

    /// <summary>
    /// Pone la barra de título del asistente del instalador con los colores de Trazo (fondo oscuro, texto claro)
    /// y el icono de Trazo en la barra y en la barra de tareas.
    ///
    /// Windows Installer no deja cambiar la barra de título de sus diálogos, pero Windows sí permite cambiar sus
    /// colores desde otro proceso (DwmSetWindowAttribute).
    ///
    /// El instalador lo lanza al empezar y espera a que termine (Windows Installer no deja lanzar sin esperar un
    /// programa guardado en el .msi: borra el archivo temporal al acabar). Por eso funciona en dos pasos:
    ///   (sin argumentos)       se copia a %TEMP%\TrazoTitleBar, arranca la copia como vigilante, espera a que
    ///                          esté lista (como mucho 3 s, así la primera pantalla ya sale oscura) y termina.
    ///   --vigilar &lt;pid&gt;  cada ventana del asistente que se crea recibe los colores antes de mostrarse.
    ///                          Termina cuando termina ese proceso de Windows Installer.
    /// Las copias de otras veces se borran en el siguiente arranque.
    /// Si algo falla, no pasa nada: el asistente sigue funcionando con la barra de título normal.
    /// </summary>
    internal static class Program {

        // Colores de Trazo (COLORREF: 0x00BBGGRR)
        private const int CaptionColor = 0x001E1C1C;   // 28, 28, 30: el fondo del asistente
        private const int CaptionText = 0x00E6E6E6;    // 230: el texto del asistente
        private const int BorderColor = 0x003F3A3A;    // 58, 58, 63: borde discreto

        private static readonly HashSet<IntPtr> done = new();
        private static WinEventProc? callback;
        private static IntPtr smallIcon, bigIcon;

        [STAThread]
        private static int Main(string[] args) {
            try {
                if (args.Length >= 2 && args[0] == "--vigilar" && int.TryParse(args[1], out int installer)) return Watch(installer);
                return Launch();
            } catch {
                return 0;
            }
        }

        private static string ReadyEvent(int installer) => $"Local\\TrazoTitleBar-{installer}";

        /// <summary>Copia el programa fuera del archivo temporal de Windows Installer y arranca el vigilante.</summary>
        private static int Launch() {
            int installer = ParentProcessId();
            if (installer <= 0) return 0;
            string folder = Path.Combine(Path.GetTempPath(), "TrazoTitleBar");
            Directory.CreateDirectory(folder);
            // Copias de otras veces (las que siguen en uso no se pueden borrar y se dejan)
            foreach (string old in Directory.GetFiles(folder, "*.exe")) {
                try { File.Delete(old); } catch { }
            }
            string copy = Path.Combine(folder, $"TrazoTitleBar-{installer}.exe");
            File.Copy(Process.GetCurrentProcess().MainModule!.FileName, copy, true);

            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEvent(installer));
            using (Process.Start(new ProcessStartInfo(copy, $"--vigilar {installer}") { UseShellExecute = false })) { }
            ready.WaitOne(3000);
            return 0;
        }

        private static int Watch(int installer) {
            using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEvent(installer));
            IntPtr process = OpenProcess(SYNCHRONIZE, false, installer);
            if (process == IntPtr.Zero) return 0;

            callback = OnWinEvent;
            IntPtr hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, callback, 0, 0,
                                          WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            // Las ventanas que ya estuvieran abiertas
            EnumWindows((hwnd, _) => { Apply(hwnd); return true; }, IntPtr.Zero);
            signal.Set();

            // Bucle de mensajes (los avisos de ventanas nuevas llegan por aquí) hasta que termine Windows Installer;
            // como mucho dos horas, por si acaso
            var handles = new[] { process };
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromHours(2)) {
                uint r = MsgWaitForMultipleObjects(1, handles, false, 60000, QS_ALLINPUT);
                if (r == WAIT_OBJECT_0) break;
                while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE)) {
                    if (msg.message == WM_QUIT) return 0;
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            CloseHandle(process);
            return 0;
        }

        private static void OnWinEvent(IntPtr hook, uint e, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) {
            if (idObject == OBJID_WINDOW && idChild == 0 && hwnd != IntPtr.Zero) Apply(hwnd);
        }

        /// <summary>Colores e icono para las ventanas de diálogo de Windows Installer (msiexec).</summary>
        private static void Apply(IntPtr hwnd) {
            if (done.Contains(hwnd) || GetAncestor(hwnd, GA_ROOT) != hwnd) return;
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (!name.ToString().StartsWith("MsiDialog", StringComparison.Ordinal)) return;
            GetWindowThreadProcessId(hwnd, out int pid);
            try {
                using var owner = Process.GetProcessById(pid);
                if (!owner.ProcessName.Equals("msiexec", StringComparison.OrdinalIgnoreCase)) return;
            } catch {
                return;
            }
            done.Add(hwnd);
            if (done.Count > 500) done.Clear();

            // Modo oscuro de la barra (Windows 10 2004+ usa el atributo 20; versiones anteriores, el 19)
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0) {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, 4);
            }
            // Colores exactos (Windows 11)
            int caption = CaptionColor, text = CaptionText, border = BorderColor;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);

            // Icono de Trazo en lugar del de Windows Installer
            if (smallIcon == IntPtr.Zero) {
                uint dpi = 96;
                try { dpi = GetDpiForWindow(hwnd); } catch { }
                if (dpi == 0) dpi = 96;
                IntPtr module = GetModuleHandle(null);
                smallIcon = LoadImage(module, (IntPtr)AppIconId, IMAGE_ICON, (int)(16 * dpi / 96), (int)(16 * dpi / 96), 0);
                bigIcon = LoadImage(module, (IntPtr)AppIconId, IMAGE_ICON, (int)(32 * dpi / 96), (int)(32 * dpi / 96), 0);
            }
            if (smallIcon != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon);
            if (bigIcon != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, bigIcon);
        }

        /// <summary>Proceso que lanzó este programa (Windows Installer).</summary>
        private static int ParentProcessId() {
            int self = Process.GetCurrentProcess().Id;
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == (IntPtr)(-1)) return 0;
            try {
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                for (bool ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry)) {
                    if (entry.th32ProcessID == self) return (int)entry.th32ParentProcessID;
                }
                return 0;
            } finally {
                CloseHandle(snapshot);
            }
        }

        // ── Windows ──

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19, DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
        private const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        private const int OBJID_WINDOW = 0;
        private const uint GA_ROOT = 2;
        private const uint WM_SETICON = 0x0080, WM_QUIT = 0x0012;
        private const int ICON_SMALL = 0, ICON_BIG = 1;
        private const int AppIconId = 32512;   // icono de la aplicación que pone el SDK de .NET (ApplicationIcon)
        private const uint IMAGE_ICON = 1;
        private const uint SYNCHRONIZE = 0x00100000;
        private const uint QS_ALLINPUT = 0x04FF, PM_REMOVE = 1, WAIT_OBJECT_0 = 0;
        private const uint TH32CS_SNAPPROCESS = 2;

        private delegate void WinEventProc(IntPtr hook, uint e, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32 {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    }
}
