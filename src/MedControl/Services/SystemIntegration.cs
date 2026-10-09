using System.IO;
using Microsoft.Win32;
using MedControl.Logging;

namespace MedControl.Services;

// §36 автозапуск (HKCU Run, без скриптов), §38 единственный экземпляр.
public static class AutostartService
{
    private const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "MedControl";
    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) != null;
        }
        catch { return false; }
    }
    public static void Set(bool enable, LogService log)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true)!;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                k.SetValue(Name, $"\"{exe}\" --minimized");
            }
            else k.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception ex) { log.Write("Autostart", ex.Message); }
    }
}

public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\MedControl_SingleInstance";
    public const string ShowEventName = @"Local\MedControl_ShowMain";
    private Mutex? _mutex;
    public bool IsFirst { get; private set; }
    public bool Acquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool created);
            IsFirst = created;
            return created;
        }
        catch (AbandonedMutexException ex)
        {
            // Прошлый процесс убили — мьютекс всё равно наш.
            try { _mutex = ex.Mutex ?? new Mutex(true, MutexName, out _); } catch { }
            IsFirst = true;
            return true;
        }
    }

    // Второй процесс: попросить первый показать окно и тихо выйти.
    public static bool SignalFirst()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowEventName);
            ev.Set();
            return true;
        }
        catch { return false; }
    }

    public void Dispose() { try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { } }
}
