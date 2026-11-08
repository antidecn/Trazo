using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using TrazoInstallerArt;

// Imágenes del asistente del instalador con el aspecto de Trazo (los mismos colores, fuentes y formas
// que la ventana de configuración y la barra de la captura).
//
//   dotnet run --project installer\art -- <carpeta de salida>
//
// Escribe en la carpeta de salida los BMP y TrazoArt.wxs (las entradas Binary que los meten en el .msi).
//
// Windows Installer no deja cambiar el color de sus controles, así que los botones, las opciones y las casillas
// del asistente son botones con imagen. Una imagen de botón no se escala bien (se ve el marco nativo alrededor),
// así que cada imagen se genera a su tamaño exacto para cada escala de pantalla habitual (100 % a 200 %).
// En el instalador, la propiedad TRAZOUISCALE elige el juego que toca según TextHeight (ver Package.wxs).
//
// Los tamaños (en unidades de diálogo de Windows Installer) deben coincidir con los de los controles en Package.wxs.

if (args.Length != 1) {
    Console.Error.WriteLine("Uso: dotnet run --project installer\\art -- <carpeta de salida>");
    return 1;
}
string outDir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outDir);

// assets\Trazo-simbolo.ai, subiendo desde la carpeta del proyecto
string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "assets", "Trazo-simbolo.ai"))) {
    root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new FileNotFoundException("No se encuentra assets\\Trazo-simbolo.ai");
}
using GraphicsPath symbol = Symbol.Read(Path.Combine(root, "assets", "Trazo-simbolo.ai"));

// Altura del texto del sistema (propiedad TextHeight de Windows Installer) en cada escala: 16 px al 100 %.
// Una unidad de diálogo mide TextHeight / 12 píxeles.
int[] scales = [16, 20, 24, 28, 32];

var art = new Art(symbol);
var assets = new List<(string Id, int W, int H, Action<Graphics, float> Draw)>();
void Add(string id, int w, int h, Action<Graphics, float> draw) => assets.Add((id, w, h, draw));

// ── Fondos ──
Add("Bg", 370, 270, (g, f) => art.Background(g, f, 370, 270, Art.FooterTop));
Add("BgSmall", 270, 100, (g, f) => art.Background(g, f, 270, 100, Art.SmallFooterTop));

// ── Logo (recuadro blanco redondeado con el símbolo, como el icono de la aplicación) ──
Add("Logo", 24, 24, (g, f) => art.Logo(g, f, 24, Art.Back));
Add("LogoBig", 48, 48, (g, f) => art.Logo(g, f, 48, Art.Back));

// ── Botones de la franja inferior (66 × 20) ──
foreach (var (id, text, primary) in new[] {
    ("Next", "Siguiente", true), ("Install", "Instalar", true), ("Update", "Actualizar", true),
    ("Repair", "Reparar", true), ("Remove", "Desinstalar", true), ("Finish", "Finalizar", true),
    ("Ok", "Aceptar", true), ("Yes", "Sí", true), ("Retry", "Reintentar", true),
    ("Back", "Atrás", false), ("Cancel", "Cancelar", false), ("No", "No", false),
    ("Ignore", "Omitir", false), ("Abort", "Anular", false), ("Exit", "Salir", false),
}) {
    Add("Btn" + id, 66, 20, (g, f) => art.Button(g, f, 66, 20, text, primary, Art.Footer));
}

// ── Botones sobre el contenido (76 × 18) ──
foreach (var (id, text) in new[] { ("Change", "Cambiar…"), ("Up", "Subir un nivel"), ("NewFolder", "Nueva carpeta") }) {
    Add("Btn" + id, 76, 18, (g, f) => art.Button(g, f, 76, 18, text, false, Art.Back));
}

// ── Tarjetas de opción (330 × 52): dos imágenes por opción, elegida y sin elegir ──
foreach (var (id, title, text) in new[] {
    ("User", "Solo para mí (recomendado)", "Se instala en tu carpeta de usuario. No necesita permisos de administrador."),
    ("Machine", "Para todos los usuarios de este equipo", "Se instala en Archivos de programa. Windows pedirá permisos de administrador."),
}) {
    Add("Opt" + id + "On", 330, 52, (g, f) => art.Option(g, f, 330, 52, title, text, selected: true));
    Add("Opt" + id + "Off", 330, 52, (g, f) => art.Option(g, f, 330, 52, title, text, selected: false));
}

// ── Tarjetas de acción (330 × 52): reparar o desinstalar ──
Add("ActRepair", 330, 52, (g, f) => art.Action(g, f, 330, 52, "Reparar", "Vuelve a copiar los archivos de Trazo por si falta alguno o está dañado."));
Add("ActRemove", 330, 52, (g, f) => art.Action(g, f, 330, 52, "Desinstalar", "Quita Trazo de este equipo."));

// ── Casillas: marcada y sin marcar. "Abrir Trazo" (120 × 16) es estrecha para no tapar el símbolo del fondo ──
foreach (var (id, text, w) in new[] { ("Updates", "Avisarme cuando haya una versión nueva", 300), ("Launch", "Abrir Trazo", 120) }) {
    Add("Chk" + id + "On", w, 16, (g, f) => art.CheckBox(g, f, w, 16, text, true));
    Add("Chk" + id + "Off", w, 16, (g, f) => art.CheckBox(g, f, w, 16, text, false));
}

var wxs = new StringBuilder();
wxs.AppendLine("<!-- Generado por installer\\art al compilar el instalador. No editar a mano. -->");
wxs.AppendLine("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">");
wxs.AppendLine("  <Fragment>");
wxs.AppendLine("    <UI Id=\"TrazoArt\" />");
foreach (var (id, w, h, draw) in assets) {
    foreach (int s in scales) {
        // Tamaño en píxeles igual que el que calcula Windows Installer para el control
        int pw = w * s / 12, ph = h * s / 12;
        using var bmp = new Bitmap(pw, ph);
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            draw(g, s / 16f);
        }
        string file = $"Tz{id}{s}.bmp";
        // Fondos y logo van en controles de imagen y admiten RLE8; los botones no
        bool isStatic = id.StartsWith("Bg") || id.StartsWith("Logo");
        Bmp.Save8(bmp, Path.Combine(outDir, file), rle: true);
        wxs.AppendLine($"    <Binary Id=\"Tz{id}{s}\" SourceFile=\"{Path.Combine(outDir, file)}\" />");
    }
}
wxs.AppendLine("  </Fragment>");
wxs.AppendLine("</Wix>");
File.WriteAllText(Path.Combine(outDir, "TrazoArt.wxs"), wxs.ToString(), new UTF8Encoding(false));

Console.WriteLine($"Imágenes del instalador: {assets.Count * scales.Length} en {outDir}");
return 0;

/// <summary>Dibujo de cada pieza. f = escala respecto al 100 % (1 px de diseño = f píxeles).</summary>
internal sealed class Art(GraphicsPath symbol) {

    // Colores de la ventana de configuración de Trazo (SettingsForm)
    public static readonly Color Back = Color.FromArgb(28, 28, 30);
    public static readonly Color Footer = Color.FromArgb(21, 21, 23);
    public static readonly Color Surface = Color.FromArgb(44, 44, 48);
    public static readonly Color Edge = Color.FromArgb(70, 70, 76);
    public static readonly Color Accent = Color.FromArgb(88, 88, 94);
    public static readonly Color AccentLine = Color.FromArgb(185, 185, 190);
    public static readonly Color Ink = Color.FromArgb(240, 240, 240);
    public static readonly Color SubText = Color.FromArgb(160, 160, 166);

    /// <summary>Inicio de la franja de botones (unidades de diálogo) en los diálogos grandes y pequeños.</summary>
    public const int FooterTop = 230, SmallFooterTop = 64;

    // Proporciones del logo de la aplicación (en un icono de 256 px: radio 46 y símbolo de 172 px de ancho)
    private const float LogoCorner = 46f / 256f, LogoSymbol = 172f / 256f;

    private static float Px(float units, float f) => units * 16f / 12f * f;

    private static Font UiFont(float points, float f, bool semibold = false) =>
        new(semibold ? "Segoe UI Semibold" : "Segoe UI", points * 96f / 72f * f, FontStyle.Regular, GraphicsUnit.Pixel);

    private static GraphicsPath Rounded(RectangleF r, float radius) {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static RectangleF Inner(Graphics g, float inset) {
        RectangleF b = g.VisibleClipBounds;
        return new RectangleF(inset, inset, b.Width - 1 - inset * 2, b.Height - 1 - inset * 2);
    }

    private static void Text(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment h, StringAlignment v) {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) {
            Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
        };
        g.DrawString(text, font, brush, rect, format);
    }

    public void Background(Graphics g, float f, int w, int h, int footerTop) {
        g.Clear(Back);
        using var footer = new SolidBrush(Footer);
        g.FillRectangle(footer, 0, Px(footerTop, f), Px(w, f) + 1, Px(h, f));
        using var line = new Pen(Color.FromArgb(38, 38, 42), Math.Max(1, (int)f));
        g.DrawLine(line, 0, Px(footerTop, f), Px(w, f), Px(footerTop, f));
    }

    public void Logo(Graphics g, float f, int units, Color back) {
        g.Clear(back);
        float size = g.VisibleClipBounds.Width;
        using (GraphicsPath square = Rounded(new RectangleF(0, 0, size - 0.5f, size - 0.5f), size * LogoCorner)) g.FillPath(Brushes.White, square);
        Symbol.Fill(g, symbol, Color.Black, size * LogoSymbol, size / 2f, size / 2f);
    }

    /// <summary>Como los botones de la configuración: el principal gris claro y los demás gris oscuro con borde.</summary>
    public void Button(Graphics g, float f, int w, int h, string text, bool primary, Color back) {
        g.Clear(back);
        using (GraphicsPath path = Rounded(Inner(g, 0.5f * f), 6 * f)) {
            using var brush = new SolidBrush(primary ? Accent : Surface);
            g.FillPath(brush, path);
            if (!primary) {
                using var pen = new Pen(Edge, f);
                g.DrawPath(pen, path);
            }
        }
        using Font font = UiFont(9, f);
        Text(g, text, font, primary ? Color.White : Ink, g.VisibleClipBounds, StringAlignment.Center, StringAlignment.Center);
    }

    /// <summary>Como las opciones de la configuración: elegida, con fondo más claro y borde gris claro.</summary>
    public void Option(Graphics g, float f, int w, int h, string title, string text, bool selected) {
        g.Clear(Back);
        using (GraphicsPath path = Rounded(Inner(g, 0.75f * f), 8 * f)) {
            using var brush = new SolidBrush(selected ? Blend(Back, Color.White, 55) : Surface);
            g.FillPath(brush, path);
            using var pen = new Pen(selected ? AccentLine : Edge, (selected ? 1.5f : 1f) * f);
            g.DrawPath(pen, path);
        }
        // Indicador redondo a la izquierda
        float d = 16 * f, cx = 22 * f, cy = Px(h, f) / 2f;
        using (var ring = new Pen(selected ? AccentLine : Color.FromArgb(130, 130, 136), 1.5f * f)) g.DrawEllipse(ring, cx - d / 2, cy - d / 2, d, d);
        if (selected) {
            float dot = 8 * f;
            g.FillEllipse(Brushes.White, cx - dot / 2, cy - dot / 2, dot, dot);
        }
        TitleAndText(g, f, h, 42 * f, title, text, 16 * f);
    }

    /// <summary>Tarjeta que se pulsa para elegir una acción, con una flecha a la derecha.</summary>
    public void Action(Graphics g, float f, int w, int h, string title, string text) {
        g.Clear(Back);
        using (GraphicsPath path = Rounded(Inner(g, 0.5f * f), 8 * f)) {
            using var brush = new SolidBrush(Surface);
            g.FillPath(brush, path);
            using var pen = new Pen(Edge, f);
            g.DrawPath(pen, path);
        }
        float cx = Px(w, f) - 20 * f, cy = Px(h, f) / 2f, s = 4.5f * f;
        using (var chevron = new Pen(SubText, 1.6f * f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
            g.DrawLines(chevron, [new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s)]);
        }
        TitleAndText(g, f, h, 16 * f, title, text, 36 * f);
    }

    private static void TitleAndText(Graphics g, float f, int h, float x, string title, string text, float right) {
        float width = g.VisibleClipBounds.Width - x - right;
        using Font titleFont = UiFont(9.5f, f, semibold: true);
        using Font textFont = UiFont(9, f);
        // Título arriba y descripción debajo, en una o dos líneas; el bloque va centrado en vertical
        using var format = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisWord };
        float titleHeight = titleFont.GetHeight(g), lineHeight = textFont.GetHeight(g);
        SizeF textSize = g.MeasureString(text, textFont, new SizeF(width, lineHeight * 2.2f), format);
        float gap = 3 * f, top = (Px(h, f) - (titleHeight + gap + textSize.Height)) / 2f;
        Text(g, title, titleFont, Ink, new RectangleF(x, top, width, titleHeight + f), StringAlignment.Near, StringAlignment.Near);
        using var brush = new SolidBrush(SubText);
        g.DrawString(text, textFont, brush, new RectangleF(x, top + titleHeight + gap, width, lineHeight * 2.2f), format);
    }

    /// <summary>Como las casillas de la configuración: gris claro con una marca blanca cuando está activada.</summary>
    public void CheckBox(Graphics g, float f, int w, int h, string text, bool isChecked) {
        g.Clear(Back);
        float size = 18 * f;
        var box = new RectangleF(0.5f * f, (Px(h, f) - size) / 2f, size - f, size - f);
        using (GraphicsPath path = Rounded(box, 4 * f)) {
            using var brush = new SolidBrush(isChecked ? Accent : Surface);
            g.FillPath(brush, path);
            if (!isChecked) {
                using var pen = new Pen(Edge, f);
                g.DrawPath(pen, path);
            }
        }
        if (isChecked) {
            using var check = new Pen(Color.White, 2f * f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(check, [
                new PointF(box.X + 4.5f * f, box.Y + 9 * f),
                new PointF(box.X + 7.5f * f, box.Y + 12 * f),
                new PointF(box.X + 13 * f, box.Y + 5.5f * f),
            ]);
        }
        using Font font = UiFont(9, f);
        float x = size + 10 * f;
        Text(g, text, font, Ink, new RectangleF(x, 0, g.VisibleClipBounds.Width - x, Px(h, f)), StringAlignment.Near, StringAlignment.Center);
    }

    private static Color Blend(Color back, Color over, int alpha) => Color.FromArgb(
        back.R + (over.R - back.R) * alpha / 255,
        back.G + (over.G - back.G) * alpha / 255,
        back.B + (over.B - back.B) * alpha / 255);
}
