using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace TrazoInstallerArt;

/// <summary>
/// Lee el símbolo vectorial de Trazo de assets\Trazo-simbolo.ai (la parte PDF de la página:
/// trazados rellenos m/l/c/h con transformaciones cm). Es el mismo lector con el que se generan los iconos
/// de la aplicación, así el logo del instalador sale de la misma fuente.
/// </summary>
internal static class Symbol {

    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static GraphicsPath Read(string path) {
        byte[] bytes = File.ReadAllBytes(path);
        string text = Latin1.GetString(bytes);
        if (!text.StartsWith("%PDF")) throw new InvalidDataException("El .ai no es compatible con PDF: guárdalo con 'Crear archivo compatible con PDF'.");

        float pageHeight = float.Parse(Regex.Match(text, @"/MediaBox\s*\[\s*[-\d.]+\s+[-\d.]+\s+[-\d.]+\s+([-\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);

        // Objetos de formulario (XObject /Form) por nombre: algunos programas dibujan ahí y la página solo los invoca con "/x0 Do"
        var xobjects = new Dictionary<string, string>();
        foreach (Match d in Regex.Matches(text, @"/XObject\s*<<(.*?)>>", RegexOptions.Singleline)) {
            foreach (Match e in Regex.Matches(d.Groups[1].Value, @"/([^\s/\[\]<>()]+)\s+(\d+) 0 R")) xobjects[e.Groups[1].Value] = e.Groups[2].Value;
        }
        (string Content, Matrix Matrix)? ReadForm(string name) {
            if (!xobjects.TryGetValue(name, out string? id)) return null;
            string dict = Regex.Match(text, @"(?<!\d)" + id + @" 0 obj\s*<<(.*?)>>\s*stream", RegexOptions.Singleline).Groups[1].Value;
            if (!dict.Contains("/Subtype/Form") && !dict.Contains("/Subtype /Form")) return null;
            var matrix = new Matrix();
            Match mt = Regex.Match(dict, @"/Matrix\s*\[([^\]]*)\]");
            if (mt.Success) {
                float[] v = mt.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                matrix = new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]);
            }
            return (ReadStream(bytes, text, id), matrix);
        }

        var result = new GraphicsPath(FillMode.Winding);
        foreach (Match contents in Regex.Matches(text, @"/Contents\s+(\d+) 0 R")) {
            AddContent(result, ReadStream(bytes, text, contents.Groups[1].Value), pageHeight, new Matrix(), ReadForm, 0);
        }
        if (result.PointCount == 0) throw new InvalidDataException("No se han encontrado formas rellenas en el .ai.");
        return result;
    }

    private static string ReadStream(byte[] bytes, string text, string id) {
        Match obj = Regex.Match(text, @"(?<!\d)" + id + @" 0 obj\s*<<(.*?)>>\s*stream\r?\n", RegexOptions.Singleline);
        string dict = obj.Groups[1].Value;
        int length = int.Parse(Regex.Match(dict, @"/Length (\d+)").Groups[1].Value);
        int start = obj.Index + obj.Length;
        if (!dict.Contains("/FlateDecode")) return Latin1.GetString(bytes, start, length);
        using var zs = new ZLibStream(new MemoryStream(bytes, start, length), CompressionMode.Decompress);
        using var sr = new StreamReader(zs, Latin1);
        return sr.ReadToEnd();
    }

    private static void AddContent(GraphicsPath result, string content, float pageHeight, Matrix initial,
                                   Func<string, (string Content, Matrix Matrix)?> readForm, int depth) {
        var stack = new Stack<Matrix>();
        Matrix ctm = initial;
        string? lastName = null;
        var nums = new List<float>();
        var shape = new GraphicsPath(FillMode.Winding);
        PointF cur = PointF.Empty, start = PointF.Empty;
        // PDF tiene el origen abajo; aquí, arriba
        PointF Map(float x, float y) {
            var p = new[] { new PointF(x, y) };
            ctm.TransformPoints(p);
            return new PointF(p[0].X, pageHeight - p[0].Y);
        }
        // Cadenas, nombres y números; los operadores son palabras sueltas
        foreach (Match tok in Regex.Matches(content, @"\((?:\\.|[^\\)])*\)|/[^\s/\[\]<>()]+|[-+]?(?:\d+\.?\d*|\.\d+)|[A-Za-z*'""]+")) {
            string s = tok.Value;
            if (char.IsDigit(s[0]) || s[0] is '-' or '+' or '.') {
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) nums.Add(v);
                continue;
            }
            if (s[0] == '/') { lastName = s[1..]; continue; }
            switch (s) {
                case "q": stack.Push(ctm.Clone()); break;
                case "Q": ctm = stack.Count > 0 ? stack.Pop() : initial.Clone(); break;
                case "cm": {
                    var m = new Matrix(nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]);
                    m.Multiply(ctm, MatrixOrder.Append);
                    ctm = m;
                    break;
                }
                case "m": shape.StartFigure(); cur = start = new PointF(nums[0], nums[1]); break;
                case "l": {
                    var p = new PointF(nums[0], nums[1]);
                    shape.AddLine(Map(cur.X, cur.Y), Map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "c": {
                    var p = new PointF(nums[4], nums[5]);
                    shape.AddBezier(Map(cur.X, cur.Y), Map(nums[0], nums[1]), Map(nums[2], nums[3]), Map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "v": {
                    var p = new PointF(nums[2], nums[3]);
                    shape.AddBezier(Map(cur.X, cur.Y), Map(cur.X, cur.Y), Map(nums[0], nums[1]), Map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "y": {
                    var p = new PointF(nums[2], nums[3]);
                    shape.AddBezier(Map(cur.X, cur.Y), Map(nums[0], nums[1]), Map(p.X, p.Y), Map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "h": shape.CloseFigure(); cur = start; break;
                case "re": {
                    var r = new[] { Map(nums[0], nums[1]), Map(nums[0] + nums[2], nums[1]), Map(nums[0] + nums[2], nums[1] + nums[3]), Map(nums[0], nums[1] + nums[3]) };
                    shape.StartFigure(); shape.AddPolygon(r);
                    break;
                }
                case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                    shape.CloseAllFigures();
                    result.AddPath(shape, false);
                    shape = new GraphicsPath(FillMode.Winding);
                    break;
                case "n": case "S": case "s":
                    shape = new GraphicsPath(FillMode.Winding);
                    break;
                case "Do": {
                    var form = lastName != null && depth < 8 ? readForm(lastName) : null;
                    if (form is { } f) {
                        Matrix m = f.Matrix;
                        m.Multiply(ctm, MatrixOrder.Append);
                        AddContent(result, f.Content, pageHeight, m, readForm, depth + 1);
                    }
                    break;
                }
            }
            nums.Clear();
            lastName = null;
        }
    }

    /// <summary>Símbolo escalado a un ancho dado y centrado en (cx, cy).</summary>
    public static void Fill(Graphics g, GraphicsPath symbol, Color color, float width, float cx, float cy) {
        RectangleF b = symbol.GetBounds();
        float scale = width / Math.Max(b.Width, b.Height);
        using var p = (GraphicsPath)symbol.Clone();
        using var m = new Matrix();
        using var brush = new SolidBrush(color);
        m.Translate(cx, cy);
        m.Scale(scale, scale);
        m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
        p.Transform(m);
        g.FillPath(brush, p);
    }
}
