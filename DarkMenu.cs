using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Menús con el aspecto de Trazo, al estilo de los de Claude o Discord: fondo oscuro, solo texto con bastante
    /// margen, filas altas, separadores y resaltado de lado a lado. En Windows 11 la ventana del menú
    /// lleva además esquinas redondeadas y un borde fino.
    /// </summary>
    internal static class DarkMenu {

        /// <summary>Margen del texto respecto al borde izquierdo del menú.</summary>
        private const int TextInset = 18;
        /// <summary>Hueco entre la primera y la última fila y los bordes de arriba y abajo del menú.</summary>
        private const int VerticalPadding = 5;

        private static int Scaled(ToolStrip strip, int logical) => (int)Math.Round(logical * strip.DeviceDpi / 96f);

        /// <summary>
        /// Menú con un poco de hueco arriba y abajo, para que el resaltado de la primera y la última fila no toque el borde.
        /// WinForms recalcula el margen de los menús al colocarlos y pisa el que se le ponga; solo respeta el margen
        /// por defecto. Sin margen a los lados: así los separadores y el resaltado van de borde a borde.
        /// </summary>
        private sealed class Strip : ContextMenuStrip {
            protected override Padding DefaultPadding {
                get {
                    int v = Scaled(this, VerticalPadding);
                    return new Padding(0, v, 0, v);
                }
            }
        }

        public static ContextMenuStrip Create() {
            var menu = new Strip {
                Renderer = new Renderer(),
                // Solo texto, sin la columna de iconos
                ShowImageMargin = false,
                ShowCheckMargin = false,
                ForeColor = SettingsForm.TextColor,
                Font = new Font("Segoe UI", 9.5f),
            };
            menu.MinimumSize = new Size(Scaled(menu, 170), 0);
            menu.HandleCreated += (_, _) => Ui.StylePopup(menu.Handle);
            menu.Disposed += (_, _) => menu.Font.Dispose();
            return menu;
        }

        /// <summary>Elemento de texto en una fila alta.</summary>
        /// <param name="trailing">Imagen al final de la fila, a la derecha (opcional); el menú la libera al cerrarse.</param>
        public static ToolStripMenuItem AddItem(ContextMenuStrip menu, string text, Action onClick, Image? trailing = null) {
            int x = Scaled(menu, TextInset), y = Scaled(menu, 7);
            int right = trailing != null ? x + TrailingSize(menu) + Scaled(menu, 12) : x;
            var item = new ToolStripMenuItem(text) {
                Padding = new Padding(x, y, right, y),
                Tag = trailing,
            };
            item.Click += (_, _) => onClick();
            if (trailing != null) item.Disposed += (_, _) => trailing.Dispose();
            menu.Items.Add(item);
            return item;
        }

        /// <summary>Lado de la imagen al final de una fila.</summary>
        public static int TrailingSize(ToolStrip strip) => Scaled(strip, 18);

        /// <summary>
        /// La fila entera de un elemento, en sus coordenadas: de borde a borde del menú. WinForms hace los elementos
        /// algo más anchos que el menú, así que sus propios límites no sirven para centrar nada.
        /// </summary>
        private static Rectangle RowBounds(ToolStripItem item) {
            int width = item.Owner?.ClientSize.Width ?? item.Width;
            return new Rectangle(-item.Bounds.X, 0, width, item.Height);
        }

        /// <summary>Línea de separación de lado a lado, con aire arriba y abajo.</summary>
        public static void AddSeparator(ContextMenuStrip menu) {
            int y = Scaled(menu, 4);
            menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, y, 0, y) });
        }

        /// <summary>
        /// Da el aspecto de Trazo a un desplegable que ya existe (los de la barra de la captura): fondo oscuro,
        /// texto e iconos claros, filas algo más altas y resaltado de lado a lado. Conserva iconos y marcas.
        /// </summary>
        public static void Style(ToolStripDropDownItem button) {
            // El desplegable que WinForms crea solo usa siempre el renderizador de la barra: se cambia por uno
            // propio y se le pasan los elementos
            var dropDown = new ToolStripDropDownMenu {
                Renderer = new DropDownRenderer(),
                Font = button.Font,
                ImageScalingSize = button.Owner?.ImageScalingSize ?? new Size(16, 16),
            };
            ToolStripItem[] items = button.DropDownItems.Cast<ToolStripItem>().ToArray();
            dropDown.Items.AddRange(items);
            button.DropDown = dropDown;
            dropDown.BackColor = SettingsForm.Back;
            dropDown.ForeColor = SettingsForm.TextColor;
            int y = Scaled(dropDown, 3);
            foreach (ToolStripItem item in dropDown.Items) {
                if (item is ToolStripMenuItem) item.Padding = new Padding(item.Padding.Left, y, Scaled(dropDown, 10), y);
            }
            if (dropDown.IsHandleCreated) Ui.StylePopup(dropDown.Handle);
            else dropDown.HandleCreated += (_, _) => Ui.StylePopup(dropDown.Handle);
        }

        /// <summary>Lo común a todos los menús: fondo oscuro, resaltado y separadores de borde a borde.</summary>
        private abstract class BaseRenderer : ToolStripProfessionalRenderer {

            protected BaseRenderer() : base(new Colors()) {
                RoundedEdges = false;
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
                using var back = new SolidBrush(SettingsForm.Back);
                e.Graphics.FillRectangle(back, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) =>
                Ui.DrawWindows10Border(e.Graphics, e.ToolStrip.Size);

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {
                // Sin la franja gris de los iconos: todo el menú del mismo color
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
                if (!e.Item.Selected || !e.Item.Enabled || e.Item.Owner == null) return;
                using var brush = new SolidBrush(SettingsForm.SurfaceHover);
                e.Graphics.FillRectangle(brush, RowBounds(e.Item));
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
                Rectangle row = RowBounds(e.Item);
                int y = e.Item.Height / 2;
                using var pen = new Pen(SettingsForm.Edge);
                e.Graphics.DrawLine(pen, row.Left, y, row.Right, y);
            }

            protected static Color TextColor(ToolStripItem? item) => item?.Enabled == false ? SettingsForm.SubText : SettingsForm.TextColor;
        }

        /// <summary>Menú de solo texto (el de la bandeja), con una imagen opcional al final de la fila.</summary>
        private sealed class Renderer : BaseRenderer {

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
                if (e.Item.Owner == null) { base.OnRenderItemText(e); return; }
                // Siempre a la misma distancia del borde, sea cual sea el margen que calcule WinForms
                Rectangle row = RowBounds(e.Item);
                int inset = Scaled(e.Item.Owner, TextInset);
                var r = Rectangle.FromLTRB(row.X + inset, 0, row.Right - inset, row.Height);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, TextColor(e.Item),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

                if (e.Item.Tag is Image image) {
                    int size = TrailingSize(e.Item.Owner);
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(image, new Rectangle(row.Right - inset - size, (row.Height - size) / 2, size, size));
                }
            }
        }

        /// <summary>
        /// Menú con iconos, marcas y submenús (los de la barra de la captura): lo elegido se señala con un fondo
        /// gris claro detrás del icono o, si no tiene, con una marca dibujada en claro.
        /// </summary>
        private sealed class DropDownRenderer : BaseRenderer {

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
                e.TextColor = TextColor(e.Item);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
                e.ArrowColor = TextColor(e.Item);
                base.OnRenderArrow(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = e.ImageRectangle;
                r.Inflate(2, 2);
                float scale = (e.Item.Owner?.DeviceDpi ?? 96) / 96f;
                using (GraphicsPath path = Ui.RoundedRect(r, (int)Math.Round(4 * scale)))
                using (var back = new SolidBrush(SettingsForm.Accent)) {
                    g.FillPath(back, path);
                }
                if (e.Item.Image is Image image) {
                    // El icono va encima del fondo; por eso OnRenderItemImage no lo dibuja en los elementos marcados
                    g.DrawImage(image, e.ImageRectangle);
                    return;
                }
                float w = r.Width, h = r.Height;
                var check = new[] {
                    new PointF(r.X + w * 0.28f, r.Y + h * 0.52f),
                    new PointF(r.X + w * 0.44f, r.Y + h * 0.68f),
                    new PointF(r.X + w * 0.73f, r.Y + h * 0.34f),
                };
                using var pen = new Pen(Color.White, 1.8f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, check);
            }

            protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) {
                if (e.Item is ToolStripMenuItem { Checked: true }) return;
                base.OnRenderItemImage(e);
            }
        }

        private sealed class Colors : ProfessionalColorTable {
            public override Color ToolStripDropDownBackground => SettingsForm.Back;
            public override Color MenuBorder => SettingsForm.Edge;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => SettingsForm.SurfaceHover;
            public override Color SeparatorDark => SettingsForm.Edge;
            public override Color SeparatorLight => SettingsForm.Edge;
        }
    }
}
