using System.Drawing.Drawing2D;

namespace Trazo {

    internal enum IconKind { Arrow, Rectangle, Ellipse, Text, Picker, Undo, Ocr, Copy, Save, Close }

    /// <summary>
    /// Iconos de la barra dibujados por código: nítidos a cualquier escala y sin archivos de recursos.
    /// Se diseñan en una cuadrícula de 24×24 y se escalan al tamaño pedido.
    /// </summary>
    internal static class Icons {

        /// <summary>Para la barra oscura translúcida.</summary>
        public static readonly Color LightInk = Color.FromArgb(240, 240, 240);
        /// <summary>Para los menús desplegables (fondo claro).</summary>
        public static readonly Color DarkInk = Color.FromArgb(45, 45, 45);

        /// <summary>Logo de Trazo (Trazo.ico embebido) en el tamaño más cercano al pedido.</summary>
        public static Icon AppIcon(int size) => LoadIcon("Trazo.ico", size);

        /// <summary>Símbolo para la bandeja: en negro sobre la barra de tareas clara y en blanco sobre la oscura.</summary>
        public static Icon TrayIcon(int size, bool lightTaskbar) =>
            LoadIcon(lightTaskbar ? "TrazoTrayBlack.ico" : "TrazoTray.ico", size);

        private static Icon LoadIcon(string resource, int size) {
            using Stream stream = typeof(Icons).Assembly.GetManifestResourceStream(resource)!;
            return new Icon(stream, size, size);
        }

        public static Bitmap Create(IconKind kind, int size, Color ink) {
            return Draw(size, g => {
                using var pen = new Pen(ink, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                using var brush = new SolidBrush(ink);

                switch (kind) {
                    case IconKind.Arrow:
                        g.DrawLine(pen, 5, 19, 15, 9);
                        g.FillPolygon(brush, new PointF[] { new(19.5f, 4.5f), new(10.5f, 7), new(17, 13.5f) });
                        break;

                    case IconKind.Rectangle:
                        g.DrawRectangle(pen, 4, 6, 16, 12);
                        break;

                    case IconKind.Ellipse:
                        g.DrawEllipse(pen, 3.5f, 5.5f, 17, 13);
                        break;

                    case IconKind.Text:
                        pen.Width = 2.4f;
                        g.DrawLine(pen, 5, 5, 19, 5);
                        g.DrawLine(pen, 12, 5, 12, 19);
                        g.DrawLine(pen, 9, 19, 15, 19);
                        break;

                    case IconKind.Picker:
                        // Cuentagotas: bulbo arriba a la derecha, tubo en diagonal y punta
                        g.FillEllipse(brush, 14, 3, 7, 7);
                        pen.Width = 3.2f;
                        g.DrawLine(pen, 15.5f, 8.5f, 7, 17);
                        pen.Width = 1.6f;
                        g.DrawLine(pen, 7, 17, 4, 20);
                        pen.Width = 2f;
                        g.DrawLine(pen, 12.5f, 6.5f, 17.5f, 11.5f);
                        break;

                    case IconKind.Undo:
                        g.DrawLine(pen, 7, 9, 14, 9);
                        g.DrawArc(pen, 10, 9, 8, 8, -90, 180);
                        g.DrawLine(pen, 14, 17, 8, 17);
                        g.DrawLines(pen, new PointF[] { new(10.5f, 5.5f), new(7, 9), new(10.5f, 12.5f) });
                        break;

                    case IconKind.Ocr:
                        // Esquinas de "escáner" con líneas de texto dentro
                        g.DrawLines(pen, new PointF[] { new(4, 9), new(4, 4), new(9, 4) });
                        g.DrawLines(pen, new PointF[] { new(15, 4), new(20, 4), new(20, 9) });
                        g.DrawLines(pen, new PointF[] { new(20, 15), new(20, 20), new(15, 20) });
                        g.DrawLines(pen, new PointF[] { new(9, 20), new(4, 20), new(4, 15) });
                        g.DrawLine(pen, 8, 10, 16, 10);
                        g.DrawLine(pen, 8, 14, 13.5f, 14);
                        break;

                    case IconKind.Copy:
                        g.DrawLines(pen, new PointF[] { new(9, 7), new(9, 4), new(20, 4), new(20, 15), new(17, 15) });
                        g.DrawRectangle(pen, 4, 8, 12, 12);
                        break;

                    case IconKind.Save:
                        g.DrawLine(pen, 12, 3.5f, 12, 15);
                        g.DrawLines(pen, new PointF[] { new(7.5f, 10.5f), new(12, 15), new(16.5f, 10.5f) });
                        g.DrawLines(pen, new PointF[] { new(4, 15), new(4, 20), new(20, 20), new(20, 15) });
                        break;

                    case IconKind.Close:
                        g.DrawLine(pen, 6, 6, 18, 18);
                        g.DrawLine(pen, 18, 6, 6, 18);
                        break;
                }
            });
        }

        /// <summary>Cuadrado hueco con el color del borde.</summary>
        public static Bitmap Stroke(Color color, int size) => Draw(size, g => {
            using var outline = new Pen(Color.FromArgb(150, 150, 150), 1f);
            g.DrawRectangle(outline, 3, 3, 18, 18);
            g.DrawRectangle(outline, 8, 8, 8, 8);
            using var pen = new Pen(color, 4f);
            g.DrawRectangle(pen, 5.5f, 5.5f, 13, 13);
        });

        /// <summary>Cuadrado relleno con el color de fondo, o tachado si no hay relleno.</summary>
        public static Bitmap Fill(Color? color, int size) => Draw(size, g => {
            using var outline = new Pen(Color.FromArgb(150, 150, 150), 1f);
            if (color is Color c) {
                using var brush = new SolidBrush(c);
                g.FillRectangle(brush, 3, 3, 18, 18);
            } else {
                g.FillRectangle(Brushes.White, 3, 3, 18, 18);
                using var red = new Pen(Color.FromArgb(220, 40, 40), 2f);
                g.DrawLine(red, 4, 20, 20, 4);
            }
            g.DrawRectangle(outline, 3, 3, 18, 18);
        });

        /// <summary>Línea horizontal con el grosor indicado.</summary>
        public static Bitmap Line(float width, int size, Color ink) => Draw(size, g => {
            using var pen = new Pen(ink, Math.Min(width, 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 5, 12, 19, 12);
        });

        private static Bitmap Draw(int size, Action<Graphics> paint) {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 24f, size / 24f);
            paint(g);
            return bmp;
        }
    }
}
