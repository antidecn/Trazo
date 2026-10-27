using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Trazo {

    /// <summary>
    /// Elemento dibujado sobre la captura. Se guarda como vector para poder moverlo,
    /// recolorearlo o borrarlo antes de exportar.
    /// </summary>
    internal abstract class Shape {

        /// <summary>Distancia en píxeles a la que se considera que el ratón está "sobre" un trazo.</summary>
        protected const float HitTolerance = 7;

        public Color Stroke { get; set; } = Color.Red;
        public Color? Fill { get; set; }
        public float Width { get; set; } = 3;

        public abstract void Draw(Graphics g);
        public abstract Rectangle Bounds { get; }
        public abstract bool HitTest(Point p);
        public abstract void Offset(int dx, int dy);

        public Shape Clone() => (Shape)MemberwiseClone();

        protected Pen CreatePen() => new(Stroke, Width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

        protected static Rectangle Normalize(Point a, Point b) =>
            Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        protected Rectangle Padded(Rectangle r) {
            int pad = (int)Math.Ceiling(Width) + 2;
            r.Inflate(pad, pad);
            return r;
        }
    }

    /// <summary>Figura definida por dos esquinas (rectángulo, elipse).</summary>
    internal abstract class BoxShape : Shape {

        public Point Start { get; set; }
        public Point End { get; set; }

        public Rectangle Rect => Normalize(Start, End);
        public override Rectangle Bounds => Padded(Rect);

        public override void Offset(int dx, int dy) {
            Start = new Point(Start.X + dx, Start.Y + dy);
            End = new Point(End.X + dx, End.Y + dy);
        }

        protected abstract void AddToPath(GraphicsPath path, Rectangle r);

        public override void Draw(Graphics g) {
            Rectangle r = Rect;
            if (r.Width < 1 || r.Height < 1) return;
            using var path = new GraphicsPath();
            AddToPath(path, r);
            if (Fill is Color fill) {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            using var pen = CreatePen();
            g.DrawPath(pen, path);
        }

        public override bool HitTest(Point p) {
            Rectangle r = Rect;
            if (r.Width < 1 || r.Height < 1) return false;
            using var path = new GraphicsPath();
            AddToPath(path, r);
            if (Fill != null && path.IsVisible(p)) return true;
            using var pen = new Pen(Color.Black, Width + HitTolerance * 2);
            return path.IsOutlineVisible(p, pen);
        }
    }

    internal sealed class RectangleShape : BoxShape {
        protected override void AddToPath(GraphicsPath path, Rectangle r) => path.AddRectangle(r);
    }

    internal sealed class EllipseShape : BoxShape {
        protected override void AddToPath(GraphicsPath path, Rectangle r) => path.AddEllipse(r);
    }

    /// <summary>Figura definida por dos extremos (flecha, línea).</summary>
    internal abstract class SegmentShape : Shape {

        public Point Start { get; set; }
        public Point End { get; set; }

        public override Rectangle Bounds => Padded(Normalize(Start, End));

        public override void Offset(int dx, int dy) {
            Start = new Point(Start.X + dx, Start.Y + dy);
            End = new Point(End.X + dx, End.Y + dy);
        }

        public override bool HitTest(Point p) {
            using var path = new GraphicsPath();
            path.AddLine(Start, End);
            using var pen = new Pen(Color.Black, Width + HitTolerance * 2);
            return path.IsOutlineVisible(p, pen);
        }
    }

    internal sealed class LineShape : SegmentShape {
        public override void Draw(Graphics g) {
            if (Start == End) return;
            using var pen = CreatePen();
            g.DrawLine(pen, Start, End);
        }
    }

    internal sealed class ArrowShape : SegmentShape {

        private float HeadLength => Math.Max(12, Width * 4);

        public override Rectangle Bounds {
            get {
                Rectangle r = Normalize(Start, End);
                int pad = (int)Math.Ceiling(HeadLength);
                r.Inflate(pad, pad);
                return r;
            }
        }

        public override void Draw(Graphics g) {
            float dx = End.X - Start.X, dy = End.Y - Start.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;

            float ux = dx / len, uy = dy / len;
            float head = Math.Min(HeadLength, len);
            float halfWidth = head * 0.55f;

            // La línea termina en la base de la punta para que no asome por delante
            var baseCenter = new PointF(End.X - ux * head, End.Y - uy * head);
            using (var pen = CreatePen()) {
                g.DrawLine(pen, Start, baseCenter);
            }

            PointF[] tip = {
                End,
                new(baseCenter.X - uy * halfWidth, baseCenter.Y + ux * halfWidth),
                new(baseCenter.X + uy * halfWidth, baseCenter.Y - ux * halfWidth),
            };
            using var brush = new SolidBrush(Stroke);
            g.FillPolygon(brush, tip);
        }
    }

    /// <summary>
    /// Zona de la captura pixelada para ocultar lo que hay debajo. Trabaja sobre la captura original:
    /// lo dibujado antes queda tapado y lo dibujado después, encima.
    /// </summary>
    internal sealed class PixelateShape : BoxShape {

        /// <summary>Lado de cada bloque, en píxeles de la captura.</summary>
        private const int BlockSize = 12;

        public PixelateShape(Bitmap source) => this.source = source;

        private readonly Bitmap source;

        // El resultado se reutiliza mientras no cambie la zona (se repinta a menudo).
        // No se libera al sustituirlo: las copias para deshacer pueden seguir usándolo.
        private Bitmap? cache;
        private Rectangle cacheRect;

        protected override void AddToPath(GraphicsPath path, Rectangle r) => path.AddRectangle(r);

        public override void Draw(Graphics g) {
            Rectangle r = Rectangle.Intersect(Rect, new Rectangle(Point.Empty, source.Size));
            if (r.Width < 1 || r.Height < 1) return;
            if (cache == null || cacheRect != r) {
                cache = Pixelate(r);
                cacheRect = r;
            }
            GraphicsState state = g.Save();
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(cache, r, new Rectangle(Point.Empty, cache.Size), GraphicsUnit.Pixel);
            g.Restore(state);
        }

        public override bool HitTest(Point p) => Rect.Contains(p);

        /// <summary>Color medio de cada bloque (reducción de calidad alta) ampliado sin suavizar.</summary>
        private Bitmap Pixelate(Rectangle r) {
            int w = (r.Width + BlockSize - 1) / BlockSize, h = (r.Height + BlockSize - 1) / BlockSize;
            using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            using (var attrs = new ImageAttributes()) {
                // Sin esto, los bordes se mezclan con el exterior transparente y salen oscuros
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, w, h), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, attrs);
            }

            var result = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result)) {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, new Rectangle(0, 0, w * BlockSize, h * BlockSize), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    internal sealed class TextShape : Shape {

        private const float Padding = 4;
        private static readonly Graphics Measurer = CreateMeasurer();
        // Formato tipográfico: lo que se mide coincide exactamente con lo que se dibuja (necesario para el cursor)
        private static readonly StringFormat Format = CreateFormat();

        public Point Location { get; set; }
        public string Text { get; set; } = "";
        public const string DefaultFontFamily = "Arial";

        public string FontFamily { get; set; } = DefaultFontFamily;
        /// <summary>Altura de la fuente en píxeles de la captura.</summary>
        public float FontSize { get; set; } = 24;

        /// <summary>
        /// Negrita si la fuente la tiene (se lee mejor sobre capturas); si no, su estilo normal.
        /// Si la fuente ya no está instalada, se usa Arial.
        /// </summary>
        public Font CreateFont() {
            try {
                using var family = new FontFamily(FontFamily);
                FontStyle style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                    : family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                    : family.IsStyleAvailable(FontStyle.Italic) ? FontStyle.Italic
                    : FontStyle.Bold | FontStyle.Italic;
                return new Font(family, FontSize, style, GraphicsUnit.Pixel);
            } catch (ArgumentException) {
                return new Font(DefaultFontFamily, FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            }
        }

        private static Graphics CreateMeasurer() {
            var g = Graphics.FromImage(new Bitmap(1, 1));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            return g;
        }

        private static StringFormat CreateFormat() {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        private float LineHeight(Font font) => font.GetHeight(Measurer);

        private SizeF Measure() {
            using var font = CreateFont();
            SizeF size = Text.Length == 0 ? new SizeF(FontSize * 0.5f, 0) : Measurer.MeasureString(Text, font, PointF.Empty, Format);
            int lines = Text.Split('\n').Length;
            float height = Math.Max(size.Height, lines * LineHeight(font));
            return new SizeF(size.Width + Padding * 2, height + Padding * 2);
        }

        /// <summary>Línea vertical donde se dibuja el cursor de escritura para la posición indicada.</summary>
        public (PointF top, PointF bottom) Caret(int index) {
            using var font = CreateFont();
            string prefix = Text.Substring(0, MathUtil.Clamp(index, 0, Text.Length));
            int line = prefix.Count(c => c == '\n');
            string lineText = prefix.Substring(prefix.LastIndexOf('\n') + 1).TrimEnd('\r');

            float x = lineText.Length == 0 ? 0 : Measurer.MeasureString(lineText, font, PointF.Empty, Format).Width;
            float lineHeight = LineHeight(font);
            var top = new PointF(Location.X + Padding + x, Location.Y + Padding + line * lineHeight);
            return (top, new PointF(top.X, top.Y + lineHeight));
        }

        public override Rectangle Bounds {
            get {
                SizeF s = Measure();
                return new Rectangle(Location, Size.Ceiling(s));
            }
        }

        public override void Offset(int dx, int dy) => Location = new Point(Location.X + dx, Location.Y + dy);

        public override void Draw(Graphics g) {
            if (Text.Length == 0) return;
            Rectangle r = Bounds;
            if (Fill is Color fill) {
                using var back = new SolidBrush(fill);
                g.FillRectangle(back, r);
            }
            using var font = CreateFont();
            using var brush = new SolidBrush(Stroke);
            g.DrawString(Text, font, brush, new PointF(Location.X + Padding, Location.Y + Padding), Format);
        }

        public override bool HitTest(Point p) => Bounds.Contains(p);
    }
}
