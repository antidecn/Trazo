using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Trazo {

    internal static class ScreenCapture {

        /// <summary>
        /// Captura todos los monitores a la vez. <paramref name="bounds"/> es el rectángulo
        /// del escritorio virtual (puede tener origen negativo si hay monitores a la izquierda/arriba).
        /// </summary>
        /// <remarks>
        /// No usa Graphics.CopyFromScreen: GDI+ trata el color #0C0B0D como "sin dibujar" al pasar por un HDC
        /// y esos píxeles se quedaban transparentes (o negros), lo que en fotos oscuras se veía como puntos.
        /// Aquí se copia la pantalla con BitBlt a un DIB propio y se pasa tal cual al bitmap.
        /// </remarks>
        public static Bitmap CaptureVirtualScreen(out Rectangle bounds) {
            bounds = SystemInformation.VirtualScreen;
            int w = bounds.Width, h = bounds.Height;

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            // Alto negativo: filas de arriba abajo, como en Bitmap
            var header = new NativeMethods.BitmapInfoHeader {
                biSize = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
            };
            IntPtr dib = NativeMethods.CreateDIBSection(screenDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr previous = NativeMethods.SelectObject(memDc, dib);
            try {
                NativeMethods.BitBlt(memDc, 0, 0, w, h, screenDc, bounds.X, bounds.Y, NativeMethods.SRCCOPY);

                // PArgb es el formato que GDI+ dibuja más rápido. La pantalla es opaca: alfa a 255 en todos los píxeles
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
                try {
                    var row = new int[w];
                    for (int y = 0; y < h; y++) {
                        Marshal.Copy(bits + y * w * 4, row, 0, w);
                        for (int x = 0; x < w; x++) row[x] |= unchecked((int)0xFF000000);
                        Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
                    }
                } finally {
                    bmp.UnlockBits(data);
                }
                return bmp;
            } finally {
                NativeMethods.SelectObject(memDc, previous);
                NativeMethods.DeleteObject(dib);
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
