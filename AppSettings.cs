using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace Trazo {

    /// <summary>
    /// Configuración guardada en %AppData%\Trazo\settings.json.
    /// </summary>
    [DataContract]
    internal sealed class AppSettings {

        /// <summary>Tecla más modificadores (Keys.Control, Keys.Alt, Keys.Shift).</summary>
        [DataMember] public Keys Hotkey { get; set; }

        [DataMember] public bool StartWithWindows { get; set; }

        // Último estilo de dibujo elegido; colores en formato #RRGGBB

        [DataMember] public string StrokeColor { get; set; } = "";

        /// <summary>Null = sin relleno.</summary>
        [DataMember] public string? FillColor { get; set; }

        [DataMember] public float StrokeWidth { get; set; }

        [DataMember] public string TextFont { get; set; } = "";

        /// <summary>Tamaño del texto en píxeles.</summary>
        [DataMember] public float TextSize { get; set; }

        /// <summary>Última herramienta de dibujo elegida (Arrow, Line, Rectangle, Ellipse o Text).</summary>
        [DataMember] public string LastTool { get; set; } = "";

        // Aspecto de la imagen copiada o guardada

        [DataMember] public bool RoundCorners { get; set; }

        /// <summary>Radio de las esquinas en píxeles de la captura.</summary>
        [DataMember] public int CornerRadius { get; set; }

        [DataMember] public bool AddBorder { get; set; }

        [DataMember] public string BorderColor { get; set; } = "";

        /// <summary>Grosor del borde en píxeles de la captura.</summary>
        [DataMember] public int BorderWidth { get; set; }

        public AppSettings() => SetDefaults();

        // El serializador no ejecuta el constructor: así los valores que falten en el archivo
        // (por ejemplo, de una versión anterior) toman su valor por defecto
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults() {
            Hotkey = Keys.Control | Keys.Alt | Keys.S;
            StartWithWindows = true;
            StrokeColor = "#E61E1E";
            FillColor = null;
            StrokeWidth = 3;
            TextFont = TextShape.DefaultFontFamily;
            TextSize = 24;
            LastTool = "Arrow";
            RoundCorners = false;
            CornerRadius = 12;
            AddBorder = false;
            BorderColor = "#C8C8C8";
            BorderWidth = 3;
        }

        public Color BorderColorValue {
            get => ParseColor(BorderColor) ?? Color.FromArgb(200, 200, 200);
            set => BorderColor = ToHex(value);
        }

        public Color StrokeColorValue {
            get => ParseColor(StrokeColor) ?? Color.FromArgb(230, 30, 30);
            set => StrokeColor = ToHex(value);
        }

        public Color? FillColorValue {
            get => ParseColor(FillColor);
            set => FillColor = value is Color c ? ToHex(c) : null;
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color? ParseColor(string? hex) {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try {
                return ColorTranslator.FromHtml(hex);
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>No existía archivo de configuración: es la primera vez que se abre.</summary>
        public bool IsFirstRun { get; private set; }

        private static readonly DataContractJsonSerializer Serializer = new(typeof(AppSettings));

        private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string FilePath = Path.Combine(AppData, "Trazo", "settings.json");
        // Ubicación anterior, de cuando el programa se llamaba Screenshot
        private static readonly string LegacyFilePath = Path.Combine(AppData, "Screenshot", "settings.json");

        public static AppSettings Load() {
            try {
                string? path = File.Exists(FilePath) ? FilePath : File.Exists(LegacyFilePath) ? LegacyFilePath : null;
                if (path != null) {
                    using FileStream stream = File.OpenRead(path);
                    return Serializer.ReadObject(stream) as AppSettings ?? new AppSettings();
                }
            } catch (Exception) {
                // Archivo corrupto o ilegible: se usan los valores por defecto
            }
            return new AppSettings { IsFirstRun = true };
        }

        /// <summary>Borra la configuración del usuario actual (al desinstalar, si lo pide).</summary>
        public static void DeleteAll() {
            try {
                string folder = Path.GetDirectoryName(FilePath)!;
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                if (File.Exists(LegacyFilePath)) File.Delete(LegacyFilePath);
            } catch (Exception) {
                // Archivo bloqueado o sin permisos: se deja como está
            }
        }

        public void Save() {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using FileStream stream = File.Create(FilePath);
            using var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true);
            Serializer.WriteObject(writer, this);
        }
    }

    /// <summary>
    /// Inicio automático con Windows (HKCU\...\Run). El instalador crea el valor y lo borra al desinstalar.
    /// </summary>
    internal static class Autostart {

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Trazo";

        public static bool IsEnabled {
            get {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled) {
            try {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
            } catch (Exception) {
                // Sin permisos sobre el registro: no es crítico
            }
        }
    }

    internal static class HotkeyText {

        public static string Format(Keys hotkey) {
            var parts = new List<string>();
            if (hotkey.HasFlag(Keys.Control)) parts.Add("Ctrl");
            if (hotkey.HasFlag(Keys.Alt)) parts.Add("Alt");
            if (hotkey.HasFlag(Keys.Shift)) parts.Add("Mayús");

            Keys key = hotkey & Keys.KeyCode;
            if (key != Keys.None) parts.Add(KeyName(key));
            return string.Join(" + ", parts);
        }

        private static string KeyName(Keys key) => key switch {
            Keys.PrintScreen => "Impr Pant",
            Keys.Pause => "Pausa",
            Keys.Scroll => "Bloq Despl",
            Keys.Insert => "Insert",
            Keys.Delete => "Supr",
            Keys.Home => "Inicio",
            Keys.End => "Fin",
            Keys.PageUp => "Re Pág",
            Keys.PageDown => "Av Pág",
            Keys.Space => "Espacio",
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num {key - Keys.NumPad0}",
            _ => key.ToString(),
        };
    }
}
