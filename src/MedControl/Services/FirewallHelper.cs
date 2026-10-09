using System.Diagnostics;

namespace MedControl.Services;

// Брандмауэр Windows блокирует входящие подключения к серверу синхронизации.
// Правило добавляется один раз через UAC (netsh требует администратора).
// Антивирусы со своим сетевым экраном правило Windows могут игнорировать —
// тогда разрешение дают вручную в самом антивирусе (см. подсказку в окне).
public static class FirewallHelper
{
    public const string RuleName = "MedControl (LAN sync)";

    public static bool RuleExists()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RuleName}\"")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return o.Contains(RuleName);
        }
        catch { return false; }
    }

    public static (bool Ok, string Msg) EnsureRule()
    {
        if (RuleExists()) return (true, "Правило брандмауэра уже есть.");
        var exe = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exe)) return (false, "Не найден путь приложения.");
        // profile=any: домашний Wi-Fi часто определён как «общедоступная» сеть.
        // Разрешаем только нашу программу (program=...), а не весь трафик.
        var args = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" enable=yes profile=any";
        try
        {
            var psi = new ProcessStartInfo("netsh", args) { Verb = "runas", UseShellExecute = true };
            using var p = Process.Start(psi);
            p?.WaitForExit(60000);
            return RuleExists() ? (true, "MedControl разрешён в брандмауэре.") : (false, "Не подтверждено.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Отменено: без подтверждения администратора правило не добавить.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
