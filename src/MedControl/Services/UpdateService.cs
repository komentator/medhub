using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using MedControl.Logging;

namespace MedControl.Services;

// Обновления из GitHub Releases (https://github.com/komentator/medhub/releases).
// Каждая новая версия программы = новый Release с setup-файлом.
public sealed record UpdateInfo(bool Available, string Version, string Url, string Notes, string Current);

public static class UpdateCheck
{
    public static Version? ParseTag(string? tag)
    {
        tag = (tag ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(tag, out var v) ? v : null;
    }

    public static bool IsNewer(Version current, Version latest) => latest > current;

    public static Version Current()
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
}

public sealed class UpdateService
{
    private const string Api = "https://api.github.com/repos/komentator/medhub/releases/latest";
    private readonly LogService _log;
    public UpdateService(LogService log) => _log = log;

    public async Task<UpdateInfo> CheckAsync()
    {
        var cur = UpdateCheck.Current();
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MedControl");
            http.Timeout = TimeSpan.FromSeconds(20);
            var json = await http.GetStringAsync(Api);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var latest = UpdateCheck.ParseTag(tag);
            if (latest == null) return new UpdateInfo(false, "", "", "Не удалось прочитать версию.", cur.ToString());
            if (!UpdateCheck.IsNewer(cur, latest))
                return new UpdateInfo(false, latest.ToString(), "", "", cur.ToString());
            string url = "", notes = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var u))
                    {
                        url = u.GetString() ?? "";
                        break;
                    }
                }
            }
            if (root.TryGetProperty("body", out var b)) notes = b.GetString() ?? "";
            if (string.IsNullOrEmpty(url) && root.TryGetProperty("html_url", out var h)) url = h.GetString() ?? "";
            return new UpdateInfo(true, latest.ToString(), url, notes, cur.ToString());
        }
        catch (Exception ex)
        {
            _log.Write("Update", "Check: " + ex.Message);
            throw new InvalidOperationException("Нет связи с GitHub: " + ex.Message);
        }
    }

    public async Task<string> DownloadAsync(string url, string dest)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MedControl");
        http.Timeout = TimeSpan.FromMinutes(10);
        var bytes = await http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(dest, bytes);
        return dest;
    }
}
