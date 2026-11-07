using System.Drawing.Drawing2D;

namespace Trazo {

    internal enum IconKind { Arrow, PathArrowStraight, PathArrowRounded, Line, Rectangle, Ellipse, Text, Pixelate, Picker, Undo, Ocr, Copy, Save, Close, Update, Help }

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

                    case IconKind.Line:
                        g.DrawLine(pen, 5, 19, 19, 5);
                        break;

                    case IconKind.PathArrowStraight:
                        g.DrawLines(pen, new PointF[] { new(4, 6), new(16, 6), new(16, 14) });
                        g.FillPolygon(brush, new PointF[] { new(16, 20.5f), new(11.5f, 13.5f), new(20.5f, 13.5f) });
                        break;

                    case IconKind.PathArrowRounded:
                        using (var path = new GraphicsPath()) {
                            path.AddLine(4, 6, 10, 6);
                            path.AddArc(4, 6, 12, 12, 270, 90);
                            path.AddLine(16, 12, 16, 14);
                            g.DrawPath(pen, path);
                        }
                        g.FillPolygon(brush, new PointF[] { new(16, 20.5f), new(11.5f, 13.5f), new(20.5f, 13.5f) });
                        break;

                    case IconKind.Pixelate:
                        // Cuadrícula de bloques con distintas intensidades
                        for (int row = 0; row < 3; row++) {
                            for (int col = 0; col < 3; col++) {
                                int alpha = (row + col) % 2 == 0 ? 255 : 110;
                                using var block = new SolidBrush(Color.FromArgb(alpha * ink.A / 255, ink));
                                g.FillRectangle(block, 4 + col * 5.5f, 4 + row * 5.5f, 5, 5);
                            }
                        }
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

                    case IconKind.Update:
                        // Dos flechas en círculo
                        g.DrawArc(pen, 5, 5, 14, 14, 200, 130);
                        g.DrawArc(pen, 5, 5, 14, 14, 20, 130);
                        g.FillPolygon(brush, new PointF[] { new(20.5f, 6.5f), new(20, 12.5f), new(14.5f, 10) });
                        g.FillPolygon(brush, new PointF[] { new(3.5f, 17.5f), new(4, 11.5f), new(9.5f, 14) });
                        break;

                    case IconKind.Help:
                        g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                        using (var path = new GraphicsPath()) {
                            path.AddArc(9, 7, 6, 6, 180, 270);
                            path.AddLine(12, 13, 12, 14.5f);
                            g.DrawPath(pen, path);
                        }
                        g.FillEllipse(brush, 10.8f, 16.3f, 2.4f, 2.4f);
                        break;
                }
            });
        }

        /// <summary>
        /// Botón redondo de mover la zona: cristal oscuro como la barra y la cruz de flechas dibujada en vectores,
        /// centrada exactamente en el círculo (una imagen pequeña se desplaza medio píxel o uno al redondear).
        /// </summary>
        /// <param name="highlight">Alfa del resaltado blanco (0 = ninguno).</param>
        public static void DrawMoveButton(Graphics g, Rectangle r, float scale, int highlight, Color edge) {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Con medio píxel de desplazamiento las coordenadas son los bordes de los píxeles: el círculo ocupa
            // exactamente el rectángulo y su centro es el del rectángulo
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var back = new SolidBrush(Color.FromArgb(225, 24, 24, 26))) g.FillEllipse(back, r);
            if (highlight > 0) {
                using var light = new SolidBrush(Color.FromArgb(highlight, 255, 255, 255));
                g.FillEllipse(light, r);
            }
            using (var ring = new Pen(edge, 1f)) g.DrawEllipse(ring, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);

            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            // Proporcional al círculo, para que la cruz quepa sea cual sea su tamaño
            float arm = r.Width * 0.26f, head = r.Width * 0.09f;
            using var pen = new Pen(LightInk, 1.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
            g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            foreach ((float dx, float dy) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) }) {
                // Punta en el extremo de cada brazo, abierta hacia el centro
                float tx = cx + dx * arm, ty = cy + dy * arm;
                g.DrawLines(pen, new[] {
                    new PointF(tx - dx * head - dy * head, ty - dy * head - dx * head),
                    new PointF(tx, ty),
                    new PointF(tx - dx * head + dy * head, ty - dy * head + dx * head),
                });
            }
            g.Restore(state);
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
