using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>Utilidades de dibujo y de ventanas compartidas por todas las pantallas de Trazo.</summary>
    internal static class Ui {

        /// <summary>Windows 11 pone él mismo esquinas redondeadas y borde a las ventanas; Windows 10, no.</summary>
        public static bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;

        /// <summary>Ventana con barra de título: oscura, del color del fondo y sin icono ni texto (siguen en la barra de tareas).</summary>
        public static void StyleDialog(IntPtr handle) {
            SetAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
            SetAttribute(handle, NativeMethods.DWMWA_CAPTION_COLOR, ColorTranslator.ToWin32(SettingsForm.Back));
            SetAttribute(handle, NativeMethods.DWMWA_BORDER_COLOR, ColorTranslator.ToWin32(SettingsForm.Edge));
            NativeMethods.HideCaption(handle);
        }

        /// <summary>Ventana sin barra (menús, avisos, selector de color): esquinas redondeadas y borde gris fino.</summary>
        public static void StylePopup(IntPtr handle) {
            SetAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, NativeMethods.DWMWCP_ROUND);
            SetAttribute(handle, NativeMethods.DWMWA_BORDER_COLOR, ColorTranslator.ToWin32(SettingsForm.Edge));
        }

        private static void SetAttribute(IntPtr handle, int attribute, int value) =>
            NativeMethods.DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

        /// <summary>El borde que Windows 10 no pone a las ventanas sin barra.</summary>
        public static void DrawWindows10Border(Graphics g, Size size) {
            if (IsWindows11) return;
            using var pen = new Pen(SettingsForm.Edge);
            g.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
        }

        public static void OpenUrl(string url) {
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception) {
                // Sin programa asociado (por ejemplo, sin navegador predeterminado): no hay nada más que hacer
            }
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius) => RoundedRect((RectangleF)r, radius);

        public static GraphicsPath RoundedRect(RectangleF r, float radius) {
            float d = radius * 2;
            var path = new GraphicsPath();
            if (d <= 0) {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Código #RRGGBB de un color (sin transparencia).</summary>
        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>Acepta RRGGBB o RGB, con o sin almohadilla.</summary>
        public static bool TryParseHex(string? text, out Color color) {
            color = Color.Empty;
            if (text == null) return false;
            string hex = text.Trim().TrimStart('#');
            if (hex.Length == 3) hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }
    }

    /// <summary>Ventana con barra de título y el aspecto oscuro de Trazo: configuración, atajos y mensajes.</summary>
    internal abstract class DarkDialog : Form {

        private readonly List<Font> fonts = new();

        protected DarkDialog() {
            BackColor = SettingsForm.Back;
            ForeColor = SettingsForm.TextColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
        }

        /// <summary>Fuente que se libera con la ventana.</summary>
        protected Font NewFont(string family, float size, GraphicsUnit unit = GraphicsUnit.Point) {
            var font = new Font(family, size, FontStyle.Regular, unit);
            fonts.Add(font);
            return font;
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Ui.StyleDialog(Handle);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                foreach (Font f in fonts) f.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Avisos con forma de tarjeta oscura: los de dentro de la captura y los de las ventanas flotantes
    /// (<see cref="Toast"/>) se miden y dibujan con el mismo código para que se vean iguales.
    /// </summary>
    internal sealed class CardStyle : IDisposable {

        public static readonly Color Back = Color.FromArgb(28, 28, 30);
        private static readonly Color OkGreen = Color.FromArgb(90, 200, 120);
        private static readonly Color FootGray = Color.FromArgb(150, 150, 150);
        private static readonly Color OcrBorder = Color.FromArgb(90, 0, 150, 255);
        private const TextFormatFlags BodyFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        private readonly float scale;
        /// <summary>Texto de los avisos de una línea (y de otras etiquetas de la captura).</summary>
        public Font Label { get; }
        private readonly Font title, body, foot;

        public CardStyle(float scale) {
            this.scale = scale;
            Label = PixelFont("Segoe UI", 12);
            title = PixelFont("Segoe UI Semibold", 12);
            body = PixelFont("Segoe UI", 13.5f);
            foot = PixelFont("Segoe UI", 11);
        }

        private Font PixelFont(string family, float pixels) => new(family, pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        private int Px(float logical) => (int)(logical * scale);
        private int Pad => Px(14);
        private int SwatchSize => Px(26);
        private int TitleHeight => Px(24);
        private int FootHeight => Px(20);

        /// <summary>Arriba y centrado en <paramref name="area"/>, el sitio de todos los avisos.</summary>
        public Rectangle TopCenter(Rectangle area, Size size) {
            int y = Math.Max(area.Top, Math.Min(area.Top + Px(16), area.Bottom - size.Height));
            return new Rectangle(area.Left + (area.Width - size.Width) / 2, y, size.Width, size.Height);
        }

        // ── Aviso de una línea, con una muestra de color opcional ──

        public Size MeasureMessage(string message, bool swatch) {
            Size text = TextRenderer.MeasureText(message, Label);
            int padY = Px(8);
            int width = text.Width + (swatch ? SwatchSize + Pad / 2 * 2 : 0) + Pad * 2;
            int height = Math.Max(text.Height, swatch ? SwatchSize : 0) + padY * 2;
            return new Size(width, height);
        }

        public void DrawMessage(Graphics g, Rectangle r, string message, Color? swatch, Color back) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedRect(r, Px(8)))
            using (var brush = new SolidBrush(back)) {
                g.FillPath(brush, path);
            }

            Rectangle textRect = r;
            if (swatch is Color c) {
                var box = new Rectangle(r.X + Pad, r.Y + (r.Height - SwatchSize) / 2, SwatchSize, SwatchSize);
                using (GraphicsPath path = Ui.RoundedRect(box, Px(5)))
                using (var fill = new SolidBrush(c))
                using (var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                textRect = Rectangle.FromLTRB(box.Right + Pad / 2, r.Y, r.Right - Pad / 2, r.Bottom);
            }
            TextRenderer.DrawText(g, message, Label, textRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ── Texto leído con el OCR ──

        public Size MeasureOcr(string text) {
            int width = Px(380);
            Size size = TextRenderer.MeasureText(text, body, new Size(width - Pad * 2, int.MaxValue), BodyFlags);
            return new Size(width, Pad + TitleHeight + Math.Min(size.Height, Px(260)) + Px(4) + FootHeight + Pad);
        }

        /// <param name="hint">Añadido al pie (por ejemplo, cómo ocultar la tarjeta); null si no hace falta.</param>
        public void DrawOcr(Graphics g, Rectangle r, string text, string? hint, Color back) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f), Px(10))) {
                using var brush = new SolidBrush(back);
                g.FillPath(brush, path);
                using var border = new Pen(OcrBorder, 1);
                g.DrawPath(border, path);
            }

            var titleRect = new Rectangle(r.X + Pad, r.Y + Pad, r.Width - Pad * 2, TitleHeight);
            TextRenderer.DrawText(g, "✓  Texto copiado al portapapeles", title, titleRect, OkGreen, TextFormatFlags.Left | TextFormatFlags.Top);

            var footRect = new Rectangle(titleRect.Left, r.Bottom - Pad - FootHeight, titleRect.Width, FootHeight);
            var bodyRect = Rectangle.FromLTRB(titleRect.Left, titleRect.Bottom, titleRect.Right, footRect.Top - Px(4));
            TextRenderer.DrawText(g, text, body, bodyRect, Color.White, BodyFlags);

            int lines = text.Split('\n').Length;
            string summary = $"{lines} {(lines == 1 ? "línea" : "líneas")} · {text.Length} caracteres";
            if (hint != null) summary += " · " + hint;
            TextRenderer.DrawText(g, summary, foot, footRect, FootGray, TextFormatFlags.Left | TextFormatFlags.Bottom);
        }

        public void Dispose() {
            Label.Dispose();
            title.Dispose();
            body.Dispose();
            foot.Dispose();
        }
    }
}
