using System.Drawing.Imaging;

namespace Trazo {

    internal static class ScreenCapture {

        /// <summary>
        /// Captura todos los monitores a la vez. <paramref name="bounds"/> es el rectángulo
        /// del escritorio virtual (puede tener origen negativo si hay monitores a la izquierda/arriba).
        /// </summary>
        public static Bitmap CaptureVirtualScreen(out Rectangle bounds) {
            bounds = SystemInformation.VirtualScreen;
            // PArgb es el formato que GDI+ dibuja más rápido
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
    }
}
