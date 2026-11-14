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

            Tone tone = AnalyzeTone(source);

            // Primera pasada con una ampliación pensada para la letra habitual de pantalla
            float scale = ClampScale(source, InitialScale);
            OcrResult result = await RecognizeAt(engine, source, scale, tone);

            // El motor acierta más con letras de unos 30-40 píxeles de alto: si la que ha encontrado
            // queda lejos de eso (letra muy pequeña o muy grande), se repite con la ampliación justa
            double height = MedianLineHeight(result);
            if (height > 0 && (height < TargetLineHeight * 0.7 || height > TargetLineHeight * 1.8)) {
                float better = ClampScale(source, scale * (float)(TargetLineHeight / height));
                if (Math.Abs(better / scale - 1) > 0.2f) {
                    OcrResult second = await RecognizeAt(engine, source, better, tone);
                    if (second.Lines.Count > 0) result = second;
                }
            }

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

        // ───────────────────────────── Preparación de la imagen ─────────────────────────────

        /// <summary>Ampliación de la primera pasada: la letra de pantalla (11-16 px) queda en unos 30-45 px.</summary>
        private const float InitialScale = 3f;
        /// <summary>Alto de renglón, en píxeles de la imagen ampliada, con el que mejor lee el motor.</summary>
        private const double TargetLineHeight = 36;
        private const float MinScale = 0.5f, MaxScale = 6f;
        /// <summary>Límite de píxeles de la imagen ampliada, para no gastar demasiada memoria ni tiempo.</summary>
        private const double MaxPixels = 8_000_000;

        /// <summary>
        /// Brillo del fondo y de la tinta de la zona, para pasarla a texto oscuro sobre fondo claro
        /// con el máximo contraste (el motor falla más con texto claro sobre fondo oscuro o con poco contraste).
        /// </summary>
        private readonly struct Tone {
            public readonly bool Invert;
            /// <summary>Brillo (ya invertido si hace falta) que pasa a negro y a blanco.</summary>
            public readonly int Ink, Paper;
            public Tone(bool invert, int ink, int paper) { Invert = invert; Ink = ink; Paper = paper; }
        }

        private static int Luma(byte b, byte g, byte r) => (r * 299 + g * 587 + b * 114) / 1000;

        private static Tone AnalyzeTone(Bitmap source) {
            int w = source.Width, h = source.Height;
            byte[] px = ReadPixels(source, out int stride);
            var all = new int[256];
            var border = new int[256];
            // Basta con una muestra de unos 400.000 píxeles
            int step = Math.Max(1, (int)Math.Sqrt((double)w * h / 400_000));
            for (int y = 0; y < h; y += step) {
                int row = y * stride;
                for (int x = 0; x < w; x += step) {
                    int i = row + x * 4;
                    all[Luma(px[i], px[i + 1], px[i + 2])]++;
                }
            }
            // El fondo es lo que hay en los bordes de la zona
            for (int x = 0; x < w; x++) { AddBorder(x, 0); AddBorder(x, h - 1); }
            for (int y = 0; y < h; y++) { AddBorder(0, y); AddBorder(w - 1, y); }
            void AddBorder(int x, int y) {
                int i = y * stride + x * 4;
                border[Luma(px[i], px[i + 1], px[i + 2])]++;
            }

            int background = Percentile(border, 0.5);
            bool invert = background < 128;
            if (invert) {
                Array.Reverse(all);
                background = 255 - background;
            }

            // Tinta: lo más oscuro, sin hacer caso de unos pocos píxeles sueltos
            int total = all.Sum();
            int need = Math.Max(5, total / 2000), seen = 0, ink = 0;
            for (int v = 0; v < 256; v++) {
                seen += all[v];
                if (seen >= need) { ink = v; break; }
            }
            // Sin contraste apreciable (zona lisa): no se estira
            if (background - ink < 8) { ink = 0; background = 255; }
            return new Tone(invert, ink, background);
        }

        private static int Percentile(int[] histogram, double p) {
            int total = histogram.Sum(), target = (int)(total * p), seen = 0;
            for (int v = 0; v < 256; v++) {
                seen += histogram[v];
                if (seen > target) return v;
            }
            return 255;
        }

        /// <summary>Ampliación limitada por el tamaño máximo que admite el motor y por la memoria.</summary>
        private static float ClampScale(Bitmap source, float scale) {
            scale = Math.Max(MinScale, Math.Min(MaxScale, scale));
            double max = OcrEngine.MaxImageDimension - Margin(scale) * 2;
            scale = (float)Math.Min(scale, max / Math.Max(source.Width, source.Height));
            scale = (float)Math.Min(scale, Math.Sqrt(MaxPixels / ((double)source.Width * source.Height)));
            return Math.Max(0.1f, scale);
        }

        /// <summary>Margen alrededor: el motor lee mal (o no lee) las letras que tocan el borde de la imagen.</summary>
        private static int Margin(float scale) => (int)Math.Round(Math.Max(16, 10 * scale));

        private static async Task<OcrResult> RecognizeAt(OcrEngine engine, Bitmap source, float scale, Tone tone) {
            using SoftwareBitmap bitmap = Prepare(source, scale, tone);
            return await engine.RecognizeAsync(bitmap);
        }

        /// <summary>
        /// Amplía la zona, la pasa a gris con el texto oscuro sobre fondo blanco y el contraste estirado
        /// (así también desaparecen los bordes de colores del ClearType) y le añade un margen blanco.
        /// </summary>
        private static SoftwareBitmap Prepare(Bitmap source, float scale, Tone tone) {
            int w = Math.Max(1, (int)Math.Round(source.Width * scale));
            int h = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled)) {
                g.InterpolationMode = scale >= 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Los bordes se repiten hacia fuera en lugar de mezclarse con negro
                using var attrs = new ImageAttributes();
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
            }

            // Tabla de brillo: invertir si hace falta y estirar [tinta, papel] a [0, 255]
            var map = new byte[256];
            int range = Math.Max(1, tone.Paper - tone.Ink);
            for (int v = 0; v < 256; v++) {
                int l = tone.Invert ? 255 - v : v;
                map[v] = (byte)Math.Max(0, Math.Min(255, (l - tone.Ink) * 255 / range));
            }

            byte[] src = ReadPixels(scaled, out int stride);
            int margin = Margin(scale);
            int outW = w + margin * 2, outH = h + margin * 2;
            byte[] dst = new byte[outW * outH * 4];
            for (int i = 0; i < dst.Length; i++) dst[i] = 255;   // margen blanco y opaco
            for (int y = 0; y < h; y++) {
                int s = y * stride, d = ((y + margin) * outW + margin) * 4;
                for (int x = 0; x < w; x++, s += 4, d += 4) {
                    byte v = map[Luma(src[s], src[s + 1], src[s + 2])];
                    dst[d] = dst[d + 1] = dst[d + 2] = v;
                }
            }
            return SoftwareBitmap.CreateCopyFromBuffer(dst.AsBuffer(), BitmapPixelFormat.Bgra8, outW, outH, BitmapAlphaMode.Premultiplied);
        }

        private static byte[] ReadPixels(Bitmap bmp, out int stride) {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                stride = data.Stride;
                byte[] pixels = new byte[stride * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return pixels;
            } finally {
                bmp.UnlockBits(data);
            }
        }

        /// <summary>Alto típico de los renglones encontrados, en píxeles de la imagen que ha leído el motor.</summary>
        private static double MedianLineHeight(OcrResult result) {
            var heights = new List<double>();
            foreach (OcrLine line in result.Lines) {
                if (line.Words.Count == 0) continue;
                double top = double.MaxValue, bottom = double.MinValue;
                foreach (OcrWord word in line.Words) {
                    top = Math.Min(top, word.BoundingRect.Y);
                    bottom = Math.Max(bottom, word.BoundingRect.Y + word.BoundingRect.Height);
                }
                heights.Add(bottom - top);
            }
            if (heights.Count == 0) return 0;
            heights.Sort();
            return heights[heights.Count / 2];
        }
    }
}
