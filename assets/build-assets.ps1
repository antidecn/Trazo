# Genera, a partir del símbolo vectorial assets\Trazo-simbolo.ai, los iconos de la aplicación
# y las imágenes del instalador:
#   Trazo.ico                (ejecutable, ventanas, menú Inicio, "Aplicaciones instaladas"): recuadro blanco con el símbolo en negro
#   TrazoTray.ico            (bandeja del sistema, barra de tareas oscura): símbolo en blanco sobre transparente
#   TrazoTrayBlack.ico       (bandeja del sistema, barra de tareas clara): símbolo en negro sobre transparente
#   installer\banner.bmp     (franja superior del asistente, 493x58)
#   installer\dialog.bmp     (pantallas de bienvenida y final del asistente, 493x312)
#   docs\logo.png            (logo para el README, 128x128)
#
# Uso (Windows PowerShell):  powershell -ExecutionPolicy Bypass -File assets\build-assets.ps1
#
# Trazo-simbolo.ai: guardado desde Illustrator con "Crear archivo compatible con PDF" (la opción por defecto).
# Se usan las formas rellenas de la mesa de trabajo, en cualquier color; para trazos, Objeto > Expandir antes de guardar.
# Cada tamaño se dibuja desde el vector, así los iconos pequeños quedan nítidos.

param(
    # Panel izquierdo de las pantallas de bienvenida y final del instalador
    [string]$BrandColor = "#000000"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

public static class TrazoAssets {

    // Proporciones del logo (en un icono de 256 px: radio 46 y símbolo de 172 px de ancho)
    const float CornerRadius = 46f / 256f;
    const float SymbolWidth = 172f / 256f;

    // --- Lectura del .ai: la parte PDF de la página (trazados rellenos m/l/c/h con transformaciones cm) ---

    public static GraphicsPath ReadSymbol(string path) {
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.GetEncoding(28591).GetString(bytes);
        if (!text.StartsWith("%PDF")) throw new Exception("El .ai no es compatible con PDF: guárdalo con 'Crear archivo compatible con PDF'.");

        float pageHeight = float.Parse(Regex.Match(text, @"/MediaBox\s*\[\s*[-\d.]+\s+[-\d.]+\s+[-\d.]+\s+([-\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var path2d = new GraphicsPath(FillMode.Winding);
        foreach (Match contents in Regex.Matches(text, @"/Contents\s+(\d+) 0 R")) {
            AddContent(path2d, ReadStream(bytes, text, contents.Groups[1].Value), pageHeight);
        }
        if (path2d.PointCount == 0) throw new Exception("No se han encontrado formas rellenas en el .ai.");
        return path2d;
    }

    static string ReadStream(byte[] bytes, string text, string id) {
        Match obj = Regex.Match(text, @"(?<!\d)" + id + @" 0 obj\s*<<(.*?)>>\s*stream\r?\n", RegexOptions.Singleline);
        string dict = obj.Groups[1].Value;
        int length = int.Parse(Regex.Match(dict, @"/Length (\d+)").Groups[1].Value);
        int start = obj.Index + obj.Length;
        if (!dict.Contains("/FlateDecode")) return Encoding.GetEncoding(28591).GetString(bytes, start, length);
        // zlib: se saltan los 2 bytes de cabecera para usar DeflateStream
        using (var ms = new MemoryStream(bytes, start + 2, length - 2))
        using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
        using (var sr = new StreamReader(ds, Encoding.GetEncoding(28591))) return sr.ReadToEnd();
    }

    static void AddContent(GraphicsPath result, string content, float pageHeight) {
        var stack = new Stack<Matrix>();
        var ctm = new Matrix();
        var nums = new List<float>();
        var shape = new GraphicsPath(FillMode.Winding);
        PointF cur = PointF.Empty, start = PointF.Empty;
        // PDF tiene el origen abajo; aquí, arriba
        Func<float, float, PointF> map = (x, y) => {
            var p = new[] { new PointF(x, y) };
            ctm.TransformPoints(p);
            return new PointF(p[0].X, pageHeight - p[0].Y);
        };
        // Cadenas, nombres y números; los operadores son palabras sueltas
        foreach (Match tok in Regex.Matches(content, @"\((?:\\.|[^\\)])*\)|/[^\s/\[\]<>()]+|[-+]?(?:\d+\.?\d*|\.\d+)|[A-Za-z*'""]+")) {
            string s = tok.Value;
            float v;
            if (char.IsDigit(s[0]) || s[0] == '-' || s[0] == '+' || s[0] == '.') {
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) nums.Add(v);
                continue;
            }
            switch (s) {
                case "q": stack.Push(ctm.Clone()); break;
                case "Q": ctm = stack.Count > 0 ? stack.Pop() : new Matrix(); break;
                case "cm": {
                    var m = new Matrix(nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]);
                    m.Multiply(ctm, MatrixOrder.Append);
                    ctm = m;
                    break;
                }
                case "m": shape.StartFigure(); cur = start = new PointF(nums[0], nums[1]); break;
                case "l": {
                    var p = new PointF(nums[0], nums[1]);
                    shape.AddLine(map(cur.X, cur.Y), map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "c": {
                    var p = new PointF(nums[4], nums[5]);
                    shape.AddBezier(map(cur.X, cur.Y), map(nums[0], nums[1]), map(nums[2], nums[3]), map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "v": {
                    var p = new PointF(nums[2], nums[3]);
                    shape.AddBezier(map(cur.X, cur.Y), map(cur.X, cur.Y), map(nums[0], nums[1]), map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "y": {
                    var p = new PointF(nums[2], nums[3]);
                    shape.AddBezier(map(cur.X, cur.Y), map(nums[0], nums[1]), map(p.X, p.Y), map(p.X, p.Y));
                    cur = p;
                    break;
                }
                case "h": shape.CloseFigure(); cur = start; break;
                case "re": {
                    // Rectángulos de recorte ("re W n") se descartan con "n"; los rellenos se añaden con "f"
                    var r = new[] { map(nums[0], nums[1]), map(nums[0] + nums[2], nums[1]), map(nums[0] + nums[2], nums[1] + nums[3]), map(nums[0], nums[1] + nums[3]) };
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
            }
            nums.Clear();
        }
    }

    // --- Dibujo ---

    static Graphics Prepare(Bitmap bmp) {
        Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        return g;
    }

    // Símbolo escalado a un ancho dado y centrado en (cx, cy)
    static void FillSymbol(Graphics g, GraphicsPath symbol, Color color, float width, float cx, float cy) {
        RectangleF b = symbol.GetBounds();
        float scale = width / Math.Max(b.Width, b.Height);
        using (var p = (GraphicsPath)symbol.Clone())
        using (var m = new Matrix())
        using (var brush = new SolidBrush(color)) {
            m.Translate(cx, cy);
            m.Scale(scale, scale);
            m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
            p.Transform(m);
            g.FillPath(brush, p);
        }
    }

    static GraphicsPath RoundedSquare(float size, float radius) {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(0, 0, d, d, 180, 90);
        p.AddArc(size - d, 0, d, d, 270, 90);
        p.AddArc(size - d, size - d, d, d, 0, 90);
        p.AddArc(0, size - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Logo: recuadro blanco redondeado con el símbolo en negro
    public static Bitmap Logo(GraphicsPath symbol, int size) {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Prepare(bmp))
        using (GraphicsPath square = RoundedSquare(size, size * CornerRadius)) {
            g.FillPath(Brushes.White, square);
            FillSymbol(g, symbol, Color.Black, size * SymbolWidth, size / 2f, size / 2f);
        }
        return bmp;
    }

    // Bandeja: solo el símbolo, con margen para que ocupe lo mismo que los iconos de Windows (unos 12 px de 16)
    const float TraySymbolSize = 0.75f;

    public static Bitmap Symbol(GraphicsPath symbol, int size, Color color) {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Prepare(bmp)) {
            FillSymbol(g, symbol, color, (float)Math.Round(size * TraySymbolSize), size / 2f, size / 2f);
        }
        return bmp;
    }

    // --- Archivos ---

    // ICO estándar: mapas de bits de 32 bits para los tamaños pequeños y PNG solo para 256 px
    public static void WriteIco(string path, Bitmap[] images) {
        var data = new List<byte[]>();
        foreach (Bitmap b in images) data.Add(b.Width >= 256 ? ToPng(b) : ToDib(b));
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs)) {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Length);
            int offset = 6 + 16 * images.Length;
            for (int i = 0; i < images.Length; i++) {
                w.Write((byte)(images[i].Width % 256)); w.Write((byte)(images[i].Height % 256));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(data[i].Length); w.Write(offset);
                offset += data[i].Length;
            }
            foreach (byte[] d in data) w.Write(d);
        }
        foreach (Bitmap b in images) b.Dispose();
    }

    private static byte[] ToPng(Bitmap b) {
        using (var ms = new MemoryStream()) {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // BITMAPINFOHEADER + píxeles BGRA de abajo arriba + máscara AND (vacía: manda el canal alfa)
    private static byte[] ToDib(Bitmap b) {
        int w = b.Width, h = b.Height;
        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms)) {
            bw.Write(40); bw.Write(w); bw.Write(h * 2);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--) {
                for (int x = 0; x < w; x++) {
                    Color c = b.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            }
            bw.Write(new byte[maskStride * h]);
            return ms.ToArray();
        }
    }

    // Pantallas de bienvenida y final: panel izquierdo de color con el logo y el nombre
    public static void WriteDialog(string path, GraphicsPath symbol, Color brand) {
        using (var bmp = new Bitmap(493, 312, PixelFormat.Format24bppRgb))
        using (var g = Graphics.FromImage(bmp))
        using (Bitmap logo = Logo(symbol, 88)) {
            g.Clear(Color.White);
            using (var b = new SolidBrush(brand)) g.FillRectangle(b, 0, 0, 164, 312);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawImage(logo, (164 - 88) / 2, 78, 88, 88);
            using (var font = new Font("Segoe UI Semibold", 22f, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center }) {
                g.DrawString("Trazo", font, Brushes.White, new RectangleF(0, 178, 164, 32), fmt);
            }
            bmp.Save(path, ImageFormat.Bmp);
        }
    }

    // Franja superior: fondo blanco con el símbolo a la derecha (el texto del asistente va a la izquierda)
    public static void WriteBanner(string path, GraphicsPath symbol) {
        using (var bmp = new Bitmap(493, 58, PixelFormat.Format24bppRgb))
        using (Graphics g = Prepare(bmp)) {
            g.Clear(Color.White);
            FillSymbol(g, symbol, Color.Black, 42 * SymbolWidth, 493 - 10 - 21, 29);
            bmp.Save(path, ImageFormat.Bmp);
        }
    }
}
"@

$brand = [System.Drawing.ColorTranslator]::FromHtml($BrandColor)
$symbol = [TrazoAssets]::ReadSymbol((Join-Path $PSScriptRoot "Trazo-simbolo.ai"))

$appSizes = [int[]](16, 20, 24, 32, 40, 48, 64, 256)
# La bandeja usa el tamaño de icono pequeño: 16 px al 100 % hasta 32 px al 200 %
$traySizes = [int[]](16, 20, 24, 32, 40, 48)

[TrazoAssets]::WriteIco((Join-Path $root "Trazo.ico"), [System.Drawing.Bitmap[]]($appSizes | ForEach-Object { [TrazoAssets]::Logo($symbol, $_) }))
[TrazoAssets]::WriteIco((Join-Path $root "TrazoTray.ico"), [System.Drawing.Bitmap[]]($traySizes | ForEach-Object { [TrazoAssets]::Symbol($symbol, $_, [System.Drawing.Color]::White) }))
[TrazoAssets]::WriteIco((Join-Path $root "TrazoTrayBlack.ico"), [System.Drawing.Bitmap[]]($traySizes | ForEach-Object { [TrazoAssets]::Symbol($symbol, $_, [System.Drawing.Color]::Black) }))
[TrazoAssets]::WriteDialog((Join-Path $root "installer\dialog.bmp"), $symbol, $brand)
[TrazoAssets]::WriteBanner((Join-Path $root "installer\banner.bmp"), $symbol)

$docs = Join-Path $root "docs"
New-Item -ItemType Directory -Force $docs | Out-Null
$readmeLogo = [TrazoAssets]::Logo($symbol, 128)
$readmeLogo.Save((Join-Path $docs "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$readmeLogo.Dispose()

$symbol.Dispose()
Write-Host "Generados: Trazo.ico, TrazoTray.ico, TrazoTrayBlack.ico, installer\dialog.bmp, installer\banner.bmp, docs\logo.png"
