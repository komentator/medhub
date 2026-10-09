using NAudio.CoreAudioApi;
using MedControl.Logging;

namespace MedControl.Audio;

// §11. Снижение Master Volume текущего устройства вывода через NAudio, диапазон 0–50%.
public sealed class VolumeService
{
    private readonly LogService _log;
    private MMDevice? _device;          // удерживаем, пока громкость снижена
    private float? _savedVolume;        // исходная громкость до первого duck
    private float _duckTarget = -1;     // целевой уровень нашего снижения (%), -1 = нет
    private bool _suppressOwn;          // синхронное окно нашей собственной установки
    private bool _userChanged;          // пользователь вручную менял громкость

    public VolumeService(LogService log) => _log = log;

    public float? GetCurrent()
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            using var dev = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return dev.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        }
        catch (Exception ex) { _log.Write("Volume", "GetCurrent: " + ex.Message); return null; }
    }

    // Вызывается при активном напоминании: сохранить исходную (один раз) и снизить.
    public void Duck(int percent)
    {
        percent = Math.Clamp(percent, 0, 50);
        try
        {
            if (_device == null)
            {
                using var e = new MMDeviceEnumerator();
                _device = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _savedVolume = _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
                _userChanged = false;
                _duckTarget = -1;
                _device.AudioEndpointVolume.OnVolumeNotification += OnVol;
            }
            // Ручное изменение пользователя не перезаписываем (§11).
            if (_userChanged) return;
            _duckTarget = percent;
            var vol = _device.AudioEndpointVolume;
            _suppressOwn = true;
            try
            {
                vol.MasterVolumeLevelScalar = percent / 100f;
                if (vol.Mute) vol.Mute = false;
            }
            finally { _suppressOwn = false; }
        }
        catch (Exception ex) { _log.Write("Volume", "Duck: " + ex.Message); }
    }

    private void OnVol(AudioVolumeNotificationData data)
    {
        try
        {
            if (_suppressOwn) return;
            // Наша собственная установка тоже приходит уведомлением (асинхронно) —
            // отличаем её по совпадению значения с целевым уровнем.
            if (ShouldIgnoreNotification(data.MasterVolume * 100f, _duckTarget)) return;
            // Иначе громкость изменил пользователь вручную (§11).
            _userChanged = true;
        }
        catch { }
    }

    // Чистая функция для тестов: своё ли это уведомление (совпало с нашим целевым уровнем).
    public static bool ShouldIgnoreNotification(float notifiedPercent, float duckTargetPercent)
        => duckTargetPercent >= 0 && Math.Abs(notifiedPercent - duckTargetPercent) < 1.0f;

    public void Restore()
    {
        var dev = _device;
        _device = null;
        try
        {
            if (dev != null)
            {
                try { dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { }
                // Восстанавливаем только если пользователь не менял громкость вручную:
                // чужое изменение не перезаписываем (§11).
                if (!_userChanged && _savedVolume is float s)
                    dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(s / 100f, 0f, 1f);
                dev.Dispose();
            }
        }
        catch (Exception ex) { _log.Write("Volume", "Restore: " + ex.Message); }
        finally { _savedVolume = null; _duckTarget = -1; _userChanged = false; _suppressOwn = false; }
    }

    public bool IsDucked => _device != null;
}
