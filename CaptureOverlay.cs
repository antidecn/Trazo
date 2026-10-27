using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Trazo {

    /// <summary>
    /// Ventana sin bordes que cubre todos los monitores con la captura congelada.
    /// Primero se selecciona una zona y después se dibuja sobre ella.
    /// </summary>
    internal sealed class CaptureOverlay : Form {

        private enum Phase { Selecting, Editing }
        private enum Tool { Arrow, Line, Rectangle, Ellipse, Text, Pixelate, Picker }

        /// <summary>Lados de la zona que se arrastran al redimensionarla (dos a la vez en las esquinas).</summary>
        [Flags]
        private enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

        private const int WM_DPICHANGED = 0x02E0;
        private const int MagnifierSource = 15;   // píxeles capturados alrededor del cursor
        private const int MagnifierZoom = 8;
        private static readonly Color Accent = Color.FromArgb(0, 150, 255);
        /// <summary>Gris del borde de la barra de herramientas; también el del recuadro de selección.</summary>
        private static readonly Color EdgeGray = Color.FromArgb(85, 85, 90);
        /// <summary>Borde fino de la barra de herramientas y la lupa.</summary>
        private static readonly Color GlassEdge = Color.FromArgb(70, 255, 255, 255);
        private static readonly Color CardBack = Color.FromArgb(235, 28, 28, 30);
        private static readonly float[] Widths = { 2, 3, 5, 8 };

        // Fuentes habituales de Windows que se ofrecen directamente; el resto, con "Otra fuente…"
        private static readonly string[] CommonFonts = {
            "Arial", "Segoe UI", "Calibri", "Verdana", "Tahoma", "Trebuchet MS",
            "Times New Roman", "Georgia", "Consolas", "Courier New", "Comic Sans MS", "Impact",
        };
        private static readonly float[] TextSizes = { 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 72, 96 };
        private const float MinTextSize = 6, MaxTextSize = 300;

        private readonly Bitmap screenshot;
        private readonly Bitmap dimmed;
        private readonly Rectangle virtualBounds;
        private readonly float scale;
        private readonly CrossCursor crossCursor;

        private Phase phase = Phase.Selecting;
        private Rectangle selection;
        private Point? selectStart;
        private Edge resizing;
        private Rectangle resizeOrigin;
        private Point resizeStart;
        private const int MinSelection = 8;

        private Tool tool = Tool.Arrow;
        private readonly List<Shape> shapes = new();
        private readonly Stack<List<Shape>> undoStack = new();
        private Shape? drawing;
        private Shape? selected;
        private bool moving;
        private bool movedSinceDown;
        private Point dragLast;
        // Redimensionado del elemento seleccionado con sus tiradores
        private Edge shapeResize;
        private Rectangle shapeOrigin;
        // Ctrl+rueda: giro acumulado y elemento de la última racha, para deshacerla de una vez
        private int wheelDelta;
        private Shape? wheelShape;
        private int wheelUndoDepth = -1;

        private readonly AppSettings settings;
        private Color strokeColor;
        private Color? fillColor;
        private float strokeWidth;
        private string textFont;
        private float textSize;

        // El texto se escribe en un TextBox invisible (fuera de la vista) y se pinta en directo sobre la captura
        private TextBox? textEditor;
        private TextShape? editingText;
        private bool editingExisting;
        private bool caretVisible;
        private readonly System.Windows.Forms.Timer caretTimer = new() { Interval = Math.Max(300, SystemInformation.CaretBlinkTime) };

        private Point mouse = new(-1000, -1000);
        private string? status;
        private Color? statusSwatch;
        private Rectangle statusRect;
        private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 2500 };
        private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 10 };

        private string? ocrText;
        private Rectangle ocrRect;
        private bool ocrRunning;

        private readonly ToolStrip toolbar;
        private readonly Dictionary<Tool, ToolStripButton> toolButtons = new();
        private ToolStripButton strokeButton = null!;
        private ToolStripDropDownButton fillButton = null!;
        private ToolStripDropDownButton widthButton = null!;
        // Solo visibles con la herramienta Texto o un texto seleccionado
        private ToolStripSeparator textSeparator = null!;
        private ToolStripDropDownButton fontButton = null!;
        private ToolStripDropDownButton sizeButton = null!;
        private Bitmap? toolbarBackdrop;
        private Bitmap? magnifierCard;
        private readonly List<Font> previewFonts = new();
        private readonly int iconSize;
        private readonly Font uiFont;
        private readonly Font labelFont;
        private readonly Font cardTitleFont;
        private readonly Font cardBodyFont;
        private readonly Font cardFootFont;

        public CaptureOverlay(Bitmap screenshot, Rectangle virtualBounds, AppSettings settings) {
            this.screenshot = screenshot;
            this.virtualBounds = virtualBounds;
            this.settings = settings;
            strokeColor = settings.StrokeColorValue;
            fillColor = settings.FillColorValue;
            strokeWidth = Widths.Contains(settings.StrokeWidth) ? settings.StrokeWidth : 3;
            textFont = string.IsNullOrWhiteSpace(settings.TextFont) ? TextShape.DefaultFontFamily : settings.TextFont;
            textSize = MathUtil.Clamp(settings.TextSize, MinTextSize, MaxTextSize);
            dimmed = CreateDimmed(screenshot);

            scale = DeviceDpi / 96f;
            iconSize = (int)Math.Round(20 * scale);
            uiFont = new Font("Segoe UI", 9f);
            labelFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            cardTitleFont = new Font("Segoe UI Semibold", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            cardBodyFont = new Font("Segoe UI", 13.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            cardFootFont = new Font("Segoe UI", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            ShowInTaskbar = false;
            TopMost = true;
            Opacity = 0;
            KeyPreview = true;
            crossCursor = new CrossCursor(scale);
            Cursor = crossCursor.Cursor;
            Text = "Trazo";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            toolbar = BuildToolbar();
            Controls.Add(toolbar);
            // Se abre con la última herramienta de dibujo usada
            SetTool(Enum.TryParse(settings.LastTool, out Tool last) && IsDrawingTool(last) ? last : Tool.Arrow);

            statusTimer.Tick += (_, _) => ShowStatus(null);
            caretTimer.Tick += (_, _) => {
                caretVisible = !caretVisible;
                InvalidateEditingText();
            };
        }

        // ───────────────────────────── Ciclo de vida ─────────────────────────────

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            Bounds = virtualBounds;
            Activate();
            FadeIn();
        }

        private const int FadeInMs = 300;

        /// <summary>
        /// La pantalla se va oscureciendo al abrir. Como la captura coincide con lo que ya se ve,
        /// basta con subir la opacidad de la ventana.
        /// </summary>
        private void FadeIn() {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            fadeTimer.Tick += (_, _) => {
                double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / FadeInMs);
                // Al llegar a 1 WinForms quita el estilo de ventana con capas y se vuelve a pintar sin coste extra
                Opacity = 1 - Math.Pow(1 - t, 3);
                if (t >= 1) fadeTimer.Stop();
            };
            fadeTimer.Start();
        }

        protected override void WndProc(ref Message m) {
            // La ventana abarca monitores con distinto escalado: Windows no debe redimensionarla
            if (m.Msg == WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                statusTimer.Dispose();
                caretTimer.Dispose();
                fadeTimer.Dispose();
                screenshot.Dispose();
                dimmed.Dispose();
                toolbarBackdrop?.Dispose();
                magnifierCard?.Dispose();
                foreach (Font f in previewFonts) f.Dispose();
                foreach (ToolStripItem item in toolbar.Items) {
                    item.Image?.Dispose();
                    if (item is ToolStripDropDownItem dd) {
                        foreach (ToolStripItem sub in dd.DropDownItems) sub.Image?.Dispose();
                    }
                }
                uiFont.Dispose();
                labelFont.Dispose();
                cardTitleFont.Dispose();
                cardBodyFont.Dispose();
                cardFootFont.Dispose();
            }
            base.Dispose(disposing);
            // Después de cerrar la ventana, cuando ya no se está mostrando
            if (disposing) crossCursor.Dispose();
        }

        private static Bitmap CreateDimmed(Bitmap source) {
            var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImageUnscaled(source, 0, 0);
            using var shade = new SolidBrush(Color.FromArgb(175, 0, 0, 0));
            g.FillRectangle(shade, 0, 0, bmp.Width, bmp.Height);
            return bmp;
        }

        // ───────────────────────────── Barra de herramientas ─────────────────────────────

        private ToolStrip BuildToolbar() {
            int pad = (int)Math.Round(6 * scale);
            var strip = new ToolStrip {
                Dock = DockStyle.None,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = true,
                CanOverflow = false,
                // Fondo transparente: el renderizador pinta el efecto cristal y las esquinas redondeadas
                BackColor = Color.Transparent,
                Renderer = new GlassToolbarRenderer(this),
                Font = uiFont,
                ImageScalingSize = new Size(iconSize, iconSize),
                Padding = new Padding(pad + 2, pad, pad, pad),
                Visible = false,
                Cursor = Cursors.Default,
                ShowItemToolTips = true,
            };

            ToolStripButton IconButton(IconKind kind, string tip, Action onClick) {
                var b = new ToolStripButton {
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    Image = Icons.Create(kind, iconSize, Icons.LightInk),
                    ToolTipText = tip,
                    AutoToolTip = false,
                    Padding = new Padding((int)Math.Round(4 * scale)),
                    Margin = new Padding(1, 0, 1, 0),
                };
                b.Click += (_, _) => onClick();
                strip.Items.Add(b);
                return b;
            }

            ToolStripDropDownButton TextDropDown(string tip) {
                var b = new ToolStripDropDownButton {
                    DisplayStyle = ToolStripItemDisplayStyle.Text,
                    ToolTipText = tip,
                    AutoToolTip = false,
                    Padding = new Padding((int)Math.Round(4 * scale), 0, (int)Math.Round(2 * scale), 0),
                    Margin = new Padding(1, 0, 1, 0),
                };
                strip.Items.Add(b);
                return b;
            }

            void AddTool(Tool t, IconKind icon, string tip) => toolButtons[t] = IconButton(icon, tip, () => SetTool(t));

            AddTool(Tool.Rectangle, IconKind.Rectangle, "Rectángulo (R)\nMayús: cuadrado");
            AddTool(Tool.Ellipse, IconKind.Ellipse, "Elipse (E)\nMayús: círculo");
            AddTool(Tool.Arrow, IconKind.Arrow, "Flecha (A)\nMayús: horizontal / vertical");
            AddTool(Tool.Line, IconKind.Line, "Línea (L)\nMayús: horizontal / vertical");
            AddTool(Tool.Text, IconKind.Text, "Texto (T)\nIntro: aceptar · Mayús+Intro: nueva línea\nDoble clic en un texto seleccionado para editarlo");
            strip.Items.Add(new ToolStripSeparator());

            ToolStripDropDownButton DropDown(string tip) {
                var b = new ToolStripDropDownButton {
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    ToolTipText = tip,
                    AutoToolTip = false,
                    Padding = new Padding((int)Math.Round(3 * scale)),
                    Margin = new Padding(1, 0, 1, 0),
                };
                strip.Items.Add(b);
                return b;
            }

            strokeButton = new ToolStripButton {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = "Color del borde, flecha y texto",
                AutoToolTip = false,
                Padding = new Padding((int)Math.Round(4 * scale)),
                Margin = new Padding(1, 0, 1, 0),
            };
            strokeButton.Click += (_, _) => {
                Color? c = PickWithDialog(strokeColor);
                if (c != null) { strokeColor = c.Value; DrawingStyleChanged(s => s.Stroke = strokeColor); }
            };
            strip.Items.Add(strokeButton);

            fillButton = DropDown("Color de relleno de rectángulos, elipses y texto");
            fillButton.DropDownItems.Add("Elegir color…", null, (_, _) => {
                Color? c = PickWithDialog(fillColor ?? Color.White);
                if (c != null) { fillColor = c.Value; DrawingStyleChanged(SetFill); }
            });
            fillButton.DropDownItems.Add("Sin relleno", null, (_, _) => {
                fillColor = null;
                DrawingStyleChanged(SetFill);
            });

            widthButton = DropDown("Grosor de líneas y bordes");
            foreach (float w in Widths) {
                var item = new ToolStripMenuItem($"{w} px", Icons.Line(w, iconSize, Icons.DarkInk));
                item.Click += (_, _) => {
                    strokeWidth = w;
                    DrawingStyleChanged(s => s.Width = strokeWidth);
                };
                widthButton.DropDownItems.Add(item);
            }

            // Fuente y tamaño del texto
            textSeparator = new ToolStripSeparator();
            strip.Items.Add(textSeparator);
            fontButton = TextDropDown("Fuente del texto");
            var installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string name in CommonFonts.Where(installed.Contains)) {
                var item = new ToolStripMenuItem(name) { Font = new Font(name, 10f), Tag = name };
                previewFonts.Add(item.Font);
                item.Click += (_, _) => SetTextFont(name);
                fontButton.DropDownItems.Add(item);
            }
            fontButton.DropDownItems.Add(new ToolStripSeparator());
            fontButton.DropDownItems.Add("Otra fuente…", null, (_, _) => PickOtherFont());

            sizeButton = TextDropDown("Tamaño del texto");
            foreach (float s in TextSizes) {
                var item = new ToolStripMenuItem($"{s} px") { Tag = s };
                item.Click += (_, _) => SetTextSize(s);
                sizeButton.DropDownItems.Add(item);
            }
            strip.Items.Add(new ToolStripSeparator());

            AddTool(Tool.Pixelate, IconKind.Pixelate, "Pixelar (P)\nOculta lo que hay debajo de la zona");
            AddTool(Tool.Picker, IconKind.Picker, "Cuentagotas (I)\nCopia el color de un píxel");
            IconButton(IconKind.Ocr, "Leer texto (O)\nReconoce el texto de la zona y lo copia", RecognizeText);
            strip.Items.Add(new ToolStripSeparator());

            IconButton(IconKind.Copy, "Copiar imagen y cerrar (Ctrl+C / Intro)", CopyAndClose);
            IconButton(IconKind.Save, "Guardar como imagen (Ctrl+S)", SaveAndClose);
            IconButton(IconKind.Undo, "Deshacer (Ctrl+Z)", Undo);
            IconButton(IconKind.Close, "Cerrar sin guardar (Esc)", Close);

            UpdateSwatches();
            UpdateTextControls(strip);
            return strip;
        }

        private void SetFill(Shape s) {
            if (s is not SegmentShape and not PixelateShape) s.Fill = fillColor;
        }

        private void SetTextFont(string family) {
            textFont = family;
            DrawingStyleChanged(s => { if (s is TextShape t) t.FontFamily = textFont; });
        }

        private void SetTextSize(float size) {
            textSize = MathUtil.Clamp(size, MinTextSize, MaxTextSize);
            DrawingStyleChanged(s => { if (s is TextShape t) t.FontSize = textSize; });
        }

        /// <summary>Cualquier fuente instalada, con el diálogo de fuentes de Windows.</summary>
        private void PickOtherFont() {
            // El diálogo trabaja en puntos; el texto de Trazo, en píxeles de la captura
            using var current = new Font(textFont, Math.Max(1, textSize * 72f / 96f));
            using var dialog = new FontDialog {
                Font = current,
                ShowEffects = false,
                FontMustExist = true,
                AllowVerticalFonts = false,
                MinSize = 5,
                MaxSize = 220,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            textFont = dialog.Font.FontFamily.Name;
            textSize = MathUtil.Clamp((float)Math.Round(dialog.Font.SizeInPoints * 96f / 72f), MinTextSize, MaxTextSize);
            DrawingStyleChanged(s => {
                if (s is not TextShape t) return;
                t.FontFamily = textFont;
                t.FontSize = textSize;
            });
        }

        /// <summary>
        /// Fuente y tamaño se muestran con la herramienta Texto o con un texto seleccionado o en edición,
        /// y reflejan los de ese texto.
        /// </summary>
        private void UpdateTextControls() => UpdateTextControls(toolbar);

        private void UpdateTextControls(ToolStrip strip) {
            TextShape? text = editingText ?? selected as TextShape;
            string font = text?.FontFamily ?? textFont;
            float size = text?.FontSize ?? textSize;

            fontButton.Text = font.Length > 16 ? font.Substring(0, 15) + "…" : font;
            sizeButton.Text = $"{size} px";
            foreach (ToolStripItem item in fontButton.DropDownItems) {
                if (item is ToolStripMenuItem m && m.Tag is string name) m.Checked = name.Equals(font, StringComparison.OrdinalIgnoreCase);
            }
            foreach (ToolStripMenuItem item in sizeButton.DropDownItems) {
                item.Checked = item.Tag is float s && s == size;
            }

            bool show = phase == Phase.Editing && (tool == Tool.Text || text != null);
            if (fontButton.Available != show) {
                textSeparator.Available = fontButton.Available = sizeButton.Available = show;
                // La barra cambia de ancho: se recoloca y se recalcula el fondo difuminado
                if (strip.Visible) PositionToolbar();
            }
        }

        private void UpdateSwatches() {
            strokeButton.Image?.Dispose();
            strokeButton.Image = Icons.Stroke(strokeColor, iconSize);
            fillButton.Image?.Dispose();
            fillButton.Image = Icons.Fill(fillColor, iconSize);
            widthButton.Image?.Dispose();
            widthButton.Image = Icons.Line(strokeWidth, iconSize, Icons.LightInk);
            foreach (ToolStripMenuItem item in widthButton.DropDownItems) {
                item.Checked = item.Text == $"{strokeWidth} px";
            }
        }

        /// <summary>
        /// Aplica el cambio (solo esa propiedad) al elemento seleccionado o al texto en edición,
        /// y recuerda el estilo para la próxima captura y el próximo arranque.
        /// </summary>
        private void DrawingStyleChanged(Action<Shape> apply) {
            ApplyStyleToSelected(apply);
            UpdateSwatches();
            UpdateTextControls();
            settings.StrokeColorValue = strokeColor;
            settings.FillColorValue = fillColor;
            settings.StrokeWidth = strokeWidth;
            settings.TextFont = textFont;
            settings.TextSize = textSize;
            settings.Save();
        }

        private Color? PickWithDialog(Color current) {
            using var dialog = new ColorDialog { Color = current, FullOpen = true, AnyColor = true };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Color : null;
        }

        /// <summary>Herramientas que se recuerdan entre capturas; el cuentagotas y Pixelar, no.</summary>
        private static bool IsDrawingTool(Tool t) => t is Tool.Arrow or Tool.Line or Tool.Rectangle or Tool.Ellipse or Tool.Text;

        private void SetTool(Tool t) {
            CommitText();
            // Al cambiar de herramienta se suelta el elemento seleccionado
            if (t != tool) Deselect();
            tool = t;
            foreach (KeyValuePair<Tool, ToolStripButton> pair in toolButtons) pair.Value.Checked = pair.Key == t;
            if (IsDrawingTool(t) && settings.LastTool != t.ToString()) {
                settings.LastTool = t.ToString();
                settings.Save();
            }
            UpdateTextControls();
            Cursor = ToolCursor;
            Invalidate(MagnifierRect(mouse));
        }

        private Cursor ToolCursor => phase == Phase.Selecting ? crossCursor.Cursor : tool switch {
            Tool.Text => Cursors.IBeam,
            // Elegida la zona, la cruz deja paso al cursor normal del usuario
            _ => Cursors.Default,
        };

        private void PositionToolbar() {
            Size size = toolbar.PreferredSize;
            toolbar.Size = size;
            Rectangle area = ScreenAreaFor(selection);

            int x = selection.Right - size.Width;
            int y = selection.Bottom + 6;
            if (y + size.Height > area.Bottom) y = selection.Top - size.Height - 6;
            if (y < area.Top) y = selection.Bottom - size.Height - 6;

            x = Math.Max(area.Left, Math.Min(x, area.Right - size.Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height));
            toolbar.Location = new Point(x, y);

            toolbarBackdrop?.Dispose();
            toolbarBackdrop = CreateBlurredBackdrop(toolbar.Bounds);
        }

        /// <summary>
        /// Lo que hay detrás de la barra, difuminado, para el efecto cristal. Se calcula una sola vez
        /// al colocar la barra: reducir la imagen 10 veces y volver a ampliarla la desenfoca casi gratis.
        /// </summary>
        private Bitmap CreateBlurredBackdrop(Rectangle r) {
            using var behind = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(behind)) {
                g.DrawImage(dimmed, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                Rectangle bright = Rectangle.Intersect(r, selection);
                if (!bright.IsEmpty) {
                    var dest = bright;
                    dest.Offset(-r.X, -r.Y);
                    g.DrawImage(screenshot, dest, bright, GraphicsUnit.Pixel);
                }
            }

            int sw = Math.Max(1, r.Width / 10), sh = Math.Max(1, r.Height / 10);
            using var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(small)) {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(behind, 0, 0, sw, sh);
            }

            var result = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(result))
            using (var attrs = new ImageAttributes()) {
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small, new Rectangle(0, 0, r.Width, r.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
            }
            return result;
        }

        protected override void OnInvalidated(InvalidateEventArgs e) {
            base.OnInvalidated(e);
            // La barra pinta lo que tiene detrás (esquinas transparentes): se repinta si eso cambia
            if (toolbar is { Visible: true } && e.InvalidRect.IntersectsWith(toolbar.Bounds)) {
                toolbar.Invalidate();
            }
        }

        /// <summary>
        /// Barra con aspecto de cristal: fondo difuminado con un velo gris translúcido, esquinas redondeadas
        /// y botones con resaltado suave. Los menús desplegables mantienen el estilo normal.
        /// </summary>
        private sealed class GlassToolbarRenderer : ToolStripProfessionalRenderer {

            private readonly CaptureOverlay owner;

            public GlassToolbarRenderer(CaptureOverlay owner) {
                this.owner = owner;
                RoundedEdges = false;
            }

            private int Radius => (int)Math.Round(12 * owner.scale);

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
                if (e.ToolStrip is ToolStripDropDown) { base.OnRenderToolStripBackground(e); return; }

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using GraphicsPath path = RoundedRect(r, Radius);

                if (owner.toolbarBackdrop is Bitmap backdrop) {
                    GraphicsState state = g.Save();
                    g.SetClip(path);
                    g.DrawImageUnscaled(backdrop, 0, 0);
                    g.Restore(state);
                }
                using (var veil = new SolidBrush(Color.FromArgb(205, 18, 18, 20))) g.FillPath(veil, path);
                using (var edge = new Pen(GlassEdge, 1f)) g.DrawPath(edge, path);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
                if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) {
                if (e.ToolStrip is ToolStripDropDown) { base.OnRenderButtonBackground(e); return; }
                var button = (ToolStripButton)e.Item;
                DrawHighlight(e.Graphics, e.Item, button.Pressed ? 75 : button.Checked ? 60 : button.Selected ? 35 : 0);
            }

            protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) {
                if (e.ToolStrip is ToolStripDropDown) { base.OnRenderDropDownButtonBackground(e); return; }
                DrawHighlight(e.Graphics, e.Item, e.Item.Pressed ? 75 : e.Item.Selected ? 35 : 0);
            }

            private void DrawHighlight(Graphics g, ToolStripItem item, int alpha) {
                if (alpha == 0) return;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, item.Width - 1, item.Height - 1);
                using GraphicsPath path = RoundedRect(r, (int)Math.Round(6 * owner.scale));
                using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                g.FillPath(brush, path);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
                if (e.ToolStrip is ToolStripDropDown) { base.OnRenderSeparator(e); return; }
                int x = e.Item.Width / 2;
                int margin = e.Item.Height / 4;
                using var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
                e.Graphics.DrawLine(pen, x, margin, x, e.Item.Height - margin);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
                // Texto claro en la barra oscura; normal en los menús desplegables
                e.TextColor = e.ToolStrip is ToolStripDropDown ? SystemColors.MenuText : Icons.LightInk;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
                if (e.Item?.Owner is not ToolStripDropDown) e.ArrowColor = Icons.LightInk;
                base.OnRenderArrow(e);
            }
        }

        /// <summary>Área de trabajo (en coordenadas de la ventana) del monitor que contiene el rectángulo.</summary>
        private Rectangle ScreenAreaFor(Rectangle clientRect) {
            var screenRect = clientRect;
            screenRect.Offset(virtualBounds.Location);
            Rectangle area = System.Windows.Forms.Screen.FromRectangle(screenRect).WorkingArea;
            area.Offset(-virtualBounds.X, -virtualBounds.Y);
            return area;
        }

        // ───────────────────────────── Pintado ─────────────────────────────

        protected override void OnPaintBackground(PaintEventArgs e) {
            // Todo se pinta en OnPaint
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;

            // Copia directa de píxeles: lo más rápido posible
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);
            if (!selection.IsEmpty) {
                Rectangle bright = Rectangle.Intersect(clip, selection);
                if (!bright.IsEmpty) g.DrawImage(screenshot, bright, bright, GraphicsUnit.Pixel);
            }
            g.CompositingMode = CompositingMode.SourceOver;

            if (!selection.IsEmpty) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                GraphicsState state = g.Save();
                g.SetClip(selection);
                foreach (Shape s in shapes) s.Draw(g);
                drawing?.Draw(g);
                editingText?.Draw(g);
                g.Restore(state);

                if (editingText != null && textEditor != null) DrawTextEditing(g, editingText, textEditor.SelectionStart);

                // Los textos se marcan con un recuadro; flechas y figuras, con sus tiradores (encima de los de la zona)
                if (selected is TextShape) {
                    using var dash = new Pen(Accent, 1) { DashStyle = DashStyle.Dash };
                    g.DrawRectangle(dash, selected.Bounds);
                }

                DrawSelectionFrame(g);

                if (phase == Phase.Editing) DrawHandles(g);
                DrawHandles(g, ShapeHandlePoints(selected));
                if (phase == Phase.Selecting || resizing != Edge.None) DrawSizeLabel(g);
            }

            if (ShowMagnifier) DrawMagnifier(g);
            if (ocrText != null) DrawOcrPreview(g);
            if (status != null) DrawStatus(g);
        }

        /// <summary>Radio de las esquinas de la zona: solo es un detalle visual, la captura sigue siendo rectangular.</summary>
        private int SelectionRadius => Math.Min((int)Math.Round(4 * scale), Math.Min(selection.Width, selection.Height) / 2);

        /// <summary>Borde redondeado de la zona; fuera de la curva se vuelve a pintar la pantalla oscurecida.</summary>
        private void DrawSelectionFrame(Graphics g) {
            int radius = SelectionRadius;
            using var border = new Pen(EdgeGray, 1);
            if (radius < 1) {
                g.SmoothingMode = SmoothingMode.None;
                g.DrawRectangle(border, selection.X, selection.Y, selection.Width - 1, selection.Height - 1);
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath rounded = RoundedRect(new Rectangle(selection.X, selection.Y, selection.Width - 1, selection.Height - 1), radius);
            using (var ring = new GraphicsPath(FillMode.Alternate)) {
                ring.AddRectangle(new RectangleF(selection.X - 0.5f, selection.Y - 0.5f, selection.Width, selection.Height));
                ring.AddPath(rounded, false);

                int size = radius + 2;
                var image = new Rectangle(Point.Empty, dimmed.Size);
                GraphicsState state = g.Save();
                foreach (Point corner in new[] {
                    new Point(selection.X - 1, selection.Y - 1), new Point(selection.Right - size + 1, selection.Y - 1),
                    new Point(selection.X - 1, selection.Bottom - size + 1), new Point(selection.Right - size + 1, selection.Bottom - size + 1) }) {
                    Rectangle cr = Rectangle.Intersect(new Rectangle(corner, new Size(size, size)), image);
                    if (cr.IsEmpty) continue;
                    using var texture = new TextureBrush(dimmed, cr);
                    texture.TranslateTransform(cr.X, cr.Y);
                    g.SetClip(cr);
                    g.FillPath(texture, ring);
                }
                g.Restore(state);
            }
            g.DrawPath(border, rounded);
        }

        private Rectangle SizeLabelRect(Rectangle sel) {
            Size size = TextRenderer.MeasureText($"{sel.Width} × {sel.Height}", labelFont);
            var r = new Rectangle(sel.X, sel.Y - size.Height - 8, size.Width + 10, size.Height + 4);
            if (r.Y < ScreenAreaFor(sel).Top) r.Y = sel.Y + 4;
            return r;
        }

        private void DrawSizeLabel(Graphics g) {
            Rectangle r = SizeLabelRect(selection);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = new SolidBrush(Color.FromArgb(210, 20, 20, 20));
            using GraphicsPath path = RoundedRect(r, Math.Min((int)Math.Round(5 * scale), r.Height / 2));
            g.FillPath(back, path);
            TextRenderer.DrawText(g, $"{selection.Width} × {selection.Height}", labelFont, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private bool ShowMagnifier => tool == Tool.Picker && phase == Phase.Editing || phase == Phase.Selecting;

        private Rectangle MagnifierRect(Point p) {
            int size = MagnifierSource * MagnifierZoom;
            var r = new Rectangle(p.X + 20, p.Y + 20, size, size + 22);
            Rectangle area = ScreenAreaFor(new Rectangle(p, new Size(1, 1)));
            if (r.Right > area.Right) r.X = p.X - 20 - r.Width;
            if (r.Bottom > area.Bottom) r.Y = p.Y - 20 - r.Height;
            r.Inflate(2, 2);
            return r;
        }

        private int MagnifierRadius => (int)Math.Round(8 * scale);

        private void DrawMagnifier(Graphics g) {
            if (!ClientRectangle.Contains(mouse)) return;
            Rectangle outer = MagnifierRect(mouse);
            int size = MagnifierSource * MagnifierZoom;
            const int LabelHeight = 22;
            var card = new Rectangle(outer.X + 2, outer.Y + 2, size, size + LabelHeight);
            var src = new Rectangle(mouse.X - MagnifierSource / 2, mouse.Y - MagnifierSource / 2, MagnifierSource, MagnifierSource);

            int radius = MagnifierRadius;

            // Se compone en un mapa de bits y se pinta con un pincel de textura para que las esquinas salgan suavizadas
            magnifierCard ??= new Bitmap(card.Width, card.Height, PixelFormat.Format32bppPArgb);
            using (Graphics cg = Graphics.FromImage(magnifierCard)) {
                cg.Clear(Color.Black);
                cg.InterpolationMode = InterpolationMode.NearestNeighbor;
                cg.PixelOffsetMode = PixelOffsetMode.Half;
                cg.DrawImage(screenshot, new Rectangle(0, 0, size, size), src, GraphicsUnit.Pixel);
                cg.PixelOffsetMode = PixelOffsetMode.Default;

                var center = new Rectangle(MagnifierSource / 2 * MagnifierZoom, MagnifierSource / 2 * MagnifierZoom,
                    MagnifierZoom, MagnifierZoom);
                cg.DrawRectangle(Pens.White, center);

                Color c = PixelAt(mouse);
                TextRenderer.DrawText(cg, $"#{c.R:X2}{c.G:X2}{c.B:X2}   {mouse.X + virtualBounds.X}, {mouse.Y + virtualBounds.Y}",
                    labelFont, new Rectangle(0, size, size, LabelHeight), Color.White, Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            using var texture = new TextureBrush(magnifierCard);
            texture.TranslateTransform(card.X, card.Y);
            using GraphicsPath path = RoundedRect(card, radius);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.FillPath(texture, path);
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // Borde fino que contrasta con lo que hay debajo: claro sobre zonas oscuras y oscuro sobre zonas claras
            using GraphicsPath edgePath = RoundedRect(new Rectangle(card.X, card.Y, card.Width - 1, card.Height - 1), radius);
            using var edge = new Pen(EdgeColorUnder(card), 1f);
            g.DrawPath(edge, edgePath);
        }

        /// <summary>
        /// Color del borde según el brillo medio de lo que se ve bajo la zona: la captura dentro de la selección
        /// y la pantalla oscurecida fuera de ella.
        /// </summary>
        private Color EdgeColorUnder(Rectangle area) {
            const int Step = 6;
            double total = 0;
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y += Step) {
                for (int x = area.Left; x < area.Right; x += Step) {
                    if (x < 0 || y < 0 || x >= screenshot.Width || y >= screenshot.Height) continue;
                    Color c = (selection.Contains(x, y) ? screenshot : dimmed).GetPixel(x, y);
                    total += c.R * 0.299 + c.G * 0.587 + c.B * 0.114;
                    count++;
                }
            }
            double luma = count > 0 ? total / count : 0;
            return luma > 90 ? Color.FromArgb(150, 0, 0, 0) : GlassEdge;
        }

        private Color PixelAt(Point p) =>
            p.X >= 0 && p.Y >= 0 && p.X < screenshot.Width && p.Y < screenshot.Height ? screenshot.GetPixel(p.X, p.Y) : Color.Black;

        /// <param name="swatch">Si se indica, se muestra una muestra de ese color a la izquierda del texto.</param>
        private void ShowStatus(string? message, Color? swatch = null) {
            Invalidate(statusRect);
            status = message;
            statusSwatch = swatch;
            statusTimer.Stop();
            if (message == null) return;

            Size text = TextRenderer.MeasureText(message, labelFont);
            int padX = (int)(14 * scale), padY = (int)(8 * scale);
            int swatchWidth = swatch != null ? SwatchSize + padX / 2 * 2 : 0;
            int width = text.Width + swatchWidth + padX * 2;
            int height = Math.Max(text.Height, swatch != null ? SwatchSize : 0) + padY * 2;

            Rectangle area = ScreenAreaFor(selection.IsEmpty ? new Rectangle(mouse, new Size(1, 1)) : selection);
            statusRect = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (int)(16 * scale), width, height);
            Invalidate(statusRect);
            statusTimer.Start();
        }

        private int SwatchSize => (int)(26 * scale);

        private void DrawStatus(Graphics g) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(statusRect, (int)(8 * scale)))
            using (var back = new SolidBrush(CardBack)) {
                g.FillPath(back, path);
            }

            Rectangle textRect = statusRect;
            if (statusSwatch is Color c) {
                int padX = (int)(14 * scale);
                var swatch = new Rectangle(statusRect.X + padX, statusRect.Y + (statusRect.Height - SwatchSize) / 2, SwatchSize, SwatchSize);
                using (var path = RoundedRect(swatch, (int)(5 * scale)))
                using (var fill = new SolidBrush(c))
                using (var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                textRect = Rectangle.FromLTRB(swatch.Right + padX / 2, statusRect.Y, statusRect.Right - padX / 2, statusRect.Bottom);
            }
            TextRenderer.DrawText(g, status, labelFont, textRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        internal static GraphicsPath RoundedRect(Rectangle r, int radius) => RoundedRect((RectangleF)r, radius);

        internal static GraphicsPath RoundedRect(RectangleF r, float radius) {
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

        private void InvalidateShape(Shape s, Rectangle? before = null) {
            Rectangle r = s.Bounds;
            if (before is Rectangle b) r = Rectangle.Union(r, b);
            // Incluye los tiradores, que sobresalen del contorno
            r.Inflate(HandleSize + 2, HandleSize + 2);
            Invalidate(r);
        }

        // ───────────────────────────── Vista previa del OCR ─────────────────────────────

        private int CardPadding => (int)(14 * scale);
        private int CardTitleHeight => (int)(24 * scale);
        private int CardFootHeight => (int)(20 * scale);
        private const TextFormatFlags BodyFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        private void ShowOcrPreview(string text) {
            HideOcrPreview();
            ocrText = text;

            int width = (int)(380 * scale);
            int pad = CardPadding;
            int maxBody = (int)(260 * scale);
            Size body = TextRenderer.MeasureText(text, cardBodyFont, new Size(width - pad * 2, int.MaxValue), BodyFlags);
            int height = pad + CardTitleHeight + Math.Min(body.Height, maxBody) + CardFootHeight + pad;

            // Al lado de la selección si cabe; si no, dentro de ella en la esquina superior derecha
            Rectangle area = ScreenAreaFor(selection);
            int gap = (int)(10 * scale);
            int x, y = selection.Top;
            if (selection.Right + gap + width <= area.Right) x = selection.Right + gap;
            else if (selection.Left - gap - width >= area.Left) x = selection.Left - gap - width;
            else { x = selection.Right - width - gap; y = selection.Top + gap; }

            x = Math.Max(area.Left, Math.Min(x, area.Right - width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
            ocrRect = new Rectangle(x, y, width, height);
            Invalidate(ocrRect);
        }

        private void HideOcrPreview() {
            if (ocrText == null) return;
            ocrText = null;
            Invalidate(ocrRect);
        }

        private void DrawOcrPreview(Graphics g) {
            int pad = CardPadding;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(ocrRect, (int)(10 * scale))) {
                using var back = new SolidBrush(CardBack);
                g.FillPath(back, path);
                using var border = new Pen(Color.FromArgb(90, Accent), 1);
                g.DrawPath(border, path);
            }

            var title = new Rectangle(ocrRect.X + pad, ocrRect.Y + pad, ocrRect.Width - pad * 2, CardTitleHeight);
            TextRenderer.DrawText(g, "✓  Texto copiado al portapapeles", cardTitleFont, title, Color.FromArgb(90, 200, 120),
                TextFormatFlags.Left | TextFormatFlags.Top);

            var body = Rectangle.FromLTRB(title.Left, title.Bottom, title.Right, ocrRect.Bottom - pad - CardFootHeight);
            TextRenderer.DrawText(g, ocrText, cardBodyFont, body, Color.White, BodyFlags);

            int lines = ocrText!.Split('\n').Length;
            var foot = new Rectangle(title.Left, body.Bottom + (int)(4 * scale), title.Width, CardFootHeight);
            TextRenderer.DrawText(g, $"{lines} {(lines == 1 ? "línea" : "líneas")} · {ocrText.Length} caracteres · Esc para ocultar",
                cardFootFont, foot, Color.FromArgb(150, 150, 150), TextFormatFlags.Left | TextFormatFlags.Bottom);
        }

        // ───────────────────────────── Ratón ─────────────────────────────

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);

            if (textEditor != null) {
                CommitText();
                return;
            }

            if (phase == Phase.Selecting) {
                if (e.Button == MouseButtons.Left) selectStart = e.Location;
                else if (e.Button == MouseButtons.Right) Close();
                return;
            }

            HideOcrPreview();
            if (e.Button != MouseButtons.Left) return;

            // Tiradores del elemento seleccionado: se redimensiona ese elemento
            Edge handle = ShapeHandleAt(e.Location);
            if (handle != Edge.None) {
                StartShapeResize(handle, e.Location);
                return;
            }

            // Bordes y tiradores de la zona: se redimensiona la captura
            Edge edge = EdgeAt(e.Location);
            if (edge != Edge.None) {
                resizing = edge;
                resizeOrigin = selection;
                resizeStart = e.Location;
                toolbar.Visible = false;
                InvalidateSelectionChange(selection, selection);
                return;
            }

            if (tool == Tool.Picker) {
                PickColor(e.Location);
                return;
            }

            // Solo el elemento seleccionado (con doble clic) se arrastra; en el resto se dibuja encima
            if (selected != null && selected.HitTest(e.Location)) {
                StartMoving(e.Location);
                return;
            }
            Deselect();

            switch (tool) {
                case Tool.Arrow:
                    drawing = new ArrowShape { Start = e.Location, End = e.Location, Stroke = strokeColor, Width = strokeWidth };
                    break;

                case Tool.Line:
                    drawing = new LineShape { Start = e.Location, End = e.Location, Stroke = strokeColor, Width = strokeWidth };
                    break;

                case Tool.Pixelate:
                    drawing = new PixelateShape(screenshot) { Start = e.Location, End = e.Location };
                    break;

                case Tool.Rectangle:
                    drawing = new RectangleShape { Start = e.Location, End = e.Location, Stroke = strokeColor, Fill = fillColor, Width = strokeWidth };
                    break;

                case Tool.Ellipse:
                    drawing = new EllipseShape { Start = e.Location, End = e.Location, Stroke = strokeColor, Fill = fillColor, Width = strokeWidth };
                    break;

                case Tool.Text:
                    BeginText(new TextShape {
                        Location = e.Location, Stroke = strokeColor, Fill = fillColor,
                        Width = strokeWidth, FontFamily = textFont, FontSize = textSize,
                    }, isExisting: false);
                    break;
            }
        }

        /// <summary>
        /// Doble clic sobre un elemento: lo selecciona y, si se mantiene pulsado, se arrastra.
        /// Doble clic sobre un texto ya seleccionado: se edita.
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            WheelStyle(e);
        }

        /// <summary>
        /// Ctrl+rueda: tamaño del texto (con la herramienta Texto o un texto seleccionado o en edición)
        /// o grosor de líneas y bordes, saltando por los mismos valores que los desplegables.
        /// </summary>
        private void WheelStyle(MouseEventArgs e) {
            if (phase != Phase.Editing || (ModifierKeys & Keys.Control) == 0) return;
            if (e is HandledMouseEventArgs handled) handled.Handled = true;

            // Los paneles táctiles mandan giros pequeños: se acumulan hasta una muesca
            wheelDelta += e.Delta;
            int steps = wheelDelta / SystemInformation.MouseWheelScrollDelta;
            if (steps == 0) return;
            wheelDelta -= steps * SystemInformation.MouseWheelScrollDelta;

            // Una racha de giros sobre el mismo elemento se deshace de una vez
            bool coalesce = selected != null && selected == wheelShape && undoStack.Count == wheelUndoDepth;
            int depth = undoStack.Count;

            TextShape? text = editingText ?? selected as TextShape;
            if (text != null || (tool == Tool.Text && selected == null)) {
                float size = StepValue(TextSizes, text?.FontSize ?? textSize, steps);
                SetTextSize(size);
                ShowStatus($"Tamaño del texto: {size} px");
            } else {
                strokeWidth = StepValue(Widths, selected?.Width ?? strokeWidth, steps);
                DrawingStyleChanged(s => s.Width = strokeWidth);
                ShowStatus($"Grosor: {strokeWidth} px");
            }

            if (coalesce && undoStack.Count > depth) undoStack.Pop();
            wheelShape = selected;
            wheelUndoDepth = undoStack.Count;
        }

        /// <summary>El valor de la lista que queda <paramref name="steps"/> posiciones por encima o por debajo.</summary>
        private static float StepValue(float[] values, float current, int steps) {
            int i = steps > 0
                ? Array.FindLastIndex(values, v => v <= current)
                : Array.FindIndex(values, v => v >= current);
            if (i < 0) i = steps > 0 ? -1 : values.Length;
            float next = values[Math.Max(0, Math.Min(values.Length - 1, i + steps))];
            // Fuera de la lista (p. ej. un tamaño elegido con "Otra fuente…") nunca se mueve al revés
            return steps > 0 ? Math.Max(next, current) : Math.Min(next, current);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e) {
            base.OnMouseDoubleClick(e);
            if (phase != Phase.Editing || textEditor != null || e.Button != MouseButtons.Left) return;
            if (tool == Tool.Picker || resizing != Edge.None || shapeResize != Edge.None) return;

            Shape? hit = HitTest(e.Location);
            if (hit == null) return;

            // El segundo clic ya empezó a dibujar una figura: se descarta
            if (drawing != null) {
                Rectangle b = drawing.Bounds;
                drawing = null;
                b.Inflate(4, 4);
                Invalidate(b);
            }

            if (hit == selected && hit is TextShape text) {
                // El segundo clic empezó a arrastrarlo
                if (moving) {
                    moving = false;
                    if (!movedSinceDown) undoStack.Pop();
                }
                Deselect();
                BeginText(text, isExisting: true);
                return;
            }

            if (hit != selected) {
                Deselect();
                selected = hit;
                UpdateTextControls();
                InvalidateShape(hit);
            }
            if (!moving) StartMoving(e.Location);
        }

        private void StartMoving(Point p) {
            PushUndo();
            moving = true;
            movedSinceDown = false;
            dragLast = p;
        }

        private void Deselect() {
            if (selected == null) return;
            InvalidateShape(selected);
            selected = null;
            UpdateTextControls();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            if (ShowMagnifier) {
                Invalidate(MagnifierRect(mouse));
                Invalidate(MagnifierRect(e.Location));
            }
            mouse = e.Location;

            if (phase == Phase.Selecting) {
                if (selectStart is Point start) {
                    Rectangle old = selection;
                    selection = Rectangle.Intersect(Normalize(start, e.Location), ClientRectangle);
                    InvalidateSelectionChange(old, selection);
                }
                return;
            }

            if (resizing != Edge.None) {
                Rectangle old = selection;
                selection = Resized(resizeOrigin, resizing, e.X - resizeStart.X, e.Y - resizeStart.Y);
                if (selection != old) InvalidateSelectionChange(old, selection);
                return;
            }

            if (shapeResize != Edge.None) {
                ResizeShape(e.Location);
                return;
            }

            if (drawing != null) {
                Rectangle before = drawing.Bounds;
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                switch (drawing) {
                    case BoxShape box:
                        box.End = shift ? ConstrainSquare(box.Start, e.Location) : e.Location;
                        break;
                    case SegmentShape segment:
                        segment.End = shift ? ConstrainAxis(segment.Start, e.Location) : e.Location;
                        break;
                }
                InvalidateShape(drawing, before);
            } else if (moving && selected != null) {
                int dx = e.X - dragLast.X, dy = e.Y - dragLast.Y;
                if (dx != 0 || dy != 0) {
                    Rectangle before = selected.Bounds;
                    selected.Offset(dx, dy);
                    movedSinceDown = true;
                    dragLast = e.Location;
                    InvalidateShape(selected, before);
                }
            } else if (textEditor == null) {
                Edge handle = ShapeHandleAt(e.Location);
                Edge edge = EdgeAt(e.Location);
                Cursor = handle != Edge.None ? ShapeHandleCursor(handle)
                    : edge != Edge.None ? EdgeCursor(edge)
                    : tool != Tool.Picker && selected != null && selected.HitTest(e.Location) ? Cursors.SizeAll
                    : ToolCursor;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);

            if (phase == Phase.Selecting) {
                if (selectStart == null || e.Button != MouseButtons.Left) return;
                selectStart = null;
                if (selection.Width < 4 || selection.Height < 4) {
                    // Un clic sin arrastrar selecciona el monitor entero
                    Rectangle monitor = System.Windows.Forms.Screen.FromPoint(PointToScreen(e.Location)).Bounds;
                    monitor.Offset(-virtualBounds.X, -virtualBounds.Y);
                    selection = Rectangle.Intersect(monitor, ClientRectangle);
                }
                EnterEditing();
                return;
            }

            if (shapeResize != Edge.None) {
                EndShapeResize();
                return;
            }

            if (resizing != Edge.None) {
                resizing = Edge.None;
                // Quita la etiqueta de tamaño y vuelve a colocar la barra junto a la nueva zona
                InvalidateSelectionChange(selection, selection);
                PositionToolbar();
                toolbar.Visible = true;
                return;
            }

            if (drawing != null) {
                Rectangle b = drawing.Bounds;
                bool tooSmall = drawing switch {
                    BoxShape box => box.Rect.Width < 3 || box.Rect.Height < 3,
                    SegmentShape segment => Distance(segment.Start, segment.End) < 5,
                    _ => false,
                };
                if (!tooSmall) {
                    PushUndo();
                    shapes.Add(drawing);
                    // Queda seleccionado para moverlo o ajustarlo sin tener que hacer doble clic
                    selected = drawing;
                    UpdateTextControls();
                }
                drawing = null;
                b.Inflate(HandleSize + 2, HandleSize + 2);
                Invalidate(b);
            }

            if (moving) {
                moving = false;
                if (!movedSinceDown) undoStack.Pop();
            }
        }

        private void EnterEditing() {
            phase = Phase.Editing;
            Cursor = ToolCursor;
            UpdateTextControls();
            PositionToolbar();
            toolbar.Visible = true;
            Invalidate();
        }

        private void InvalidateSelectionChange(Rectangle oldSel, Rectangle newSel) {
            Rectangle r = Rectangle.Union(Decorated(oldSel), Decorated(newSel));
            Invalidate(r);

            Rectangle Decorated(Rectangle sel) {
                if (sel.IsEmpty) return sel;
                Rectangle d = Rectangle.Union(sel, SizeLabelRect(sel));
                d.Inflate(HandleSize + 2, HandleSize + 2);
                return d;
            }
        }

        // ───────────────────────────── Redimensionar la zona ─────────────────────────────

        private int HandleSize => (int)Math.Round(8 * scale);

        /// <summary>Distancia al borde de la zona dentro de la que se puede arrastrar para redimensionarla.</summary>
        private int EdgeTolerance => (int)Math.Round(5 * scale);

        /// <summary>Los 8 tiradores de un rectángulo (esquinas y centros de los lados) y los lados que mueve cada uno.</summary>
        private static IEnumerable<(Point point, Edge edge)> HandlePoints(Rectangle r) {
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            int right = r.Right - 1, bottom = r.Bottom - 1;
            yield return (new Point(r.X, r.Y), Edge.Left | Edge.Top);
            yield return (new Point(cx, r.Y), Edge.Top);
            yield return (new Point(right, r.Y), Edge.Right | Edge.Top);
            yield return (new Point(right, cy), Edge.Right);
            yield return (new Point(right, bottom), Edge.Right | Edge.Bottom);
            yield return (new Point(cx, bottom), Edge.Bottom);
            yield return (new Point(r.X, bottom), Edge.Left | Edge.Bottom);
            yield return (new Point(r.X, cy), Edge.Left);
        }

        private void DrawHandles(Graphics g) => DrawHandles(g, HandlePoints(selection).Select(h => h.point));

        private void DrawHandles(Graphics g, IEnumerable<Point> points) {
            float size = HandleSize + 1;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.White);
            using var edge = new Pen(Color.FromArgb(160, 0, 0, 0), 1);
            foreach (Point p in points) {
                var r = new RectangleF(p.X - size / 2, p.Y - size / 2, size, size);
                g.FillEllipse(fill, r);
                g.DrawEllipse(edge, r);
            }
        }

        private bool OnHandle(Point p, Point handle) {
            int reach = HandleSize / 2 + 2;
            return Math.Abs(p.X - handle.X) <= reach && Math.Abs(p.Y - handle.Y) <= reach;
        }

        /// <summary>Lados de la zona bajo el ratón: en los tiradores o a lo largo del borde.</summary>
        private Edge EdgeAt(Point p) {
            if (phase != Phase.Editing || selection.IsEmpty) return Edge.None;

            foreach ((Point point, Edge edge) in HandlePoints(selection)) {
                if (OnHandle(p, point)) return edge;
            }

            int tol = EdgeTolerance;
            Rectangle outer = selection;
            outer.Inflate(tol, tol);
            if (!outer.Contains(p)) return Edge.None;

            Edge result = Edge.None;
            int dl = Math.Abs(p.X - selection.Left), dr = Math.Abs(p.X - (selection.Right - 1));
            int dt = Math.Abs(p.Y - selection.Top), db = Math.Abs(p.Y - (selection.Bottom - 1));
            if (Math.Min(dl, dr) <= tol) result |= dl <= dr ? Edge.Left : Edge.Right;
            if (Math.Min(dt, db) <= tol) result |= dt <= db ? Edge.Top : Edge.Bottom;
            return result;
        }

        // ───────────────────────────── Redimensionar elementos ─────────────────────────────

        /// <summary>Tiradores del elemento seleccionado: los 8 de un rectángulo o elipse, o los dos extremos de una flecha.</summary>
        private IEnumerable<Point> ShapeHandlePoints(Shape? shape) => shape switch {
            BoxShape box => HandlePoints(box.Rect).Select(h => h.point),
            SegmentShape segment => new[] { segment.Start, segment.End },
            _ => Enumerable.Empty<Point>(),
        };

        /// <summary>
        /// Tirador del elemento seleccionado bajo el ratón. En rectángulos y elipses, los lados que mueve;
        /// en flechas, Left para el origen y Right para la punta.
        /// </summary>
        private Edge ShapeHandleAt(Point p) {
            if (tool == Tool.Picker) return Edge.None;
            switch (selected) {
                case BoxShape box:
                    foreach ((Point point, Edge edge) in HandlePoints(box.Rect)) {
                        if (OnHandle(p, point)) return edge;
                    }
                    break;
                case SegmentShape segment:
                    // La punta primero: si los extremos están muy juntos, suele ser la que se quiere mover
                    if (OnHandle(p, segment.End)) return Edge.Right;
                    if (OnHandle(p, segment.Start)) return Edge.Left;
                    break;
            }
            return Edge.None;
        }

        private Cursor ShapeHandleCursor(Edge edge) => selected is SegmentShape ? Cursors.SizeAll : EdgeCursor(edge);

        private void StartShapeResize(Edge edge, Point p) {
            PushUndo();
            shapeResize = edge;
            movedSinceDown = false;
            resizeStart = p;
            if (selected is BoxShape box) shapeOrigin = box.Rect;
        }

        private void ResizeShape(Point p) {
            if (selected == null) return;
            Rectangle before = selected.Bounds;
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            switch (selected) {
                case BoxShape box: {
                    int dx = p.X - resizeStart.X, dy = p.Y - resizeStart.Y;
                    int left = shapeOrigin.Left, top = shapeOrigin.Top, right = shapeOrigin.Right, bottom = shapeOrigin.Bottom;
                    if (shapeResize.HasFlag(Edge.Left)) left += dx;
                    if (shapeResize.HasFlag(Edge.Right)) right += dx;
                    if (shapeResize.HasFlag(Edge.Top)) top += dy;
                    if (shapeResize.HasFlag(Edge.Bottom)) bottom += dy;
                    // Se puede dar la vuelta pasando un lado por encima del opuesto: Rect se normaliza
                    box.Start = new Point(left, top);
                    box.End = new Point(right, bottom);
                    break;
                }
                case SegmentShape segment:
                    if (shapeResize == Edge.Right) segment.End = shift ? ConstrainAxis(segment.Start, p) : p;
                    else segment.Start = shift ? ConstrainAxis(segment.End, p) : p;
                    break;
            }
            movedSinceDown = true;
            InvalidateShape(selected, before);
        }

        private void EndShapeResize() {
            shapeResize = Edge.None;
            if (!movedSinceDown) {
                undoStack.Pop();
                return;
            }
            // Una figura que se ha quedado sin tamaño se deshace, igual que al dibujarla
            bool tooSmall = selected switch {
                BoxShape box => box.Rect.Width < 3 || box.Rect.Height < 3,
                SegmentShape segment => Distance(segment.Start, segment.End) < 5,
                _ => false,
            };
            if (tooSmall) Undo();
        }

        private static Cursor EdgeCursor(Edge edge) => edge switch {
            Edge.Left or Edge.Right => Cursors.SizeWE,
            Edge.Top or Edge.Bottom => Cursors.SizeNS,
            Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW,
        };

        private Rectangle Resized(Rectangle r, Edge edge, int dx, int dy) {
            Rectangle bounds = ClientRectangle;
            int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if (edge.HasFlag(Edge.Left)) left = MathUtil.Clamp(left + dx, bounds.Left, right - MinSelection);
            if (edge.HasFlag(Edge.Right)) right = MathUtil.Clamp(right + dx, left + MinSelection, bounds.Right);
            if (edge.HasFlag(Edge.Top)) top = MathUtil.Clamp(top + dy, bounds.Top, bottom - MinSelection);
            if (edge.HasFlag(Edge.Bottom)) bottom = MathUtil.Clamp(bottom + dy, top + MinSelection, bounds.Bottom);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Shape? HitTest(Point p) {
            for (int i = shapes.Count - 1; i >= 0; i--) {
                if (shapes[i].HitTest(p)) return shapes[i];
            }
            return null;
        }

        private static Rectangle Normalize(Point a, Point b) =>
            Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        private static Point ConstrainSquare(Point start, Point p) {
            int dx = p.X - start.X, dy = p.Y - start.Y;
            int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new Point(start.X + (dx < 0 ? -d : d), start.Y + (dy < 0 ? -d : d));
        }

        private static Point ConstrainAxis(Point start, Point p) =>
            Math.Abs(p.X - start.X) >= Math.Abs(p.Y - start.Y) ? new Point(p.X, start.Y) : new Point(start.X, p.Y);

        private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

        // ───────────────────────────── Estilo y deshacer ─────────────────────────────

        private void ApplyStyleToSelected(Action<Shape> apply) {
            // Texto que se está escribiendo: se actualiza en directo y se sigue escribiendo
            if (editingText != null) {
                Rectangle beforeEditing = editingText.Bounds;
                apply(editingText);
                InvalidateShape(editingText, beforeEditing);
                textEditor?.Focus();
                return;
            }
            if (selected == null) return;
            PushUndo();
            Rectangle before = selected.Bounds;
            apply(selected);
            InvalidateShape(selected, before);
        }

        private void PushUndo() => undoStack.Push(shapes.Select(s => s.Clone()).ToList());

        private void Undo() {
            CommitText();
            if (undoStack.Count == 0) return;
            shapes.Clear();
            shapes.AddRange(undoStack.Pop());
            selected = null;
            UpdateTextControls();
            Invalidate();
        }

        private void DeleteSelected() {
            if (selected == null) return;
            PushUndo();
            shapes.Remove(selected);
            InvalidateShape(selected);
            selected = null;
            UpdateTextControls();
        }

        // ───────────────────────────── Texto ─────────────────────────────

        private void BeginText(TextShape shape, bool isExisting) {
            editingText = shape;
            editingExisting = isExisting;
            if (isExisting) {
                PushUndo();
                shapes.Remove(shape);
                InvalidateShape(shape);
            }

            textEditor = new TextBox {
                Multiline = true,
                AcceptsReturn = true,
                Location = new Point(-20000, -20000),
                Size = new Size(10, 10),
                Text = shape.Text,
            };
            textEditor.TextChanged += (_, _) => {
                Rectangle before = shape.Bounds;
                shape.Text = textEditor.Text;
                ResetCaret();
                InvalidateShape(shape, before);
            };
            textEditor.KeyDown += (_, e) => {
                if (e.KeyCode == Keys.Enter && !e.Shift) {
                    e.SuppressKeyPress = true;
                    CommitText();
                } else if (e.KeyCode == Keys.Escape) {
                    e.SuppressKeyPress = true;
                    CancelText();
                }
            };
            // Flechas, Inicio, Fin…: el cursor se mueve sin cambiar el texto
            textEditor.KeyUp += (_, _) => ResetCaret();
            // Mientras se escribe, la rueda le llega al cuadro de texto
            textEditor.MouseWheel += (_, e) => WheelStyle(e);

            Controls.Add(textEditor);
            textEditor.Focus();
            textEditor.SelectionStart = textEditor.Text.Length;
            ResetCaret();
            caretTimer.Start();
            UpdateTextControls();
        }

        private void ResetCaret() {
            caretVisible = true;
            caretTimer.Stop();
            caretTimer.Start();
            InvalidateEditingText();
        }

        private void InvalidateEditingText() {
            if (editingText == null) return;
            Rectangle r = editingText.Bounds;
            r.Inflate(6, 6);
            Invalidate(r);
        }

        /// <summary>Marco discontinuo y cursor parpadeante del texto que se está escribiendo.</summary>
        private void DrawTextEditing(Graphics g, TextShape shape, int caretIndex) {
            g.SmoothingMode = SmoothingMode.None;
            using (var frame = new Pen(Color.FromArgb(180, Accent), 1) { DashStyle = DashStyle.Dash }) {
                Rectangle r = shape.Bounds;
                g.DrawRectangle(frame, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            if (!caretVisible) return;

            var (top, bottom) = shape.Caret(caretIndex);
            using var caret = new Pen(shape.Stroke, Math.Max(2f, shape.FontSize / 14f));
            g.DrawLine(caret, top, bottom);
        }

        private void CommitText() {
            if (textEditor == null || editingText == null) return;
            TextShape shape = editingText;
            string text = textEditor.Text.TrimEnd();
            CloseTextEditor();

            if (text.Length > 0) {
                if (!editingExisting) PushUndo();
                shape.Text = text;
                shapes.Add(shape);
                selected = shape;
                UpdateTextControls();
                InvalidateShape(shape);
            }
        }

        private void CancelText() {
            if (textEditor == null) return;
            CloseTextEditor();
            if (editingExisting) Undo();
        }

        private void CloseTextEditor() {
            if (textEditor == null) return;
            caretTimer.Stop();
            InvalidateEditingText();
            editingText = null;
            Controls.Remove(textEditor);
            textEditor.Dispose();
            textEditor = null;
            ActiveControl = null;
            Focus();
            UpdateTextControls();
        }

        // ───────────────────────────── Acciones ─────────────────────────────

        private void PickColor(Point p) {
            Color c = PixelAt(p);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            Clipboard.SetText(hex);
            ShowStatus($"Color {hex} (RGB {c.R}, {c.G}, {c.B}) copiado al portapapeles", c);
        }

        private Bitmap RenderSelection(bool withShapes) {
            var bmp = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(screenshot, new Rectangle(0, 0, selection.Width, selection.Height), selection, GraphicsUnit.Pixel);
            if (withShapes) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.TranslateTransform(-selection.X, -selection.Y);
                foreach (Shape s in shapes) s.Draw(g);
            }
            return bmp;
        }

        /// <summary>La imagen que se copia o guarda: con las anotaciones y, si se han configurado, esquinas redondeadas y borde.</summary>
        private Bitmap RenderOutput() {
            Bitmap image = RenderSelection(withShapes: true);
            int w = image.Width, h = image.Height;
            float radius = settings.RoundCorners ? Math.Min(settings.CornerRadius, Math.Min(w, h) / 2f) : 0;
            float border = settings.AddBorder ? Math.Min(settings.BorderWidth, Math.Min(w, h) / 2f) : 0;
            if (radius <= 0 && border <= 0) return image;

            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (image)
            using (var g = Graphics.FromImage(result))
            using (var texture = new TextureBrush(image)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Con medio píxel de desplazamiento el contorno coincide con los bordes de la imagen
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (GraphicsPath shape = RoundedRect(new RectangleF(0, 0, w, h), radius)) {
                    g.FillPath(texture, shape);
                }
                if (border > 0) {
                    // Por dentro de la imagen, siguiendo la curva de las esquinas
                    float inset = border / 2;
                    using GraphicsPath line = RoundedRect(new RectangleF(inset, inset, w - border, h - border), Math.Max(0, radius - inset));
                    using var pen = new Pen(settings.BorderColorValue, border);
                    g.DrawPath(pen, line);
                }
            }
            return result;
        }

        /// <summary>Formatos sin transparencia (JPEG, BMP y la imagen normal del portapapeles): las esquinas, en blanco.</summary>
        private static Bitmap Flatten(Bitmap image) {
            var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
            return bmp;
        }

        private bool HasTransparentCorners => settings.RoundCorners && settings.CornerRadius > 0;

        private void CopyAndClose() {
            if (phase != Phase.Editing) return;
            CommitText();
            using Bitmap bmp = RenderOutput();
            if (HasTransparentCorners) {
                // PNG para las aplicaciones que respetan la transparencia y una copia con fondo blanco para el resto
                using var png = new MemoryStream();
                bmp.Save(png, ImageFormat.Png);
                using Bitmap flat = Flatten(bmp);
                var data = new DataObject();
                data.SetData("PNG", false, png);
                data.SetImage(flat);
                Clipboard.SetDataObject(data, copy: true);
            } else {
                Clipboard.SetImage(bmp);
            }
            Close();
        }

        private void SaveAndClose() {
            if (phase != Phase.Editing) return;
            CommitText();
            using var dialog = new SaveFileDialog {
                Title = "Guardar captura",
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp",
                FileName = $"Captura_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            ImageFormat format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png,
            };
            using Bitmap bmp = RenderOutput();
            if (format.Equals(ImageFormat.Png) || !HasTransparentCorners) {
                bmp.Save(dialog.FileName, format);
            } else {
                using Bitmap flat = Flatten(bmp);
                flat.Save(dialog.FileName, format);
            }
            Close();
        }

        private async void RecognizeText() {
            if (phase != Phase.Editing || ocrRunning) return;
            CommitText();
            HideOcrPreview();
            ocrRunning = true;
            ShowStatus("Reconociendo texto…");
            try {
                // Se usa la imagen sin anotaciones para no confundir al OCR
                string text;
                using (Bitmap bmp = RenderSelection(withShapes: false)) {
                    text = await OcrService.RecognizeAsync(bmp);
                }
                if (IsDisposed) return;

                if (string.IsNullOrWhiteSpace(text)) {
                    ShowStatus("No se ha encontrado texto en la zona seleccionada");
                    return;
                }
                Clipboard.SetText(text);
                ShowStatus(null);
                ShowOcrPreview(text);
            } catch (Exception ex) {
                if (!IsDisposed) {
                    ShowStatus(null);
                    MessageBox.Show(this, ex.Message, "Error de OCR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            } finally {
                ocrRunning = false;
            }
        }

        // ───────────────────────────── Teclado ─────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (textEditor != null) return base.ProcessCmdKey(ref msg, keyData);

            if (keyData == Keys.Escape) {
                if (ocrText != null) {
                    HideOcrPreview();
                } else if (selected != null) {
                    InvalidateShape(selected);
                    selected = null;
                    UpdateTextControls();
                } else {
                    Close();
                }
                return true;
            }

            // Antes de elegir zona la lupa ya muestra el color: Ctrl+C lo copia sin pasar por el cuentagotas
            // y cierra, dejando el aviso en una ventana aparte
            if (phase == Phase.Selecting && keyData == (Keys.Control | Keys.C)) {
                if (!ClientRectangle.Contains(mouse)) return true;
                Color c = PixelAt(mouse);
                string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                Clipboard.SetText(hex);
                ColorToast.Show($"Color {hex} (RGB {c.R}, {c.G}, {c.B}) copiado al portapapeles", c, PointToScreen(mouse));
                Close();
                return true;
            }

            if (phase != Phase.Editing) return base.ProcessCmdKey(ref msg, keyData);

            switch (keyData) {
                case Keys.Control | Keys.C:
                case Keys.Enter:
                    CopyAndClose();
                    return true;
                case Keys.Control | Keys.S:
                    SaveAndClose();
                    return true;
                case Keys.Control | Keys.Z:
                    Undo();
                    return true;
                case Keys.Delete:
                case Keys.Back:
                    DeleteSelected();
                    return true;
                case Keys.A: SetTool(Tool.Arrow); return true;
                case Keys.L: SetTool(Tool.Line); return true;
                case Keys.P: SetTool(Tool.Pixelate); return true;
                case Keys.R: SetTool(Tool.Rectangle); return true;
                case Keys.E: SetTool(Tool.Ellipse); return true;
                case Keys.T: SetTool(Tool.Text); return true;
                case Keys.I: SetTool(Tool.Picker); return true;
                case Keys.O: RecognizeText(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
