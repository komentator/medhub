using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MedControl.Logging;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Services;
using MedControl.Settings;

namespace MedControl.Sync;

// Синхронизация двух ПК в локальной сети (без облака) + задел под Android (контракт V=1).
// Транспорт — TCP с минимальным HTTP (свой TcpListener: не требует прав администратора,
// в отличие от HttpListener; при первом запуске брандмауэр спросит разрешение).
// Слияние — last-writer-wins по UpdatedAt/DeletedAt, дубли одной дозы давятся естественным ключом.
// Ограничения v1: фото не передаются (только записи), удалённое навсегда не распространяется.
public sealed class SyncService : IDisposable
{
    private const string Prefix = "/medcontrol/";
    private readonly MedicationRepository _meds;
    private readonly IntakeRepository _intakes;
    private readonly MeasurementRepository _meas;
    private readonly WellBeingRepository _wb;
    private readonly SyncPeerRepository _peers;
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private TcpListener? _listener;
    private Thread? _thread;
    private volatile bool _running;

    public event Action? Synced;

    // Поставщик пакетов переноса для /transfer (подключает приложение).
    public TransferService? Transfer { get; set; }

    // Локальные IPv4-адреса этого ПК (какой вводить на втором компьютере).
    public static List<string> GetLocalIps()
    {
        var list = new List<string>();
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    list.Add(ip.ToString());
        }
        catch { }
        return list;
    }

    public SyncService(MedicationRepository meds, IntakeRepository intakes, MeasurementRepository meas,
        WellBeingRepository wb, SyncPeerRepository peers, SettingsService settings, LogService log)
    {
        _meds = meds; _intakes = intakes; _meas = meas; _wb = wb;
        _peers = peers; _settings = settings; _log = log;
    }

    private static string S(DateTime? d) => d?.ToString("O") ?? "";
    private static string S2(string? s) => s ?? "";

    // ---------- Сбор ----------
    public SyncPayload Collect(DateTime since)
    {
        var p = SyncPayload.Empty(_settings.DeviceId, _settings.DeviceName);
        foreach (var m in _meds.GetAll())
        {
            if (!SyncMerge.ChangedSince(S(m.UpdatedAt), null, since)) continue;
            p.Meds.Add(new RMed(m.Id.ToString(), m.Name, m.Description ?? "", m.IsActive ? 1 : 0,
                (int)m.Mode, string.Join(",", m.DaysOfWeek),
                m.StartDate?.ToString("O"), m.EndDate?.ToString("O"), S(m.UpdatedAt),
                S2(m.PhotoPackPath), S2(m.PhotoPillPath), S2(m.PhotoInstructionPath), S2(m.InstructionText),
                m.Doses.Select(d => new RDose(d.Id.ToString(), d.Time.ToString("HH:mm"), d.Dosage ?? "", S(d.UpdatedAt))).ToList()));
        }
        foreach (var i in _intakes.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true))
        {
            if (!SyncMerge.ChangedSince(S(i.UpdatedAt), S(i.DeletedAt), since)) continue;
            p.Intakes.Add(new RIntake(i.Id.ToString(), i.MedicationId.ToString(), i.MedicationName,
                i.MedicationDescription ?? "", i.Dosage ?? "", i.ScheduledAt.ToString("O"),
                i.ActualAt?.ToString("O"), (int)i.Status, (int)i.ConfirmMethod, i.Comment ?? "",
                i.IsDeleted ? 1 : 0, i.CreatedAt.ToString("O"), S(i.DeletedAt), S(i.RestoredAt),
                S(i.ShownAt), S(i.SnoozedAt), S(i.UpdatedAt)));
        }
        foreach (var t in _meas.GetTonometers(true))
        {
            if (!SyncMerge.ChangedSince(S(t.UpdatedAt), null, since)) continue;
            p.Tonos.Add(new RTono(t.Id.ToString(), t.Name, t.IsDeleted ? 1 : 0, S(t.UpdatedAt)));
        }
        foreach (var m in _meas.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true))
        {
            if (!SyncMerge.ChangedSince(S(m.UpdatedAt), S(m.DeletedAt), since)) continue;
            p.Meas.Add(new RMeas(m.Id.ToString(), m.TonometerId.ToString(), m.TonometerName,
                m.TakenAt.ToString("O"), m.Systolic, m.Diastolic, m.Pulse, m.Comment ?? "",
                m.CreatedAt.ToString("O"), S(m.ModifiedAt), m.IsDeleted ? 1 : 0,
                S(m.DeletedAt), S(m.RestoredAt), S(m.UpdatedAt)));
        }
        foreach (var w in _wb.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true))
        {
            if (!SyncMerge.ChangedSince(S(w.UpdatedAt), S(w.DeletedAt), since)) continue;
            p.Wb.Add(new RWb(w.Id.ToString(), w.Score, w.Comment ?? "", w.CreatedAt.ToString("O"),
                S(w.ModifiedAt), w.IsDeleted ? 1 : 0, S(w.DeletedAt), S(w.RestoredAt), S(w.UpdatedAt)));
            foreach (var l in _wb.GetLinks(w.Id))
                p.Links.Add(new RLink(w.Id.ToString(), l.LinkedId.ToString(), l.LinkedKind));
        }
        return p;
    }

    // ---------- Применение (возвращает сводку) ----------
    public string Apply(SyncPayload p)
    {
        int nMed = 0, nIn = 0, nTo = 0, nMe = 0, nWb = 0;
        foreach (var r in p.Meds)
        {
            var id = Guid.Parse(r.Id);
            var local = _meds.GetAll().FirstOrDefault(m => m.Id == id);
            if (local == null || SyncMerge.IncomingWins(r.Updated, null, S(local.UpdatedAt), null))
            {
                _meds.Save(new Medication
                {
                    Id = id, Name = r.Name, Description = r.Desc, IsActive = r.Active == 1,
                    Mode = (ScheduleMode)r.Mode,
                    DaysOfWeek = r.Days.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                    StartDate = string.IsNullOrEmpty(r.Start) ? null : DateOnly.Parse(r.Start),
                    EndDate = string.IsNullOrEmpty(r.End) ? null : DateOnly.Parse(r.End),
                    UpdatedAt = string.IsNullOrEmpty(r.Updated) ? null : DateTime.Parse(r.Updated),
                    PhotoPackPath = r.PhotoPack, PhotoPillPath = r.PhotoPill,
                    PhotoInstructionPath = r.PhotoInstr, InstructionText = r.InstrText,
                    Doses = r.Doses.Select(d => new MedicationDose
                    {
                        Id = Guid.Parse(d.Id), MedicationId = id, Time = TimeOnly.Parse(d.Time),
                        Dosage = d.Dosage, UpdatedAt = string.IsNullOrEmpty(d.Updated) ? null : DateTime.Parse(d.Updated),
                    }).ToList(),
                });
                nMed++;
            }
        }
        foreach (var r in p.Tonos)
        {
            var id = Guid.Parse(r.Id);
            var local = _meas.GetTonometers(true).FirstOrDefault(t => t.Id == id);
            if (local == null || SyncMerge.IncomingWins(r.Updated, null, S(local.UpdatedAt), null))
            {
                _meas.SaveTonometer(new Tonometer { Id = id, Name = r.Name, IsDeleted = r.Del == 1,
                    UpdatedAt = string.IsNullOrEmpty(r.Updated) ? null : DateTime.Parse(r.Updated) });
                nTo++;
            }
        }
        var allIn = _intakes.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true).ToDictionary(i => i.Id);
        foreach (var r in p.Intakes)
        {
            var id = Guid.Parse(r.Id);
            var e = ToIntake(r);
            if (!allIn.TryGetValue(id, out var local) ||
                SyncMerge.IncomingWins(S(e.UpdatedAt), S(e.DeletedAt), S(local.UpdatedAt), S(local.DeletedAt)))
            {
                _intakes.Upsert(e);
                nIn++;
            }
        }
        DedupIntakes();
        var allMe = _meas.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true).ToDictionary(m => m.Id);
        foreach (var r in p.Meas)
        {
            var id = Guid.Parse(r.Id);
            var e = ToMeas(r);
            if (!allMe.TryGetValue(id, out var local) ||
                SyncMerge.IncomingWins(S(e.UpdatedAt), S(e.DeletedAt), S(local.UpdatedAt), S(local.DeletedAt)))
            {
                _meas.Upsert(e);
                nMe++;
            }
        }
        DedupMeas();
        var allWb = _wb.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true).ToDictionary(w => w.Id);
        foreach (var r in p.Wb)
        {
            var id = Guid.Parse(r.Id);
            var e = ToWb(r);
            if (!allWb.TryGetValue(id, out var local) ||
                SyncMerge.IncomingWins(S(e.UpdatedAt), S(e.DeletedAt), S(local.UpdatedAt), S(local.DeletedAt)))
            {
                _wb.Upsert(e);
                if (e.IsDeleted) _wb.SetLinks(id, []); // у удалённой оценки связей нет
                nWb++;
            }
        }
        DedupWb();
        // Связи — объединением (сироты безвредны: join их не находит).
        foreach (var g in p.Links.GroupBy(l => l.Wb))
        {
            var wbId = Guid.Parse(g.Key);
            var cur = _wb.GetLinks(wbId).Select(l => (l.LinkedId, l.LinkedKind)).ToHashSet();
            foreach (var l in g) cur.Add((Guid.Parse(l.Linked), l.Kind));
            _wb.SetLinks(wbId, cur.ToList());
        }
        return $"лекарств: {nMed}, приёмов: {nIn}, тонометров: {nTo}, измерений: {nMe}, самочувствия: {nWb}";
    }

    private static MedicationIntake ToIntake(RIntake r) => new()
    {
        Id = Guid.Parse(r.Id), MedicationId = Guid.Parse(r.MedId), MedicationName = r.Name,
        MedicationDescription = r.Desc, Dosage = r.Dosage, ScheduledAt = DateTime.Parse(r.Sched),
        ActualAt = string.IsNullOrEmpty(r.Actual) ? null : DateTime.Parse(r.Actual),
        Status = (IntakeStatus)r.Status, ConfirmMethod = (ConfirmMethod)r.Method, Comment = r.Comment,
        IsDeleted = r.Del == 1, CreatedAt = DateTime.Parse(r.Created),
        DeletedAt = string.IsNullOrEmpty(r.DeletedAt) ? null : DateTime.Parse(r.DeletedAt),
        RestoredAt = string.IsNullOrEmpty(r.RestoredAt) ? null : DateTime.Parse(r.RestoredAt),
        ShownAt = string.IsNullOrEmpty(r.Shown) ? null : DateTime.Parse(r.Shown),
        SnoozedAt = string.IsNullOrEmpty(r.Snoozed) ? null : DateTime.Parse(r.Snoozed),
        UpdatedAt = string.IsNullOrEmpty(r.Updated) ? null : DateTime.Parse(r.Updated),
    };

    private static Measurement ToMeas(RMeas r) => new()
    {
        Id = Guid.Parse(r.Id), TonometerId = Guid.Parse(r.TonoId), TonometerName = r.TonoName,
        TakenAt = DateTime.Parse(r.Taken), Systolic = r.Sys, Diastolic = r.Dia, Pulse = r.Pulse,
        Comment = r.Comment, CreatedAt = DateTime.Parse(r.Created),
        ModifiedAt = string.IsNullOrEmpty(r.Mod) ? null : DateTime.Parse(r.Mod),
        IsDeleted = r.Del == 1,
        DeletedAt = string.IsNullOrEmpty(r.DeletedAt) ? null : DateTime.Parse(r.DeletedAt),
        RestoredAt = string.IsNullOrEmpty(r.RestoredAt) ? null : DateTime.Parse(r.RestoredAt),
        UpdatedAt = string.IsNullOrEmpty(r.Updated) ? null : DateTime.Parse(r.Updated),
    };

    private static WellBeingRating ToWb(RWb r) => new()
    {
        Id = Guid.Parse(r.Id), Score = r.Score, Comment = r.Comment, CreatedAt = DateTime.Parse(r.Created),
        ModifiedAt = string.IsNullOrEmpty(r.Mod) ? null : DateTime.Parse(r.Mod),
        IsDeleted = r.Del == 1,
        DeletedAt = string.IsNullOrEmpty(r.DeletedAt) ? null : DateTime.Parse(r.DeletedAt),
        RestoredAt = string.IsNullOrEmpty(r.RestoredAt) ? null : DateTime.Parse(r.RestoredAt),
        UpdatedAt = string.IsNullOrEmpty(r.Updated) ? null : DateTime.Parse(r.Updated),
    };

    // Одна доза подтверждена на обоих ПК разными строками — оставить новейшую, остальные в корзину.
    private void DedupIntakes()
    {
        var all = _intakes.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true)
            .Where(i => !i.IsDeleted).GroupBy(i => SyncMerge.IntakeKey(i.MedicationId.ToString(), i.ScheduledAt.ToString("O")));
        foreach (var g in all)
        {
            if (g.Count() < 2) continue;
            var win = SyncMerge.PickWinner(g.Select(i => (i.Id.ToString(), S(i.UpdatedAt))));
            foreach (var i in g)
            {
                if (i.Id.ToString() == win) continue;
                i.IsDeleted = true;
                i.DeletedAt = DateTime.Now;
                i.UpdatedAt = DateTime.Now;
                _intakes.Upsert(i);
            }
        }
    }

    private void DedupMeas()
    {
        var all = _meas.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true)
            .Where(m => !m.IsDeleted).GroupBy(m => SyncMerge.MeasKey(m.TakenAt.ToString("O"), m.TonometerName, m.Systolic, m.Diastolic, m.Pulse));
        foreach (var g in all)
        {
            if (g.Count() < 2) continue;
            var win = SyncMerge.PickWinner(g.Select(m => (m.Id.ToString(), S(m.UpdatedAt))));
            foreach (var m in g)
            {
                if (m.Id.ToString() == win) continue;
                m.IsDeleted = true;
                m.DeletedAt = DateTime.Now;
                m.UpdatedAt = DateTime.Now;
                _meas.Upsert(m);
            }
        }
    }

    private void DedupWb()
    {
        var all = _wb.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2), true)
            .Where(w => !w.IsDeleted).GroupBy(w => SyncMerge.WbKey(w.CreatedAt.ToString("O"), w.Score));
        foreach (var g in all)
        {
            if (g.Count() < 2) continue;
            var win = SyncMerge.PickWinner(g.Select(w => (w.Id.ToString(), S(w.UpdatedAt))));
            foreach (var w in g)
            {
                if (w.Id.ToString() == win) continue;
                w.IsDeleted = true;
                w.DeletedAt = DateTime.Now;
                w.UpdatedAt = DateTime.Now;
                _wb.Upsert(w);
            }
        }
    }

    // ---------- Сервер ----------
    public void StartServer()
    {
        StopServer();
        try
        {
            int port = _settings.SyncPort <= 0 ? 47890 : _settings.SyncPort;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _running = true;
            _thread = new Thread(ListenLoop) { IsBackground = true, Name = "SyncServer" };
            _thread.Start();
            _log.Write("Sync", $"Сервер запущен на порту {port}");
        }
        catch (Exception ex) { _log.Write("Sync", "StartServer: " + ex.Message); throw; }
    }

    public void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                var client = _listener!.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch { if (!_running) break; Thread.Sleep(500); }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 20000;
                var req = ReadAll(stream);
                var (head, body) = Split(req);
                var lines = head.Split('\n');
                var requestLine = lines.Length > 0 ? lines[0].Trim() : "";
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    var k = lines[i].IndexOf(':');
                    if (k > 0) headers[lines[i][..k].Trim()] = lines[i][(k + 1)..].Trim();
                }
                headers.TryGetValue("X-Sync-Key", out var key);
                if (string.IsNullOrEmpty(_settings.SyncKey) || key != _settings.SyncKey)
                {
                    WriteResp(stream, 403, "{\"Error\":\"bad key\"}");
                    return;
                }
                if (requestLine.StartsWith("POST " + Prefix + "pull"))
                {
                    var pr = JsonSerializer.Deserialize<PullRequest>(body) ?? new PullRequest("");
                    var payload = Collect(SyncMerge.T(pr.Since));
                    WriteResp(stream, 200, JsonSerializer.Serialize(payload));
                }
                else if (requestLine.StartsWith("POST " + Prefix + "push"))
                {
                    var p = JsonSerializer.Deserialize<SyncPayload>(body);
                    if (p == null) { WriteResp(stream, 400, "{\"Error\":\"bad payload\"}"); return; }
                    if (p.Device == _settings.DeviceId) { WriteResp(stream, 200, JsonSerializer.Serialize(new PushResult(true, "self", DateTime.UtcNow.ToString("O")))); return; }
                    var summary = Apply(p);
                    _log.Write("Sync", $"Push от {p.DeviceName}: {summary}");
                    try { Synced?.Invoke(); } catch { }
                    WriteResp(stream, 200, JsonSerializer.Serialize(new PushResult(true, null, DateTime.UtcNow.ToString("O"))));
                }
                else if (requestLine.StartsWith("POST " + Prefix + "transfer"))
                {
                    // Полный пакет переноса БД+фото (для нового ПК без файла).
                    if (Transfer == null) { WriteResp(stream, 500, "{\"Error\":\"no transfer\"}"); return; }
                    var ip = GetLocalIps().FirstOrDefault() ?? "";
                    var (ok, err, bytes) = Transfer.CreatePackBytes(ip);
                    if (!ok || bytes == null) { WriteResp(stream, 500, "{\"Error\":\"pack failed\"}"); return; }
                    WriteRespBytes(stream, 200, bytes, "application/octet-stream");
                }
                else WriteResp(stream, 404, "{\"Error\":\"unknown\"}");
            }
            catch (Exception ex) { _log.Write("Sync", "HandleClient: " + ex.Message); }
        }
    }

    private static byte[] ReadAll(NetworkStream stream)
    {
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = stream.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
        return ms.ToArray();
    }

    private static (string Head, string Body) Split(byte[] raw)
    {
        var text = Encoding.UTF8.GetString(raw);
        int i = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (i < 0) return (text, "");
        return (text[..i].Replace("\r", ""), text[(i + 4)..]);
    }

    private static void WriteResp(NetworkStream stream, int code, string body)
        => WriteRespBytes(stream, code, Encoding.UTF8.GetBytes(body), "application/json");

    private static void WriteRespBytes(NetworkStream stream, int code, byte[] body, string contentType)
    {
        var head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(head);
        stream.Write(hb, 0, hb.Length);
        stream.Write(body, 0, body.Length);
    }

    // ---------- Клиент ----------
    private static (int Code, string Head, byte[] Body) PostRaw(string host, int port, string path, string key, byte[] body, string contentType, int timeoutMs)
    {
        using var client = new TcpClient();
        if (!client.ConnectAsync(host, port).Wait(10000)) throw new TimeoutException("Нет соединения с " + host);
        var stream = client.GetStream();
        stream.ReadTimeout = timeoutMs;
        stream.WriteTimeout = timeoutMs;
        var head = $"POST {path} HTTP/1.1\r\nHost: {host}\r\nX-Sync-Key: {key}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(head);
        stream.Write(hb, 0, hb.Length);
        stream.Write(body, 0, body.Length);
        var raw = ReadAll(stream);
        var (respHead, respBody) = SplitRaw(raw);
        int code = 0;
        var first = respHead.Split('\n').FirstOrDefault() ?? "";
        var parts = first.Split(' ');
        if (parts.Length >= 2) int.TryParse(parts[1], out code);
        return (code, respHead, respBody);
    }

    private static (string Head, byte[] Body) SplitRaw(byte[] raw)
    {
        for (int i = 0; i + 3 < raw.Length; i++)
        {
            if (raw[i] == '\r' && raw[i + 1] == '\n' && raw[i + 2] == '\r' && raw[i + 3] == '\n')
                return (Encoding.ASCII.GetString(raw, 0, i).Replace("\r", ""), raw[(i + 4)..]);
        }
        return (Encoding.ASCII.GetString(raw), []);
    }

    private static string Post(string host, int port, string path, string key, string body)
    {
        var (code, respHead, respBody) = PostRaw(host, port, path, key, Encoding.UTF8.GetBytes(body), "application/json", 20000);
        if (code != 200) throw new InvalidOperationException("Сервер отказал: " + respHead.Split('\n').FirstOrDefault());
        return Encoding.UTF8.GetString(respBody);
    }

    public byte[] DownloadPack(string host)
    {
        var (code, respHead, body) = PostRaw(host, _settings.SyncPort, Prefix + "transfer", _settings.SyncKey, "{}"u8.ToArray(), "application/json", 120000);
        if (code != 200) throw new InvalidOperationException("Сервер отказал: " + respHead.Split('\n').FirstOrDefault());
        if (body.Length < 100) throw new InvalidOperationException("Пустой пакет.");
        return body;
    }

    public SyncPayload PullFrom(string host, DateTime since)
    {
        var body = Post(host, _settings.SyncPort, Prefix + "pull", _settings.SyncKey,
            JsonSerializer.Serialize(new PullRequest(since.ToString("O"))));
        return JsonSerializer.Deserialize<SyncPayload>(body) ?? throw new InvalidOperationException("Пустой ответ");
    }

    public (bool Ok, string Msg) SyncNow(string host)
    {
        try
        {
            var peer = _peers.GetAll().FirstOrDefault(p => p.Address == host);
            var since = peer?.LastSyncAt ?? DateTime.MinValue;
            var pulled = PullFrom(host, since);
            if (pulled.Device == _settings.DeviceId) return (false, "Это адрес этого же ПК.");
            var s1 = Apply(pulled);
            var mine = Collect(since);
            var resp = Post(host, _settings.SyncPort, Prefix + "push", _settings.SyncKey, JsonSerializer.Serialize(mine));
            var pr = JsonSerializer.Deserialize<PushResult>(resp);
            if (pr == null || !pr.Ok) return (false, "Push отклонён: " + pr?.Error);
            var now = DateTime.UtcNow;
            _peers.Touch(pulled.Device, pulled.DeviceName, host, now);
            try { Synced?.Invoke(); } catch { }
            return (true, $"Готово. Получено ({s1}).");
        }
        catch (Exception ex)
        {
            _log.Write("Sync", "SyncNow: " + ex.Message);
            return (false, ex.Message);
        }
    }

    public void Dispose() => StopServer();
}
