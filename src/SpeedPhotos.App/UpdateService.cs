using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SpeedPhotos.App
{
    internal sealed class UpdateInfo
    {
        public Version Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleasePage { get; set; }
    }

    internal static class UpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/scanby/SpeedPhotos/releases/latest";
        private const string InstallerAssetName = "SpeedPhotos-Setup.exe";

        public static Task<UpdateInfo> CheckForUpdateAsync()
        {
            return Task.Run(() =>
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "SpeedPhotos-Updater";
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    string json = client.DownloadString(LatestReleaseApi);
                    var serializer = new JavaScriptSerializer();
                    var release = serializer.Deserialize<Dictionary<string, object>>(json);
                    if (release == null || !release.ContainsKey("tag_name"))
                        throw new InvalidDataException("GitHub no devolvió información válida de la versión.");

                    string tag = Convert.ToString(release["tag_name"]).Trim();
                    if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
                    Version latest;
                    if (!Version.TryParse(tag, out latest))
                        throw new InvalidDataException("La etiqueta de versión de GitHub no es válida: " + tag);

                    Version current = Assembly.GetExecutingAssembly().GetName().Version;
                    if (latest <= current) return null;

                    string releasePage = release.ContainsKey("html_url") ? Convert.ToString(release["html_url"]) : null;
                    var assets = release.ContainsKey("assets") as object;
                    var assetList = release["assets"] as System.Collections.ArrayList;
                    if (assetList == null)
                        throw new InvalidDataException("La publicación no contiene archivos descargables.");

                    foreach (object entry in assetList)
                    {
                        var asset = entry as Dictionary<string, object>;
                        if (asset == null || !asset.ContainsKey("name") || !asset.ContainsKey("browser_download_url")) continue;
                        if (!String.Equals(Convert.ToString(asset["name"]), InstallerAssetName, StringComparison.OrdinalIgnoreCase)) continue;

                        string url = Convert.ToString(asset["browser_download_url"]);
                        Uri uri;
                        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                            !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("La dirección del instalador no es válida.");

                        return new UpdateInfo { Version = latest, DownloadUrl = url, ReleasePage = releasePage };
                    }
                    throw new InvalidDataException("La versión " + latest + " está publicada, pero aún no tiene el instalador " + InstallerAssetName + ".");
                }
            });
        }

        public static Task<string> DownloadInstallerAsync(UpdateInfo update)
        {
            return Task.Run(() =>
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string directory = Path.Combine(Path.GetTempPath(), "SpeedPhotos-Updates");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, InstallerAssetName);
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "SpeedPhotos-Updater";
                    client.DownloadFile(update.DownloadUrl, file);
                }
                var info = new FileInfo(file);
                if (!info.Exists || info.Length < 1024)
                    throw new InvalidDataException("El instalador descargado está incompleto.");
                return file;
            });
        }
    }
}
