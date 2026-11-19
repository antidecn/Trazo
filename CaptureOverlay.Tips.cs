using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Trazo {

    /// <summary>
    /// Descripciones de los botones de las barras con el aspecto de Trazo, en vez de las de Windows: una tarjeta oscura
    /// junto a la barra, con la primera línea en blanco y el resto en gris. Aparecen tras un momento con el cursor
    /// encima y, si acababa de verse otra, al instante. Se pintan sobre la captura, así que nunca se quedan colgadas.
    /// </summary>
    internal sealed partial class CaptureOverlay {

        private const int TipDelayMs = 450, TipWarmMs = 400;
        private static readonly Color TipSubText = Color.FromArgb(170, 170, 176);

        private readonly System.Windows.Forms.Timer tipTimer = new() { Interval = TipDelayMs };
        private readonly Stopwatch sinceTipHidden = new();
        private string? tipText;
        /// <summary>El botón, con la altura de su barra: la descripción va encima o debajo, sin taparla.</summary>
        private Rectangle tipAnchor;
        private bool tipBelow;
        /// <summary>Dónde se ve la descripción; vacío si no se ve.</summary>
        private Rectangle tipRect;

        /// <param name="below">Si va debajo de la barra (o encima); si no cabe en la pantalla, al otro lado.</param>
        private void ShowTip(string? text, Rectangle anchor, bool below) {
            if (string.IsNullOrEmpty(text)) {
                HideTip();
                return;
            }
            if (text == tipText && anchor == tipAnchor) return;
            bool warm = !tipRect.IsEmpty || (sinceTipHidden.IsRunning && sinceTipHidden.ElapsedMilliseconds < TipWarmMs);
            HideTip();
            (tipText, tipAnchor, tipBelow) = (text, anchor, below);
            if (warm) PlaceTip();
            else tipTimer.Start();
        }

        private void HideTip() {
            tipTimer.Stop();
            tipText = null;
            if (tipRect.IsEmpty) return;
            Invalidate(Rectangle.Inflate(tipRect, 2, 2));
            tipRect = Rectangle.Empty;
            sinceTipHidden.Restart();
        }

        private int TipLineHeight => TextRenderer.MeasureText("Ag", cards.Label).Height;

        private void PlaceTip() {
            tipTimer.Stop();
            if (tipText == null) return;
            string[] lines = tipText.Split('\n');
            int lineHeight = TipLineHeight;
            int width = lines.Max(l => TextRenderer.MeasureText(l, cards.Label).Width);
            var size = new Size(width + S(16), lines.Length * lineHeight + S(12));

            Rectangle area = ScreenAreaFor(tipAnchor);
            int gap = S(6);
            int below = tipAnchor.Bottom + gap, above = tipAnchor.Top - gap - size.Height;
            int y = tipBelow ? (below + size.Height <= area.Bottom ? below : above) : (above >= area.Top ? above : below);
            int x = MathUtil.Clamp(tipAnchor.X + (tipAnchor.Width - size.Width) / 2, area.Left, area.Right - size.Width);
            tipRect = new Rectangle(new Point(x, y), size);
            Invalidate(Rectangle.Inflate(tipRect, 2, 2));
        }

        private void DrawTip(Graphics g) {
            if (tipRect.IsEmpty || tipText == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inner = new RectangleF(tipRect.X + 0.5f, tipRect.Y + 0.5f, tipRect.Width - 1, tipRect.Height - 1);
            using (GraphicsPath path = Ui.RoundedRect(inner, S(6)))
            using (var back = new SolidBrush(CardBack))
            using (var edge = new Pen(GlassEdge, 1f)) {
                g.FillPath(back, path);
                g.DrawPath(edge, path);
            }
            string[] lines = tipText.Split('\n');
            int lineHeight = TipLineHeight;
            int y = tipRect.Y + S(6);
            for (int i = 0; i < lines.Length; i++, y += lineHeight) {
                var r = new Rectangle(tipRect.X + S(8), y, tipRect.Width - S(16), lineHeight);
                TextRenderer.DrawText(g, lines[i], cards.Label, r, i == 0 ? Color.White : TipSubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        /// <summary>Descripción de un botón de la barra real al pasar el ratón; desaparece al salir o al pulsarlo.</summary>
        private void AttachTip(ToolStripItem item) {
            item.MouseEnter += (_, _) => {
                // Con un desplegable abierto quedaría detrás de él (Windows tampoco las muestra entonces)
                if (toolbar.Items.OfType<ToolStripDropDownItem>().Any(d => d.DropDown.Visible)) return;
                Rectangle b = item.Bounds;
                var anchor = new Rectangle(toolbar.Left + b.X, toolbar.Top, b.Width, toolbar.Height);
                // Hacia fuera de la zona: debajo si la barra está por debajo de su centro
                ShowTip(item.ToolTipText, anchor, below: toolbar.Top + toolbar.Height / 2 > selection.Top + selection.Height / 2);
            };
            item.MouseLeave += (_, _) => HideTip();
            item.MouseDown += (_, _) => HideTip();
        }
    }
}
