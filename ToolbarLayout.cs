namespace Trazo {

    /// <summary>
    /// Qué botones lleva la barra de la captura y en qué orden. Los ocultos siguen funcionando con su atajo.
    /// Si el usuario no la ha tocado, en la configuración no se guarda nada y se usa la de por defecto.
    /// </summary>
    internal sealed class ToolbarLayout {

        public const string Separator = "|";

        /// <summary>La barra por defecto: todos los botones que existen, en su orden.</summary>
        private static readonly string[] DefaultVisible = {
            "Rectangle", "Ellipse", "Arrows", "Line", "Text", Separator,
            "Stroke", "Fill", "Width", Separator,
            "Pixelate", "Picker", "Ocr", Separator,
            "Copy", "Save", "Undo", "Close",
        };

        private static readonly HashSet<string> Known = new(DefaultVisible.Where(k => k != Separator));

        /// <summary>Botones y separadores de la barra, en orden.</summary>
        public List<string> Visible { get; }

        /// <summary>Botones ocultos (nunca separadores).</summary>
        public List<string> Hidden { get; }

        private ToolbarLayout(IEnumerable<string> visible, IEnumerable<string> hidden) {
            Visible = visible.ToList();
            Hidden = hidden.ToList();
        }

        public static ToolbarLayout Default => new(DefaultVisible, Array.Empty<string>());

        /// <summary>
        /// La barra guardada en la configuración. Se descarta lo desconocido o repetido, y los botones que no aparecen
        /// (los que traiga una versión posterior) se añaden al final de la barra.
        /// </summary>
        public static ToolbarLayout Load(AppSettings settings) {
            if (settings.ToolbarButtons == null) return Default;
            var seen = new HashSet<string>();
            var visible = settings.ToolbarButtons.Where(k => k == Separator || (Known.Contains(k) && seen.Add(k))).ToList();
            var hidden = (settings.ToolbarHidden ?? new List<string>()).Where(k => Known.Contains(k) && seen.Add(k)).ToList();
            visible.AddRange(DefaultVisible.Where(k => k != Separator && !seen.Contains(k)));
            return new ToolbarLayout(visible, hidden);
        }

        /// <summary>Guarda la barra; si es la de por defecto, la borra para que el usuario reciba los cambios de versiones futuras.</summary>
        public void Save(AppSettings settings) {
            bool isDefault = Hidden.Count == 0 && Visible.SequenceEqual(DefaultVisible);
            settings.ToolbarButtons = isDefault ? null : new List<string>(Visible);
            settings.ToolbarHidden = isDefault ? null : new List<string>(Hidden);
        }

        /// <summary>
        /// Posiciones de <see cref="Visible"/> que se pintan en la barra: sin separadores al principio, al final
        /// ni dos seguidos (de varios seguidos queda el último).
        /// </summary>
        public IEnumerable<int> ShownIndices() {
            int pendingSeparator = -1;
            bool any = false;
            for (int i = 0; i < Visible.Count; i++) {
                if (Visible[i] == Separator) {
                    if (any) pendingSeparator = i;
                    continue;
                }
                if (pendingSeparator >= 0) yield return pendingSeparator;
                pendingSeparator = -1;
                any = true;
                yield return i;
            }
        }
    }
}
