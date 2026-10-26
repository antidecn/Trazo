using System.Runtime.InteropServices;

namespace Trazo {

    internal static class NativeMethods {

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr minimumSize, IntPtr maximumSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct IconInfo {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateIconIndirect(ref IconInfo info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);
    }

    /// <summary>
    /// Cruz de selección: el trazo invierte lo que tiene debajo (como la de Windows) y lleva un
    /// contorno negro para que se vea también sobre grises, donde el invertido apenas cambia nada.
    /// </summary>
    internal sealed class CrossCursor : IDisposable {

        private readonly IntPtr handle;
        public Cursor Cursor { get; }

        public CrossCursor(float scale) {
            int arm = Math.Max(8, (int)Math.Round(10 * scale));   // largo de cada brazo desde el centro
            int center = arm + 1;                                  // deja 1 px para el contorno
            // Cursor monocromo: ancho múltiplo de 16 (filas alineadas a 2 bytes)
            int size = (center * 2 + 1 + 15) / 16 * 16;
            int stride = size / 8;

            // Máscara AND arriba y XOR abajo. AND=1/XOR=0: transparente · AND=0/XOR=0: negro · AND=1/XOR=1: invertido
            var bits = new byte[stride * size * 2];
            for (int i = 0; i < stride * size; i++) bits[i] = 0xFF;

            void Set(int x, int y, bool and, bool xor) {
                int index = y * stride + x / 8;
                byte bit = (byte)(0x80 >> (x % 8));
                if (and) bits[index] |= bit; else bits[index] &= (byte)~bit;
                int xorIndex = index + stride * size;
                if (xor) bits[xorIndex] |= bit; else bits[xorIndex] &= (byte)~bit;
            }

            for (int t = -arm - 1; t <= arm + 1; t++) {
                for (int o = -1; o <= 1; o++) {
                    Set(center + t, center + o, false, false);
                    Set(center + o, center + t, false, false);
                }
            }
            for (int t = -arm; t <= arm; t++) {
                Set(center + t, center, true, true);
                Set(center, center + t, true, true);
            }

            IntPtr mask = NativeMethods.CreateBitmap(size, size * 2, 1, 1, bits);
            var info = new NativeMethods.IconInfo { fIcon = false, xHotspot = center, yHotspot = center, hbmMask = mask };
            handle = NativeMethods.CreateIconIndirect(ref info);
            NativeMethods.DeleteObject(mask);
            Cursor = handle != IntPtr.Zero ? new Cursor(handle) : Cursors.Cross;
        }

        public void Dispose() {
            if (handle != IntPtr.Zero) NativeMethods.DestroyCursor(handle);
        }
    }

    /// <summary>Funciones que .NET Framework 4.8 no trae (Math.Clamp llegó en .NET Core).</summary>
    internal static class MathUtil {

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
