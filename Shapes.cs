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

        protected Rectangle Padded(Rectangle r) {
            int pad = (int)Math.Ceiling(Width) + 2;
            r.Inflate(pad, pad);
            return r;
        }

        protected float HeadLength => Math.Max(12, Width * 4);

        /// <summary>
        /// Triángulo de la punta de una flecha que llega a <paramref name="tip"/> desde <paramref name="from"/>;
        /// null si los dos puntos coinciden. <paramref name="length"/> es lo que mide la punta: el trazo debe
        /// acabar ahí para que no asome por delante.
        /// </summary>
        protected PointF[]? ArrowHead(PointF from, PointF tip, out float length) {
            float dx = tip.X - from.X, dy = tip.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            length = Math.Min(HeadLength, len);
            if (len < 1) return null;

            float ux = dx / len, uy = dy / len;
            float halfWidth = length * 0.55f;
            var baseCenter = new PointF(tip.X - ux * length, tip.Y - uy * length);
            return new[] {
                tip,
                new PointF(baseCenter.X - uy * halfWidth, baseCenter.Y + ux * halfWidth),
                new PointF(baseCenter.X + uy * halfWidth, baseCenter.Y - ux * halfWidth),
            };
        }

        protected void FillArrowHead(Graphics g, PointF[] head) {
            using var brush = new SolidBrush(Stroke);
            g.FillPolygon(brush, head);
        }
    }

    /// <summary>Figura definida por dos esquinas (rectángulo, elipse).</summary>
    internal abstract class BoxShape : Shape {

        public Point Start { get; set; }
        public Point End { get; set; }

        public Rectangle Rect => MathUtil.Normalize(Start, End);
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

        public override Rectangle Bounds => Padded(MathUtil.Normalize(Start, End));

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

        public override Rectangle Bounds {
            get {
                Rectangle r = MathUtil.Normalize(Start, End);
                int pad = (int)Math.Ceiling(HeadLength);
                r.Inflate(pad, pad);
                return r;
            }
        }

        public override void Draw(Graphics g) {
            PointF[]? head = ArrowHead(Start, End, out _);
            if (head == null) return;
            // Hasta el centro de la base de la punta
            var baseCenter = new PointF((head[1].X + head[2].X) / 2, (head[1].Y + head[2].Y) / 2);
            using (var pen = CreatePen()) {
                g.DrawLine(pen, Start, baseCenter);
            }
            FillArrowHead(g, head);
        }
    }

    /// <summary>
    /// Flecha pintada a mano: el recorrido se endereza en tramos rectos (horizontales y verticales
    /// si se acercan a ellos) unidos en ángulo recto o con esquinas redondeadas.
    /// </summary>
    internal sealed class PathArrowShape : Shape {

        /// <summary>Vértices del recorrido ya enderezado, del origen a la punta.</summary>
        public Point[] Points { get; private set; }
        public bool Rounded { get; set; }

        // Trazo a mano mientras se pinta (no se muestra: solo sirve para calcular el recorrido); se descarta al terminar
        private List<Point>? trace;

        public PathArrowShape(Point start) {
            trace = new List<Point> { start };
            Points = new[] { start, start };
        }

        private float CornerRadius => 14 + Width * 3;

        public void AddPoint(Point p, bool axisOnly) {
            if (trace == null) return;
            trace.Add(p);
            Points = PathRouter.Route(trace, axisOnly);
        }

        /// <summary>Termina de pintar: el trazo a mano ya no hace falta.</summary>
        public void Finish() => trace = null;

        // Edición de un punto clave: recorrido al empezar a arrastrar y qué tramos eran horizontales o verticales
        private Point[]? editOrigin;
        private bool[]? editHorizontal, editVertical;

        public void BeginEdit() {
            editOrigin = Points;
            int segments = Points.Length - 1;
            editHorizontal = new bool[segments];
            editVertical = new bool[segments];
            for (int k = 0; k < segments; k++) {
                Point a = Points[k], b = Points[k + 1];
                editHorizontal[k] = a.Y == b.Y && a.X != b.X;
                editVertical[k] = a.X == b.X && a.Y != b.Y;
            }
        }

        /// <summary>
        /// Lleva el punto clave <paramref name="index"/> a <paramref name="p"/>. Los tramos horizontales y verticales
        /// que salen de él lo siguen siendo: el punto vecino se desplaza con él (como al mover un tramo entero).
        /// </summary>
        public void MoveVertex(int index, Point p) {
            if (editOrigin == null || editHorizontal == null || editVertical == null) return;
            // Copia nueva: las copias para deshacer comparten la anterior
            var pts = (Point[])editOrigin.Clone();
            pts[index] = p;
            if (index > 0) {
                if (editHorizontal[index - 1]) pts[index - 1].Y = p.Y;
                else if (editVertical[index - 1]) pts[index - 1].X = p.X;
            }
            if (index < pts.Length - 1) {
                if (editHorizontal[index]) pts[index + 1].Y = p.Y;
                else if (editVertical[index]) pts[index + 1].X = p.X;
            }
            Points = pts;
        }

        /// <summary>Termina de arrastrar: se quitan los puntos que hayan quedado repetidos o en medio de un tramo.</summary>
        public void EndEdit() {
            editOrigin = null;
            editHorizontal = editVertical = null;
            var pts = Points.ToList();
            PathRouter.Clean(pts);
            if (pts.Count >= 2) Points = pts.ToArray();
        }

        public bool TooSmall {
            get {
                double length = 0;
                for (int i = 1; i < Points.Length; i++) length += MathUtil.Distance(Points[i - 1], Points[i]);
                return length < 8;
            }
        }

        public override Rectangle Bounds {
            get {
                Rectangle r = Rectangle.FromLTRB(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X), Points.Max(p => p.Y));
                int pad = (int)Math.Ceiling(Math.Max(HeadLength, Width)) + 2;
                r.Inflate(pad, pad);
                return r;
            }
        }

        public override void Offset(int dx, int dy) {
            // Copia nueva: las copias para deshacer comparten la anterior
            Points = Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToArray();
        }

        public override bool HitTest(Point p) {
            using GraphicsPath path = BuildPath(0);
            if (path.PointCount < 2) return false;
            using var pen = new Pen(Color.Black, Width + HitTolerance * 2);
            return path.IsOutlineVisible(p, pen);
        }

        public override void Draw(Graphics g) {
            PointF[]? head = ArrowHead(Points[Points.Length - 2], Points[Points.Length - 1], out float length);
            if (head == null) return;
            using (GraphicsPath path = BuildPath(length))
            using (Pen pen = CreatePen()) {
                if (!Rounded) pen.LineJoin = LineJoin.Miter;
                g.DrawPath(pen, path);
            }
            FillArrowHead(g, head);
        }

        /// <summary>Recorrido con las esquinas redondeadas si toca, recortado <paramref name="trimEnd"/> px antes de la punta.</summary>
        private GraphicsPath BuildPath(float trimEnd) {
            var pts = Points.Select(p => (PointF)p).ToList();
            int n = pts.Count;
            if (trimEnd > 0) {
                PointF a = pts[n - 2], b = pts[n - 1];
                float len = Length(a, b);
                if (len > 0) pts[n - 1] = Lerp(b, a, Math.Min(trimEnd, len) / len);
            }

            var path = new GraphicsPath();
            if (!Rounded || n < 3) {
                path.AddLines(pts.ToArray());
                return path;
            }

            PointF current = pts[0];
            for (int i = 1; i < n - 1; i++) {
                PointF prev = pts[i - 1], corner = pts[i], next = pts[i + 1];
                float lenIn = Length(prev, corner), lenOut = Length(corner, next);
                if (lenIn < 0.5f || lenOut < 0.5f) continue;

                // Ángulo que gira el recorrido en la esquina
                double inX = (corner.X - prev.X) / lenIn, inY = (corner.Y - prev.Y) / lenIn;
                double outX = (next.X - corner.X) / lenOut, outY = (next.Y - corner.Y) / lenOut;
                double turn = Math.Acos(Math.Max(-1, Math.Min(1, inX * outX + inY * outY)));
                if (turn < 0.01 || turn > Math.PI - 0.01) continue;

                // Los tramos interiores se reparten entre sus dos esquinas; el primero y el último, enteros
                float maxIn = i == 1 ? lenIn : lenIn / 2, maxOut = i == n - 2 ? lenOut : lenOut / 2;
                double tan = Math.Tan(turn / 2);
                float t = (float)Math.Min(CornerRadius * tan, Math.Min(maxIn, maxOut));
                PointF t1 = Lerp(corner, prev, t / lenIn), t2 = Lerp(corner, next, t / lenOut);
                // Curva de Bézier que aproxima un arco de circunferencia
                float k = (float)(4.0 / 3.0 * Math.Tan(turn / 4) / tan);
                path.AddLine(current, t1);
                path.AddBezier(t1, Lerp(t1, corner, k), Lerp(t2, corner, k), t2);
                current = t2;
            }
            path.AddLine(current, pts[n - 1]);
            return path;
        }

        private static float Length(PointF a, PointF b) => (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        private static PointF Lerp(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>Convierte un trazo a mano en unos pocos tramos rectos.</summary>
    internal static class PathRouter {

        /// <summary>Un tramo a menos de estos grados de la horizontal o la vertical se endereza a ella.</summary>
        private const double SnapDegrees = 20;

        /// <summary>Esquinas como máximo, para que la flecha siga siendo sencilla.</summary>
        private const int MaxCorners = 4;

        /// <summary>Lo más que puede girar el recorrido en una esquina, con margen sobre el ángulo recto.</summary>
        private const double MaxTurnDegrees = 115;

        /// <summary>Lo más que puede girar en total en un mismo sentido: media vuelta (forma de U), con margen.</summary>
        private const double MaxTotalTurnDegrees = 200;

        private enum Dir { H, V, Diagonal }

        /// <param name="axisOnly">Todos los tramos horizontales o verticales, sin diagonales.</param>
        public static Point[] Route(IReadOnlyList<Point> raw, bool axisOnly) {
            Point start = raw[0], end = raw[raw.Count - 1];
            var pts = new List<Point> { start };
            foreach (Point p in raw.Skip(1)) {
                if (MathUtil.Distance(p, pts[pts.Count - 1]) >= 2) pts.Add(p);
            }
            if (pts[pts.Count - 1] != end) pts.Add(end);
            if (pts.Count < 2) return new[] { start, end };

            // La tolerancia crece con el tamaño del trazo: un temblor en un trazo grande no es una esquina
            double diag = MathUtil.Distance(new Point(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point(pts.Max(p => p.X), pts.Max(p => p.Y)));
            double eps = Math.Max(6, Math.Min(28, diag * 0.06));

            List<Point> v = Simplify(pts, eps);
            RemoveShortSegments(v, eps * 1.5);

            List<Dir> dirs = Reduce(v, axisOnly, eps);

            // Las diagonales solo valen como flecha de un solo tramo. Si el recorrido tiene esquinas, una diagonal
            // suele ser un desliz del ratón: si casi todo es esa diagonal, queda una diagonal de origen a punta;
            // si no, cada diagonal se endereza al eje más cercano y se une con el tramo de al lado.
            if (dirs.Count > 1 && dirs.Contains(Dir.Diagonal)) {
                double total = 0, longestDiagonal = 0;
                for (int k = 0; k < dirs.Count; k++) {
                    double length = MathUtil.Distance(v[k], v[k + 1]);
                    total += length;
                    if (dirs[k] == Dir.Diagonal) longestDiagonal = Math.Max(longestDiagonal, length);
                }
                if (longestDiagonal >= total * 0.75) {
                    v = new List<Point> { start, end };
                    dirs = Classify(v, axisOnly);
                } else {
                    dirs = Reduce(v, axisOnly: true, eps);
                }
            }

            // Coordenada fija de cada tramo recto: Y en los horizontales, X en los verticales.
            // El primero pasa por el origen y el último por la punta; los demás, por su punto medio.
            int last = dirs.Count - 1;
            var fixedCoord = new int[dirs.Count];
            for (int k = 0; k <= last; k++) {
                Point a = v[k], b = v[k + 1];
                fixedCoord[k] = dirs[k] switch {
                    Dir.H => k == 0 ? a.Y : k == last ? b.Y : (a.Y + b.Y) / 2,
                    Dir.V => k == 0 ? a.X : k == last ? b.X : (a.X + b.X) / 2,
                    _ => 0,
                };
            }

            var result = new List<Point>();
            for (int j = 0; j < v.Count; j++) {
                int x = v[j].X, y = v[j].Y;
                foreach (int k in new[] { j - 1, j }) {
                    if (k < 0 || k > last) continue;
                    if (dirs[k] == Dir.H) y = fixedCoord[k];
                    else if (dirs[k] == Dir.V) x = fixedCoord[k];
                }
                var p = new Point(x, y);
                if (result.Count == 0 || result[result.Count - 1] != p) result.Add(p);
            }
            RemoveCollinear(result);
            if (result.Count < 2) return new[] { start, end };
            return result.ToArray();
        }

        /// <summary>
        /// Clasifica los tramos, une los consecutivos que van en la misma dirección y limita la forma:
        /// como mucho MaxCorners esquinas, ninguna que gire más de un ángulo recto y no más de media vuelta
        /// en total en un mismo sentido (nada de bucles ni círculos).
        /// </summary>
        private static List<Dir> Reduce(List<Point> v, bool axisOnly, double eps) {
            while (true) {
                List<Dir> dirs = Classify(v, axisOnly);
                if (MergeOnce(v, dirs, eps)) continue;
                if (RemoveSharpTurn(v)) continue;
                if (v.Count > MaxCorners + 2) {
                    RemoveLeastSignificant(v);
                    continue;
                }
                return dirs;
            }
        }

        /// <summary>Ramer–Douglas–Peucker: se queda con los puntos que se separan más de <paramref name="eps"/> de la recta.</summary>
        private static List<Point> Simplify(List<Point> pts, double eps) {
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0) {
                (int a, int b) = stack.Pop();
                double max = 0;
                int index = -1;
                for (int i = a + 1; i < b; i++) {
                    double d = DistanceToSegment(pts[i], pts[a], pts[b]);
                    if (d > max) { max = d; index = i; }
                }
                if (index < 0 || max <= eps) continue;
                keep[index] = true;
                stack.Push((a, index));
                stack.Push((index, b));
            }
            return pts.Where((_, i) => keep[i]).ToList();
        }

        private static double DistanceToSegment(Point p, Point a, Point b) {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 == 0) return MathUtil.Distance(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            double px = a.X + t * dx - p.X, py = a.Y + t * dy - p.Y;
            return Math.Sqrt(px * px + py * py);
        }

        /// <summary>Quita los tramos muy cortos (titubeos al empezar, al terminar o en las esquinas).</summary>
        private static void RemoveShortSegments(List<Point> v, double minLength) {
            bool changed = true;
            while (changed && v.Count > 2) {
                changed = false;
                for (int i = 0; i < v.Count - 1 && v.Count > 2; i++) {
                    if (MathUtil.Distance(v[i], v[i + 1]) >= minLength) continue;
                    if (i == 0) v.RemoveAt(1);
                    else if (i + 1 == v.Count - 1) v.RemoveAt(i);
                    else {
                        v[i] = new Point((v[i].X + v[i + 1].X) / 2, (v[i].Y + v[i + 1].Y) / 2);
                        v.RemoveAt(i + 1);
                    }
                    changed = true;
                    break;
                }
            }
        }

        private static List<Dir> Classify(List<Point> v, bool axisOnly) {
            var dirs = new List<Dir>();
            for (int i = 0; i < v.Count - 1; i++) {
                int dx = Math.Abs(v[i + 1].X - v[i].X), dy = Math.Abs(v[i + 1].Y - v[i].Y);
                double angle = Math.Atan2(Math.Min(dx, dy), Math.Max(dx, dy)) * 180 / Math.PI;
                dirs.Add(axisOnly || angle <= SnapDegrees ? (dx >= dy ? Dir.H : Dir.V) : Dir.Diagonal);
            }
            return dirs;
        }

        /// <summary>
        /// Une dos tramos consecutivos en la misma dirección, o deja en una sola esquina una diagonal corta
        /// entre un tramo horizontal y uno vertical (lo que queda de una esquina dibujada en curva).
        /// </summary>
        private static bool MergeOnce(List<Point> v, List<Dir> dirs, double eps) {
            for (int i = 0; i < dirs.Count - 1; i++) {
                bool same = dirs[i] != Dir.Diagonal && dirs[i] == dirs[i + 1]
                    || dirs[i] == Dir.Diagonal && dirs[i + 1] == Dir.Diagonal && AngleBetween(v[i], v[i + 1], v[i + 2]) < 15;
                if (same) {
                    v.RemoveAt(i + 1);
                    return true;
                }
            }
            for (int i = 1; i < dirs.Count - 1; i++) {
                if (dirs[i] != Dir.Diagonal || dirs[i - 1] == Dir.Diagonal || dirs[i + 1] == Dir.Diagonal || dirs[i - 1] == dirs[i + 1]) continue;
                double length = MathUtil.Distance(v[i], v[i + 1]);
                bool shorterThanBoth = length < MathUtil.Distance(v[i - 1], v[i]) && length < MathUtil.Distance(v[i + 1], v[i + 2]);
                if (length >= eps * 3 && !shorterThanBoth) continue;
                v[i] = new Point((v[i].X + v[i + 1].X) / 2, (v[i].Y + v[i + 1].Y) / 2);
                v.RemoveAt(i + 1);
                return true;
            }
            return false;
        }

        /// <summary>Grados que gira el recorrido en <paramref name="b"/>.</summary>
        private static double AngleBetween(Point a, Point b, Point c) {
            double a1 = Math.Atan2(b.Y - a.Y, b.X - a.X), a2 = Math.Atan2(c.Y - b.Y, c.X - b.X);
            double d = Math.Abs(a2 - a1) * 180 / Math.PI;
            return d > 180 ? 360 - d : d;
        }

        /// <summary>
        /// Quita una esquina si el recorrido gira demasiado: una sola que pase de un ángulo recto (vueltas atrás, picos)
        /// o varias en el mismo sentido que sumen más de media vuelta (rectángulos cerrados, espirales, círculos).
        /// Las escaleras, que giran a un lado y al otro, se mantienen.
        /// </summary>
        private static bool RemoveSharpTurn(List<Point> v) {
            int worst = -1;
            double max = MaxTurnDegrees;
            for (int i = 1; i < v.Count - 1; i++) {
                double turn = AngleBetween(v[i - 1], v[i], v[i + 1]);
                if (turn > max) { max = turn; worst = i; }
            }
            if (worst >= 0) {
                v.RemoveAt(worst);
                return true;
            }

            double total = 0;
            for (int i = 1; i < v.Count - 1; i++) {
                total += SignedTurn(v[i - 1], v[i], v[i + 1]);
                if (Math.Abs(total) <= MaxTotalTurnDegrees) continue;
                // Se quita la menos marcada de las esquinas hasta aquí
                int best = 1;
                double min = double.MaxValue;
                for (int j = 1; j <= i; j++) {
                    double d = DistanceToSegment(v[j], v[j - 1], v[j + 1]);
                    if (d < min) { min = d; best = j; }
                }
                v.RemoveAt(best);
                return true;
            }
            return false;
        }

        /// <summary>Grados que gira el recorrido en <paramref name="b"/>: positivos en un sentido y negativos en el otro.</summary>
        private static double SignedTurn(Point a, Point b, Point c) {
            double d = (Math.Atan2(c.Y - b.Y, c.X - b.X) - Math.Atan2(b.Y - a.Y, b.X - a.X)) * 180 / Math.PI;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;
            return d;
        }

        /// <summary>Quita la esquina que menos se separa de la recta entre sus vecinas.</summary>
        private static void RemoveLeastSignificant(List<Point> v) {
            int best = 1;
            double min = double.MaxValue;
            for (int i = 1; i < v.Count - 1; i++) {
                double d = DistanceToSegment(v[i], v[i - 1], v[i + 1]);
                if (d < min) { min = d; best = i; }
            }
            v.RemoveAt(best);
        }

        /// <summary>Quita los puntos repetidos y los que quedan en medio de un tramo recto.</summary>
        public static void Clean(List<Point> pts) {
            for (int i = pts.Count - 1; i >= 1; i--) {
                if (pts[i] == pts[i - 1]) pts.RemoveAt(i);
            }
            RemoveCollinear(pts);
        }

        private static void RemoveCollinear(List<Point> pts) {
            for (int i = pts.Count - 2; i >= 1; i--) {
                Point a = pts[i - 1], b = pts[i], c = pts[i + 1];
                long cross = (long)(b.X - a.X) * (c.Y - b.Y) - (long)(b.Y - a.Y) * (c.X - b.X);
                long dot = (long)(b.X - a.X) * (c.X - b.X) + (long)(b.Y - a.Y) * (c.Y - b.Y);
                if (cross == 0 && dot >= 0) pts.RemoveAt(i);
            }
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
