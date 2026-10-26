using System.Diagnostics;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Trazo {

    /// <summary>
    /// Actualizaciones desde las Releases de GitHub. Solo se consulta cuando el usuario lo pide
    /// desde el menú de la bandeja: Trazo no comprueba nada por su cuenta.
    /// </summary>
    internal static class Updater {

        /// <summary>Repositorio de GitHub (propietario/nombre) donde se publican las versiones.</summary>
        public const string Repository = "antidecn/Trazo";

        private static string LatestReleaseApi => $"https://api.github.com/repos/{Repository}/releases/latest";
        private static string ReleasesPage => $"https://github.com/{Repository}/releases";

        /// <summary>Versión instalada, con tres componentes (1.2.0).</summary>
        public static Version CurrentVersion => Normalize(typeof(Updater).Assembly.GetName().Version);

        /// <summary>
        /// Busca una versión nueva y, si el usuario acepta, descarga el instalador y lo abre.
        /// Devuelve true si se ha lanzado el instalador (Trazo debe cerrarse para que pueda sustituirlo).
        /// </summary>
        public static async Task<bool> CheckAsync() {
            Release? release;
            try {
                release = await GetLatestAsync();
            } catch (Exception ex) {
                if (Show($"No se ha podido comprobar si hay actualizaciones.\n\n{ex.Message}\n\n¿Quieres abrir la página de versiones en el navegador?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    OpenUrl(ReleasesPage);
                }
                return false;
            }

            Version current = CurrentVersion;
            if (release == null || release.Version == null) {
                Show($"No hay ninguna versión publicada todavía.\n\nTienes la versión {current}.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (release.Version <= current) {
                Show($"Ya tienes la última versión de Trazo ({current}).", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            // Solo instaladores descargados del propio repositorio
            Asset? installer = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) == true &&
                IsRepositoryUrl(a.DownloadUrl, "releases/download/"));
            string notes = Summarize(release.Body);
            string message = $"Hay una versión nueva de Trazo: {release.Version} (tienes la {current}).";
            if (notes.Length > 0) message += $"\n\nNovedades:\n{notes}";

            if (installer == null) {
                if (Show(message + "\n\n¿Quieres abrir la página de la versión para descargarla?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) {
                    OpenUrl(IsRepositoryUrl(release.PageUrl, "releases/") ? release.PageUrl! : ReleasesPage);
                }
                return false;
            }

            if (Show(message + "\n\n¿Quieres descargarla e instalarla ahora? Trazo se cerrará para actualizarse.",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) {
                return false;
            }

            string path;
            try {
                path = await DownloadAsync(installer);
            } catch (Exception ex) {
                Show($"No se ha podido descargar la actualización.\n\n{ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Con interfaz: el asistente detecta la versión instalada, la sustituye y ofrece volver a abrir Trazo
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\"") { UseShellExecute = true });
            return true;
        }

        /// <summary>
        /// La dirección es https://github.com/{Repository}/{subpath}…: así una respuesta manipulada
        /// no puede hacer que Trazo descargue o abra algo de otro sitio.
        /// </summary>
        private static bool IsRepositoryUrl(string? url, string subpath) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            uri.AbsolutePath.StartsWith($"/{Repository}/{subpath}", StringComparison.OrdinalIgnoreCase);

        private static WebClient CreateClient() {
            // GitHub solo acepta TLS 1.2 o superior
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new WebClient { Encoding = Encoding.UTF8 };
            // La API de GitHub rechaza las peticiones sin User-Agent
            client.Headers[HttpRequestHeader.UserAgent] = $"Trazo/{CurrentVersion}";
            return client;
        }

        /// <returns>Null si el repositorio aún no tiene versiones publicadas.</returns>
        private static async Task<Release?> GetLatestAsync() {
            using WebClient client = CreateClient();
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            string json;
            try {
                json = await client.DownloadStringTaskAsync(LatestReleaseApi);
            } catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(Release));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return serializer.ReadObject(stream) as Release;
        }

        private static async Task<string> DownloadAsync(Asset asset) {
            string folder = Path.Combine(Path.GetTempPath(), "Trazo-actualizacion");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Path.GetFileName(asset.Name!));

            using (WebClient client = CreateClient()) {
                await client.DownloadFileTaskAsync(asset.DownloadUrl!, path);
            }
            if (asset.Size > 0 && new FileInfo(path).Length != asset.Size) {
                File.Delete(path);
                throw new IOException("El archivo descargado está incompleto. Inténtalo de nuevo.");
            }
            return path;
        }

        /// <summary>Primeras líneas de las notas de la versión, sin el marcado de Markdown más habitual.</summary>
        private static string Summarize(string? body) {
            if (string.IsNullOrWhiteSpace(body)) return "";
            string[] lines = body!.Replace("\r", "").Split('\n')
                .Select(l => l.Trim().TrimStart('#').Trim().Replace("**", ""))
                .Where(l => l.Length > 0)
                .ToArray();
            const int max = 12;
            string text = string.Join("\n", lines.Take(max));
            return lines.Length > max ? text + "\n…" : text;
        }

        private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

        private static void OpenUrl(string url) {
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception) {
                // Sin navegador predeterminado: no hay nada más que hacer
            }
        }

        /// <summary>Mensaje siempre visible: Trazo no tiene ventana a la que asociarlo.</summary>
        private static DialogResult Show(string text, MessageBoxButtons buttons, MessageBoxIcon icon) {
            using var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.CenterScreen };
            owner.Show();
            return MessageBox.Show(owner, text, "Actualizaciones de Trazo", buttons, icon);
        }

        [DataContract]
        private sealed class Release {
            [DataMember(Name = "tag_name")] public string? Tag { get; set; }
            [DataMember(Name = "html_url")] public string? PageUrl { get; set; }
            [DataMember(Name = "body")] public string? Body { get; set; }
            [DataMember(Name = "assets")] public Asset[]? Assets { get; set; }

            /// <summary>Versión de la etiqueta ("v1.2.0", "v.1.2.0" o "1.2.0").</summary>
            public Version? Version =>
                System.Version.TryParse(new string((Tag ?? "").SkipWhile(c => !char.IsDigit(c)).ToArray()).Trim(), out Version v)
                    ? Normalize(v) : null;
        }

        [DataContract]
        private sealed class Asset {
            [DataMember(Name = "name")] public string? Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string? DownloadUrl { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
        }
    }
}
