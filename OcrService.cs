using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
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

            return JoinParagraphs(result.Lines);
        }

        /// <summary>
        /// El motor devuelve una línea por cada renglón visual, así que un párrafo que en pantalla
        /// ocupa varios renglones saldría partido. Se unen los renglones que, por su posición,
        /// son continuación del anterior; los títulos, listas y líneas sueltas se mantienen aparte.
        /// </summary>
        private static string JoinParagraphs(IReadOnlyList<OcrLine> lines) {
            var sb = new StringBuilder();
            LineBox? prev = null;

            foreach (OcrLine line in lines) {
                if (line.Words.Count == 0) continue;
                var box = new LineBox(line);

                if (prev == null) {
                    sb.Append(box.Text);
                } else if (Continues(prev, box)) {
                    // Palabra cortada con guion al final del renglón: se recompone
                    string done = sb.ToString();
                    if (done.Length >= 2 && done[done.Length - 1] == '-' && char.IsLetter(done[done.Length - 2]) && char.IsLower(box.Text[0])) {
                        sb.Length--;
                    } else {
                        sb.Append(' ');
                    }
                    sb.Append(box.Text);
                } else {
                    sb.Append(Environment.NewLine).Append(box.Text);
                }
                prev = box;
            }
            return sb.ToString();
        }

        private static bool Continues(LineBox prev, LineBox next) {
            double h = Math.Max(prev.Height, next.Height);

            // Tamaño de letra parecido (un título no se une con el texto que le sigue)
            if (Math.Min(prev.Height, next.Height) < h * 0.75) return false;

            // Justo debajo, sin el hueco que separa párrafos
            double gap = next.Top - prev.Bottom;
            if (gap < -h * 0.3 || gap > h * 0.75) return false;

            // Mismo margen izquierdo (se admite sangría en la primera línea del párrafo)
            double indent = prev.Left - next.Left;
            if (indent < -h || indent > h * 3) return false;

            // Un renglón partido por falta de sitio suele ser largo; los elementos de menús
            // o listas cortas van cada uno en su línea
            if (prev.WordCount < 4) return false;

            // Si la primera palabra del siguiente renglón cabía al final del anterior,
            // el salto era intencionado
            if (next.Right - prev.Right > next.FirstWordWidth + h) return false;

            // Viñetas y listas numeradas empiezan línea nueva
            if (ListMarker.IsMatch(next.Text)) return false;

            return true;
        }

        private static readonly Regex ListMarker = new(@"^\s*([•·▪◦‣►\-–—*]|\d{1,3}[.)]|[a-zA-Z][)])\s");

        private sealed class LineBox {
            public string Text { get; }
            public int WordCount { get; }
            public double Left { get; }
            public double Top { get; }
            public double Right { get; }
            public double Bottom { get; }
            public double FirstWordWidth { get; }
            public double Height => Bottom - Top;

            public LineBox(OcrLine line) {
                Text = line.Text;
                WordCount = line.Words.Count;
                Left = Top = double.MaxValue;
                Right = Bottom = double.MinValue;
                foreach (OcrWord w in line.Words) {
                    var r = w.BoundingRect;
                    Left = Math.Min(Left, r.X);
                    Top = Math.Min(Top, r.Y);
                    Right = Math.Max(Right, r.X + r.Width);
                    Bottom = Math.Max(Bottom, r.Y + r.Height);
                }
                FirstWordWidth = line.Words[0].BoundingRect.Width;
            }
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
