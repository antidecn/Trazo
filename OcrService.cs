using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Trazo {

    /// <summary>
    /// Reconocimiento de texto con el motor OCR integrado en Windows 10/11.
    /// Usa los idiomas instalados en Windows; no necesita archivos de datos propios.
    /// </summary>
    internal static class OcrService {

        /// <remarks>
        /// El motor se crea en cada lectura y no se guarda: crearlo apenas tarda unos milisegundos
        /// y así su memoria se libera al cerrar la captura en lugar de quedarse en reposo.
        /// </remarks>
        public static async Task<string> RecognizeAsync(Bitmap source) {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Language("es-ES"))
                ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                ?? throw new InvalidOperationException(
                    "No hay ningún idioma de OCR instalado en Windows.\n" +
                    "Instálalo en Configuración > Hora e idioma > Idioma y región.");

            using Bitmap prepared = Prepare(source);
            using SoftwareBitmap softwareBitmap = ToSoftwareBitmap(prepared);
            OcrResult result = await engine.RecognizeAsync(softwareBitmap);

            return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
        }

        /// <summary>
        /// El texto de pantalla suele ser pequeño: se amplía para mejorar la precisión,
        /// respetando el tamaño máximo que admite el motor.
        /// </summary>
        private static Bitmap Prepare(Bitmap source) {
            int max = (int)OcrEngine.MaxImageDimension;
            int longest = Math.Max(source.Width, source.Height);

            float scale = longest < 1000 ? 2f : 1f;
            if (longest * scale > max) scale = (float)max / longest;

            int w = Math.Max(1, (int)(source.Width * scale));
            int h = Math.Max(1, (int)(source.Height * scale));

            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, w, h);
            return result;
        }

        private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp) {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                int stride = data.Stride;
                byte[] pixels = new byte[stride * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                // SoftwareBitmap espera filas contiguas de ancho*4 bytes
                int rowBytes = bmp.Width * 4;
                if (stride != rowBytes) {
                    byte[] packed = new byte[rowBytes * bmp.Height];
                    for (int y = 0; y < bmp.Height; y++) {
                        Buffer.BlockCopy(pixels, y * stride, packed, y * rowBytes, rowBytes);
                    }
                    pixels = packed;
                }

                return SoftwareBitmap.CreateCopyFromBuffer(
                    pixels.AsBuffer(), BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
            } finally {
                bmp.UnlockBits(data);
            }
        }
    }
}
