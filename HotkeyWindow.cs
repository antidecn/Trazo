namespace Trazo {

    /// <summary>
    /// Ventana invisible (solo mensajes) que recibe el atajo de teclado global.
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable {

        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 1;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        private static readonly IntPtr HWND_MESSAGE = new(-3);

        private bool registered;

        public event Action? Pressed;

        public HotkeyWindow() {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        /// <summary>
        /// Sustituye el atajo actual. Devuelve false si otra aplicación ya lo está usando.
        /// </summary>
        public bool Register(Keys hotkey) {
            Unregister();

            uint mods = MOD_NOREPEAT;
            if (hotkey.HasFlag(Keys.Control)) mods |= MOD_CONTROL;
            if (hotkey.HasFlag(Keys.Alt)) mods |= MOD_ALT;
            if (hotkey.HasFlag(Keys.Shift)) mods |= MOD_SHIFT;

            registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, mods, (uint)(hotkey & Keys.KeyCode));
            return registered;
        }

        public void Unregister() {
            if (!registered) return;
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            registered = false;
        }

        protected override void WndProc(ref Message m) {
            if (m.Msg == WM_HOTKEY) {
                Pressed?.Invoke();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose() {
            Unregister();
            DestroyHandle();
        }
    }
}
