using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Trazo {

    /// <summary>
    /// Barra personalizable. Con los tres puntos del final la barra entra en modo edición y debajo aparece otra,
    /// más apagada, con los botones que no están. Se arrastran de una a otra o se ordenan, y un clic los quita o los
    /// añade. Mientras dura la edición las dos barras se pintan sobre la captura, sin la barra real.
    /// </summary>
    internal sealed partial class CaptureOverlay {

        /// <summary>Todos los botones de la barra por su clave de <see cref="ToolbarLayout"/>, también los ocultos.</summary>
        private readonly Dictionary<string, ToolStripItem> toolbarItems = new();
        private ToolbarLayout toolbarLayout;
        /// <summary>Los tres puntos del final: entran y salen del modo edición.</summary>
        private ToolStripButton customizeButton = null!;

        private enum EditZone { Bar, Hidden, Tools }

        /// <summary>Un botón en modo edición: su posición en la lista de su zona (las herramientas no tienen) y dónde está.</summary>
        private sealed class EditTile {
            public EditTile(string key, EditZone zone, int index, Rectangle rect) {
                Key = key;
                Zone = zone;
                Index = index;
                Rect = rect;
            }
            public string Key { get; }
            public EditZone Zone { get; }
            public int Index { get; }
            public Rectangle Rect { get; }
        }

        // Botones propios del modo edición: los tres puntos, y al final de la barra de abajo añadir separador,
        // restablecer y terminar
        private const string MoreKey = "more", AddSeparatorKey = "+separator", ResetKey = "reset", DoneKey = "done";
        private const string EmptyHint = "Arrastra aquí lo que quieras quitar";

        private bool customizing;
        private readonly List<EditTile> editTiles = new();
        private Rectangle editBar, hiddenBar, emptyHintRect;
        private int hiddenDividerX;
        private Bitmap? hiddenBackdrop, resetIcon, doneIcon;
        /// <summary>La barra de abajo ya pintada, para moverla y fundirla en cada fotograma de la animación sin volver a dibujarla.</summary>
        private Bitmap? revealLayer;
        private EditTile? editHover, editPressed;
        private Point editPressPoint, editDragPoint;
        private bool editDragging;
        private (EditZone Zone, int Index)? editDrop;
        // Animación de entrada y salida: 0 = barra normal, 1 = modo edición del todo
        private float editReveal, revealFrom, revealTo;
        private bool editClosing;
        private readonly System.Diagnostics.Stopwatch revealClock = new();
        private volatile bool revealRunning;
        private int revealFramePending;
        // Dónde está cada botón en la barra real (por su posición en ToolbarLayout.Visible), los tres puntos y la propia
        // barra: de ahí salen los botones al entrar en la edición y ahí vuelven al salir
        private readonly Dictionary<int, Rectangle> realTiles = new();
        private Rectangle realMore, realBar;

        private int S(float logical) => (int)Math.Round(logical * scale);

        /// <summary>Coloca en la barra los botones elegidos, en su orden, y al final los tres puntos.</summary>
        private void ApplyToolbarLayout() {
            toolbar.SuspendLayout();
            foreach (ToolStripSeparator separator in toolbar.Items.OfType<ToolStripSeparator>().ToList()) separator.Dispose();
            toolbar.Items.Clear();
            foreach (int i in toolbarLayout.ShownIndices()) {
                string key = toolbarLayout.Visible[i];
                toolbar.Items.Add(key == ToolbarLayout.Separator ? new ToolStripSeparator() : toolbarItems[key]);
            }
            toolbar.Items.Add(customizeButton);
            toolbar.ResumeLayout();
        }

        /// <summary>Coloca la barra real (aunque esté oculta) con los botones de ahora y apunta dónde queda cada uno.</summary>
        private void MeasureRealToolbar() {
            ApplyToolbarLayout();
            toolbar.Bounds = ToolbarBounds(toolbar.PreferredSize);
            toolbar.PerformLayout();
            realBar = toolbar.Bounds;

            // Con sus márgenes, como los botones del modo edición
            Rectangle Outer(ToolStripItem item) {
                Rectangle b = item.Bounds;
                b.Offset(toolbar.Location);
                return Rectangle.FromLTRB(b.Left - item.Margin.Left, b.Top, b.Right + item.Margin.Right, b.Bottom);
            }
            realTiles.Clear();
            int k = 0;
            foreach (int i in toolbarLayout.ShownIndices()) realTiles[i] = Outer(toolbar.Items[k++]);
            realMore = Outer(customizeButton);
        }

        /// <summary>El fondo difuminado de la barra en edición cubre también lo que ocupa la real mientras cambia de tamaño.</summary>
        private Rectangle EditBackdropRect => Rectangle.Union(realBar, editBar);

        private void StartCustomizing() {
            HideTip();
            CommitText();
            customizing = true;
            resetIcon = Icons.Create(IconKind.Update, iconSize, Icons.LightInk);
            doneIcon = Icons.Create(IconKind.Check, iconSize, Icons.LightInk);
            editClosing = false;
            MeasureRealToolbar();
            toolbar.Visible = false;
            LayoutCustomizer();
            Reveal(1);
        }

        /// <summary>Termina la edición: la barra de abajo se recoge y después vuelve la barra real.</summary>
        private void EndCustomizing() {
            if (editClosing) return;
            HideTip();
            editClosing = true;
            editHover = editPressed = null;
            editDragging = false;
            editDrop = null;
            Cursor = Cursors.Default;
            // Los botones vuelven a donde quedarán en la barra real, ya con los cambios
            MeasureRealToolbar();
            toolbarBackdrop?.Dispose();
            toolbarBackdrop = CreateBlurredBackdrop(EditBackdropRect);
            Reveal(0);
        }

        private const int RevealMs = 280;

        /// <summary>Anima la entrada (1) o la salida (0) desde donde esté, aunque se invierta a medias.</summary>
        private void Reveal(float to) {
            revealFrom = editReveal;
            revealTo = to;
            revealClock.Restart();
            if (revealRunning) return;
            // Un fotograma por cada refresco de la pantalla (los temporizadores de Windows no bajan de unos 15 ms,
            // muy poco para pantallas de 120 Hz o más): un hilo espera al compositor y pide cada fotograma a la ventana
            revealRunning = true;
            new Thread(RevealLoop) { IsBackground = true, Name = "Trazo: animación de la barra" }.Start();
        }

        private void RevealLoop() {
            while (revealRunning) {
                // Sin composición de escritorio DwmFlush no espera: entonces, unos 120 fotogramas por segundo
                if (NativeMethods.DwmFlush() != 0) Thread.Sleep(8);
                if (!revealRunning || Interlocked.Exchange(ref revealFramePending, 1) == 1) continue;
                try {
                    BeginInvoke(new Action(RevealTick));
                } catch (InvalidOperationException) {
                    return;  // La ventana ya se ha cerrado
                }
            }
        }

        private void StopReveal() {
            revealRunning = false;
            revealLayer?.Dispose();
            revealLayer = null;
        }

        private void RevealTick() {
            Volatile.Write(ref revealFramePending, 0);
            if (!revealRunning) return;
            double duration = Math.Max(1, RevealMs * Math.Abs(revealTo - revealFrom));
            double p = Math.Min(1, revealClock.Elapsed.TotalMilliseconds / duration);
            double eased = 1 - Math.Pow(1 - p, 3);
            editReveal = (float)(revealFrom + (revealTo - revealFrom) * eased);
            Invalidate(CustomizerBounds);
            // Se pinta ya, sin esperar a que Windows lo haga cuando le venga bien: el movimiento sale más regular
            Update();
            if (p < 1) return;
            StopReveal();
            if (revealTo == 0) FinishCustomizing();
        }

        private void FinishCustomizing() {
            Invalidate(CustomizerBounds);
            customizing = false;
            editClosing = false;
            DisposeCustomizerImages();
            ApplyToolbarLayout();
            PositionToolbar();
            toolbar.Visible = true;
            PlaceMoveHandle();
            Cursor = ToolCursor;
        }

        private void DisposeCustomizerImages() {
            StopReveal();
            hiddenBackdrop?.Dispose();
            resetIcon?.Dispose();
            doneIcon?.Dispose();
            hiddenBackdrop = resetIcon = doneIcon = null;
        }

        /// <summary>Cada cambio se guarda y se ve al momento.</summary>
        private void CustomizedLayoutChanged() {
            toolbarLayout.Save(settings);
            settingsChanged = true;
            editHover = null;
            HideTip();
            // Si cambia mientras aún se abre, las posiciones de partida ya no corresponden: esos botones solo aparecen
            realTiles.Clear();
            LayoutCustomizer();
        }

        // ───────────────────────────── Disposición ─────────────────────────────

        /// <summary>
        /// Todo lo del modo edición, con margen para los bordes suavizados. Los rectángulos aún vacíos (antes de colocar
        /// nada) no cuentan: unidos a los demás, la zona llegaría hasta la esquina de la pantalla.
        /// </summary>
        private Rectangle CustomizerBounds {
            get {
                Rectangle bounds = Rectangle.Empty;
                foreach (Rectangle r in new[] { editBar, hiddenBar, realBar }) {
                    if (!r.IsEmpty) bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
                }
                return Rectangle.Inflate(bounds, 2, 2);
            }
        }

        private int TileWidth(string key) => key switch {
            ToolbarLayout.Separator => S(9),
            // Como los botones de icono de la barra: relleno de 4 a cada lado y margen de 1
            AddSeparatorKey or ResetKey or DoneKey => iconSize + S(8) + 2,
            MoreKey => ItemWidth(customizeButton),
            _ => ItemWidth(toolbarItems[key]),
        };

        private static int ItemWidth(ToolStripItem item) => item.GetPreferredSize(Size.Empty).Width + item.Margin.Horizontal;

        /// <summary>
        /// La barra en edición ocupa el sitio de la real, con todos sus separadores; la de los ocultos va debajo,
        /// alineada a su derecha (o encima si no cabe), con añadir separador, restablecer y terminar al final.
        /// </summary>
        private void LayoutCustomizer() {
            Invalidate(CustomizerBounds);
            editTiles.Clear();
            Padding pad = toolbar.Padding;
            int height = toolbar.Height;

            var bar = toolbarLayout.Visible.Select((key, i) => (Key: key, Zone: EditZone.Bar, Index: i)).Append((Key: MoreKey, Zone: EditZone.Tools, Index: -1)).ToList();
            editBar = ToolbarBounds(new Size(pad.Horizontal + bar.Sum(t => TileWidth(t.Key)), height));
            AddTiles(bar, editBar.X + pad.Left, editBar.Y + pad.Top, height - pad.Vertical);

            var hidden = toolbarLayout.Hidden.Select((key, i) => (Key: key, Zone: EditZone.Hidden, Index: i)).ToList();
            var tools = new[] { AddSeparatorKey, ResetKey, DoneKey }.Select(key => (Key: key, Zone: EditZone.Tools, Index: -1)).ToList();
            int gap = S(6), divider = S(9);
            int hiddenContent = hidden.Count > 0 ? hidden.Sum(t => TileWidth(t.Key)) : TextRenderer.MeasureText(EmptyHint, cards.Label).Width + S(16);
            int width = pad.Horizontal + hiddenContent + divider + tools.Sum(t => TileWidth(t.Key));
            Rectangle area = ScreenAreaFor(selection);
            int y = editBar.Bottom + gap + height <= area.Bottom ? editBar.Bottom + gap : editBar.Top - gap - height;
            int x = MathUtil.Clamp(editBar.Right - width, area.Left, area.Right - width);
            hiddenBar = new Rectangle(x, y, width, height);

            int top = hiddenBar.Y + pad.Top, itemHeight = height - pad.Vertical;
            int left = hiddenBar.X + pad.Left;
            AddTiles(hidden, left, top, itemHeight);
            emptyHintRect = new Rectangle(left, top, hiddenContent, itemHeight);
            hiddenDividerX = left + hiddenContent + divider / 2;
            AddTiles(tools, left + hiddenContent + divider, top, itemHeight);

            toolbarBackdrop?.Dispose();
            toolbarBackdrop = CreateBlurredBackdrop(EditBackdropRect);
            hiddenBackdrop?.Dispose();
            hiddenBackdrop = CreateBlurredBackdrop(hiddenBar);
            revealLayer?.Dispose();
            revealLayer = null;
            Invalidate(CustomizerBounds);
        }

        private void AddTiles(IEnumerable<(string Key, EditZone Zone, int Index)> tiles, int x, int top, int height) {
            foreach ((string key, EditZone zone, int index) in tiles) {
                int width = TileWidth(key);
                editTiles.Add(new EditTile(key, zone, index, new Rectangle(x, top, width, height)));
                x += width;
            }
        }

        // ───────────────────────────── Ratón y teclado ─────────────────────────────

        private EditTile? EditTileAt(Point p) => editTiles.FirstOrDefault(t => t.Rect.Contains(p));

        private List<string> EditList(EditZone zone) => zone == EditZone.Bar ? toolbarLayout.Visible : toolbarLayout.Hidden;

        private static bool IsDraggable(EditTile tile) => tile.Zone != EditZone.Tools || tile.Key == AddSeparatorKey;

        /// <summary>Esc o Intro terminan; el resto de teclas no hace nada mientras se edita la barra.</summary>
        private bool CustomizeKey(Keys keyData) {
            if (keyData is Keys.Escape or Keys.Enter) EndCustomizing();
            return true;
        }

        private void CustomizeMouseDown(MouseEventArgs e) {
            // El segundo clic de un doble clic caería en el botón que ha ocupado el sitio del primero
            if (editClosing || e.Button != MouseButtons.Left || e.Clicks > 1) return;
            // Un clic fuera de las dos barras termina la edición
            if (!editBar.Contains(e.Location) && !hiddenBar.Contains(e.Location)) {
                EndCustomizing();
                return;
            }
            HideTip();
            editPressed = EditTileAt(e.Location);
            editPressPoint = editDragPoint = e.Location;
            if (editPressed != null) Invalidate(editPressed.Rect);
        }

        private void CustomizeMouseMove(MouseEventArgs e) {
            if (editClosing) return;
            if (editPressed is not EditTile pressed) {
                SetEditHover(EditTileAt(e.Location));
                return;
            }
            if (!editDragging) {
                Size drag = SystemInformation.DragSize;
                bool moved = Math.Abs(e.X - editPressPoint.X) > drag.Width || Math.Abs(e.Y - editPressPoint.Y) > drag.Height;
                if (!moved || !IsDraggable(pressed)) return;
                editDragging = true;
                SetEditHover(null);
            }
            Invalidate(GhostRect);
            editDragPoint = e.Location;
            editDrop = EditDropTarget(e.Location);
            Cursor = editDrop == null ? Cursors.No : Cursors.Hand;
            Invalidate(CustomizerBounds);
            Invalidate(GhostRect);
        }

        private void CustomizeMouseUp(MouseEventArgs e) {
            if (editClosing) return;
            if (editPressed is not EditTile tile) return;
            bool dragged = editDragging;
            (EditZone Zone, int Index)? target = editDrop;
            Invalidate(GhostRect);
            editPressed = null;
            editDragging = false;
            editDrop = null;
            Cursor = Cursors.Default;

            if (dragged) {
                if (target is { } t) MoveEditTile(tile, t.Zone, t.Index);
                else LayoutCustomizer();
                return;
            }
            Invalidate(tile.Rect);
            if (EditTileAt(e.Location) != tile) return;
            switch (tile.Key) {
                case MoreKey or DoneKey:
                    EndCustomizing();
                    break;
                case ResetKey:
                    toolbarLayout = ToolbarLayout.Default;
                    CustomizedLayoutChanged();
                    break;
                default:
                    // Un clic quita el botón de la barra o lo añade al final; el separador nuevo también va al final
                    MoveEditTile(tile, tile.Zone == EditZone.Bar ? EditZone.Hidden : EditZone.Bar, int.MaxValue);
                    break;
            }
        }

        /// <summary>Lleva un botón a otra posición o barra. Los separadores no se ocultan: llevados abajo, desaparecen.</summary>
        private void MoveEditTile(EditTile tile, EditZone zone, int index) {
            string key = tile.Key == AddSeparatorKey ? ToolbarLayout.Separator : tile.Key;
            if (tile.Zone != EditZone.Tools) {
                if (zone == tile.Zone && index > tile.Index) index--;
                EditList(tile.Zone).RemoveAt(tile.Index);
            }
            if (zone == EditZone.Bar || key != ToolbarLayout.Separator) {
                List<string> to = EditList(zone);
                to.Insert(Math.Min(index, to.Count), key);
            }
            CustomizedLayoutChanged();
        }

        /// <summary>Barra y posición en las que caería el botón arrastrado: la barra más cercana al cursor, si está cerca.</summary>
        private (EditZone Zone, int Index)? EditDropTarget(Point p) {
            int reach = S(16);
            EditZone? zone = null;
            int best = int.MaxValue;
            foreach ((EditZone z, Rectangle r) in new[] { (EditZone.Bar, editBar), (EditZone.Hidden, hiddenBar) }) {
                if (!Rectangle.Inflate(r, reach, reach).Contains(p)) continue;
                int distance = Math.Abs(p.Y - (r.Top + r.Height / 2));
                if (distance < best) (zone, best) = (z, distance);
            }
            if (zone is not EditZone found) return null;
            return (found, editTiles.Count(t => t.Zone == found && t.Rect.X + t.Rect.Width / 2 < p.X));
        }

        private void SetEditHover(EditTile? tile) {
            if (tile == editHover) return;
            if (editHover != null) Invalidate(editHover.Rect);
            editHover = tile;
            if (tile != null) Invalidate(tile.Rect);
            Cursor = tile != null ? Cursors.Hand : Cursors.Default;
            if (tile == null) HideTip();
            else ShowTip(EditTipText(tile), EditTipAnchor(tile, out bool below), below);
        }

        /// <summary>La misma descripción que en la barra; los botones propios del modo edición tienen la suya.</summary>
        private string EditTipText(EditTile tile) => tile.Key switch {
            ToolbarLayout.Separator => "Separador\nArrástralo abajo o haz clic para quitarlo",
            AddSeparatorKey => "Añadir separador\nArrástralo a su sitio en la barra",
            ResetKey => "Restablecer\nVuelve a la barra de siempre",
            MoreKey or DoneKey => "Terminar de personalizar (Esc)",
            _ => toolbarItems[tile.Key].ToolTipText,
        };

        /// <summary>El botón con la altura de su barra; la descripción va hacia fuera, lejos de la otra barra.</summary>
        private Rectangle EditTipAnchor(EditTile tile, out bool below) {
            bool inEditBar = tile.Zone == EditZone.Bar || tile.Key == MoreKey;
            Rectangle bar = inEditBar ? editBar : hiddenBar;
            Rectangle other = inEditBar ? hiddenBar : editBar;
            below = bar.Y > other.Y;
            return new Rectangle(tile.Rect.X, bar.Y, tile.Rect.Width, bar.Height);
        }

        /// <summary>El botón arrastrado, que sigue al cursor.</summary>
        private Rectangle GhostRect {
            get {
                if (!editDragging || editPressed == null) return Rectangle.Empty;
                Rectangle r = editPressed.Rect;
                r.Offset(editDragPoint.X - editPressPoint.X, editDragPoint.Y - editPressPoint.Y);
                r.Inflate(2, 2);
                return r;
            }
        }

        // ───────────────────────────── Dibujo ─────────────────────────────

        private void DrawCustomizer(Graphics g) {
            DrawRevealingLayer(g);

            // La barra en edición, con un borde claro que la marca (aparece poco a poco con la animación)
            float t = editReveal;
            Color edgeColor = Color.FromArgb(Mix(GlassEdge.A, SettingsForm.AccentLine.A, t), Mix(GlassEdge.R, SettingsForm.AccentLine.R, t),
                                             Mix(GlassEdge.G, SettingsForm.AccentLine.G, t), Mix(GlassEdge.B, SettingsForm.AccentLine.B, t));
            // La barra pasa del tamaño de la real al de la edición, y sus botones de su sitio en una al de la otra
            using (var marked = new Pen(edgeColor, (1 + 0.5f * t) * scale)) {
                DrawGlass(g, Lerp(realBar, editBar, t), toolbarBackdrop, 205, marked, EditBackdropRect.Location);
            }
            DrawEditTiles(g, bar: true);

            if (editDragging && editPressed != null) {
                if (editDrop is { } drop) DrawDropCaret(g, drop.Zone, drop.Index);
                Rectangle ghost = GhostRect;
                ghost.Inflate(-2, -2);
                using (GraphicsPath path = Ui.RoundedRect(ghost, S(6)))
                using (var back = new SolidBrush(CardBack)) {
                    g.FillPath(back, path);
                }
                DrawEditTile(g, ghost, editPressed.Key, 60, 1f);
            }
        }

        /// <summary>
        /// La barra de los ocultos. Mientras se anima la entrada o la salida sale de detrás de la barra en edición con
        /// un fundido: se pinta una vez en una imagen transparente que luego solo se mueve, con precisión de subpíxel.
        /// </summary>
        private void DrawRevealingLayer(Graphics g) {
            float t = editReveal;
            if (t >= 1) {
                DrawHiddenLayer(g);
                return;
            }
            if (t <= 0) return;
            Rectangle layer = Rectangle.Inflate(hiddenBar, 2, 2);
            if (revealLayer == null) {
                revealLayer = new Bitmap(layer.Width, layer.Height, PixelFormat.Format32bppPArgb);
                using Graphics lg = Graphics.FromImage(revealLayer);
                lg.TranslateTransform(-layer.X, -layer.Y);
                DrawHiddenLayer(lg);
            }
            float dy = (editBar.Y - hiddenBar.Y) * (1 - t);
            GraphicsState state = g.Save();
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var attrs = new ImageAttributes()) {
                attrs.SetColorMatrix(new ColorMatrix { Matrix33 = t });
                var corners = new[] { new PointF(layer.X, layer.Y + dy), new PointF(layer.Right, layer.Y + dy), new PointF(layer.X, layer.Bottom + dy) };
                g.DrawImage(revealLayer, corners, new RectangleF(0, 0, layer.Width, layer.Height), GraphicsUnit.Pixel, attrs);
            }
            g.Restore(state);
        }

        /// <summary>Barra de los ocultos, más opaca y con los iconos apagados.</summary>
        private void DrawHiddenLayer(Graphics g) {
            using (var edge = new Pen(GlassEdge, 1f)) DrawGlass(g, hiddenBar, hiddenBackdrop, 240, edge);
            if (toolbarLayout.Hidden.Count == 0) {
                // Con GDI+ y no con TextRenderer: también se ve bien en la imagen transparente de la animación
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                using var brush = new SolidBrush(Color.FromArgb(140, 140, 146));
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(EmptyHint, cards.Label, brush, emptyHintRect, format);
            }
            DrawSeparatorLine(g, hiddenDividerX, hiddenBar.Y, hiddenBar.Height, 1f);
            DrawEditTiles(g, bar: false);
        }

        /// <summary>Los botones de la barra en edición (con los tres puntos) o los de la barra de abajo.</summary>
        private void DrawEditTiles(Graphics g, bool bar) {
            foreach (EditTile tile in editTiles) {
                bool inBar = tile.Zone == EditZone.Bar || tile.Key == MoreKey;
                if (inBar != bar) continue;
                // El que se arrastra deja su hueco vacío (salvo el de añadir separador, que es una fuente)
                if (editDragging && tile == editPressed && tile.Key != AddSeparatorKey) continue;
                int highlight = tile == editPressed && !editDragging ? 75 : tile == editHover ? 35 : 0;
                float opacity = tile.Zone == EditZone.Hidden && tile != editHover ? 0.45f : 1f;
                Rectangle rect = tile.Rect;
                if (bar && editReveal < 1) {
                    // Durante la animación cada botón va desde su sitio en la barra real; los separadores que allí
                    // no se pintan aparecen con un fundido
                    if (RealTileRect(tile) is Rectangle from) rect = Lerp(from, tile.Rect, editReveal);
                    else opacity *= editReveal;
                }
                DrawEditTile(g, rect, tile.Key, highlight, opacity);
            }
        }

        private Rectangle? RealTileRect(EditTile tile) =>
            tile.Key == MoreKey ? realMore : realTiles.TryGetValue(tile.Index, out Rectangle r) ? r : null;

        /// <summary>Punto intermedio de la animación entre <paramref name="from"/> (t = 0) y <paramref name="to"/> (t = 1).</summary>
        private static int Mix(int from, int to, float t) => from + (int)Math.Round((to - from) * t);

        private static Rectangle Lerp(Rectangle a, Rectangle b, float t) =>
            Rectangle.FromLTRB(Mix(a.Left, b.Left, t), Mix(a.Top, b.Top, t), Mix(a.Right, b.Right, t), Mix(a.Bottom, b.Bottom, t));

        private void DrawEditTile(Graphics g, Rectangle r, string key, int highlight, float opacity) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (highlight > 0) {
                using GraphicsPath path = Ui.RoundedRect(new Rectangle(r.X + 1, r.Y, r.Width - 3, r.Height - 1), S(6));
                using var brush = new SolidBrush(Color.FromArgb(highlight, 255, 255, 255));
                g.FillPath(brush, path);
            }

            if (key == ToolbarLayout.Separator) {
                DrawSeparatorLine(g, r.X + r.Width / 2, r.Y, r.Height, opacity);
                return;
            }
            if (key == AddSeparatorKey) {
                // Una línea de separador con un + al lado
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, arm = S(3);
                DrawSeparatorLine(g, cx - S(4), r.Y, r.Height, 2.5f);
                using var pen = new Pen(Icons.LightInk, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, cx + S(4) - arm, cy, cx + S(4) + arm, cy);
                g.DrawLine(pen, cx + S(4), cy - arm, cx + S(4), cy + arm);
                return;
            }

            toolbarItems.TryGetValue(key, out ToolStripItem? item);
            Image? image = key switch {
                MoreKey => customizeButton.Image,
                ResetKey => resetIcon,
                DoneKey => doneIcon,
                _ => item?.Image,
            };
            // Los botones con desplegable llevan su flechita, como en la barra
            int arrow = item is ToolStripDropDownItem ? S(9) : 0;
            if (image != null) {
                var dest = new Rectangle(r.X + (r.Width - arrow - image.Width) / 2, r.Y + (r.Height - image.Height) / 2, image.Width, image.Height);
                DrawFaded(g, image, dest, opacity);
            }
            if (arrow > 0) {
                float ax = r.Right - arrow / 2f - S(2), ay = r.Y + r.Height / 2f, w = S(2.5f);
                using var brush = new SolidBrush(Color.FromArgb((int)(255 * opacity), Icons.LightInk));
                g.FillPolygon(brush, new[] { new PointF(ax - w, ay - w / 2), new PointF(ax + w, ay - w / 2), new PointF(ax, ay + w / 1.2f) });
            }
        }

        private static void DrawFaded(Graphics g, Image image, Rectangle dest, float opacity) {
            if (opacity >= 1) {
                g.DrawImage(image, dest);
                return;
            }
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
            g.DrawImage(image, dest, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
        }

        /// <summary>Línea vertical de separador, como las de la barra (más intensa con <paramref name="strength"/> &gt; 1).</summary>
        private static void DrawSeparatorLine(Graphics g, int x, int top, int height, float strength) {
            int margin = height / 4;
            using var pen = new Pen(Color.FromArgb((int)Math.Min(255, 70 * strength), 255, 255, 255), 1f);
            g.DrawLine(pen, x, top + margin, x, top + height - margin);
        }

        /// <summary>Línea clara donde caerá el botón arrastrado.</summary>
        private void DrawDropCaret(Graphics g, EditZone zone, int index) {
            List<EditTile> tiles = editTiles.Where(t => t.Zone == zone).ToList();
            Rectangle bar = zone == EditZone.Bar ? editBar : hiddenBar;
            int x = index < tiles.Count ? tiles[index].Rect.Left
                : tiles.Count > 0 ? tiles[tiles.Count - 1].Rect.Right
                : bar.X + toolbar.Padding.Left;
            int margin = S(7);
            using var pen = new Pen(Icons.LightInk, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, x, bar.Top + margin, x, bar.Bottom - margin);
        }
    }
}
