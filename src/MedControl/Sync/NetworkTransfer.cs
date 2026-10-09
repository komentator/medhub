using MedControl.Logging;
using MedControl.Services;

namespace MedControl.Sync;

// Перенос базы с ведущего ПК по сети (без файла): скачал пакет — восстановил.
public static class NetworkTransfer
{
    public static (bool Ok, string Msg) Run(SyncService sync, TransferService transfer, LogService log, string host)
    {
        host = (host ?? "").Trim();
        if (string.IsNullOrEmpty(host)) return (false, "Укажите IP-адрес ведущего компьютера.");
        try
        {
            var bytes = sync.DownloadPack(host);
            var (ok, msg) = transfer.RestorePackBytes(bytes);
            log.Write("Transfer", $"Перенос по сети с {host}: {msg}");
            return (ok, msg);
        }
        catch (Exception ex)
        {
            log.Write("Transfer", "Net: " + ex.Message);
            return (false, "Не удалось перенести: " + ex.Message +
                " Проверьте брандмауэр/антивирус на обоих ПК и изоляцию Wi-Fi в роутере.");
        }
    }
}
