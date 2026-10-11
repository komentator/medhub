using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using MedControl.Logging;

namespace MedControl.Services;

// Обновления из GitHub Releases (https://github.com/komentator/medhub/releases).
// Каждая новая версия программы = новый Release с setup-файлом.
public sealed record UpdateInfo(bool Available, string Version, string Url, string Notes, string Current, long Size);

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
        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (attempt > 0) await Task.Delay(2000);
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MedControl");
                http.Timeout = TimeSpan.FromSeconds(60);
                var json = await http.GetStringAsync(Api);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                var latest = UpdateCheck.ParseTag(tag);
            if (latest == null) return new UpdateInfo(false, "", "", "Не удалось прочитать версию.", cur.ToString(), 0);
            if (!UpdateCheck.IsNewer(cur, latest))
                return new UpdateInfo(false, latest.ToString(), "", "", cur.ToString(), 0);
            string url = "", notes = "";
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var u))
                    {
                        url = u.GetString() ?? "";
                        if (a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long s)) size = s;
                        break;
                    }
                }
            }
            if (root.TryGetProperty("body", out var b)) notes = b.GetString() ?? "";
            if (string.IsNullOrEmpty(url) && root.TryGetProperty("html_url", out var h)) url = h.GetString() ?? "";
            return new UpdateInfo(true, latest.ToString(), url, notes, cur.ToString(), size);
            }
            catch (Exception ex)
            {
                last = ex;
                _log.Write("Update", $"Check попытка {attempt + 1}: " + ex.Message);
            }
        }
        throw new InvalidOperationException("Нет связи с GitHub: " + last?.Message + " Проверьте интернет и антивирус.");
    }

    public Task<string> DownloadAsync(string url, string dest)
        => DownloadWithResumeAsync(url, dest, null, null);

    // Закачка с докачкой: до 4 попыток, продолжает с места обрыва (Range).
    public async Task<string> DownloadWithResumeAsync(string url, string dest, long? expectedSize, Action<long, long?>? progress)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (attempt > 0) await Task.Delay(3000);
                long have = File.Exists(dest) ? new FileInfo(dest).Length : 0;
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MedControl");
                http.Timeout = TimeSpan.FromMinutes(30);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    if (expectedSize != null && have >= expectedSize) return dest;
                    try { File.Delete(dest); } catch { }
                    have = 0;
                    continue;
                }
                resp.EnsureSuccessStatusCode();
                bool append = resp.StatusCode == System.Net.HttpStatusCode.PartialContent && have > 0;
                if (!append && have > 0)
                {
                    try { File.Delete(dest); } catch { }
                    have = 0;
                }
                long? total = resp.Content.Headers.ContentRange?.Length
                    ?? (append ? null : resp.Content.Headers.ContentLength);
                if (append && total == null && expectedSize != null) total = expectedSize;
                using var src = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(dest, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                int n;
                long got = have;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n));
                    got += n;
                    try { progress?.Invoke(got, total); } catch { }
                }
                long want = expectedSize ?? total ?? got;
                if (got != want) throw new IOException($"Скачано {got / 1048576} МБ из {want / 1048576} МБ — обрыв.");
                return dest;
            }
            catch (Exception ex)
            {
                last = ex;
                _log.Write("Update", $"Загрузка попытка {attempt + 1}: " + ex.Message);
            }
        }
        throw new InvalidOperationException("Загрузка не удалась: " + last?.Message);
    }
}
