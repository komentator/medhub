using System.IO;
using NAudio.Wave;
using MedControl.Logging;

namespace MedControl.Audio;

// Воспроизведение звука напоминания. Используем встроенный системный звук,
// чтобы не таскать wav-файлы; повтор через 10 мин организует ReminderService.
public sealed class SoundService : IDisposable
{
    private readonly LogService _log;
    private WaveOutEvent? _out;
    private WaveStream? _reader;
    public SoundService(LogService log) => _log = log;

    public void Play()
    {
        try
        {
            Stop();
            // Генерируем заметный сигнал 880 Гц, 1.2 c — без внешних файлов.
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
            var samples = (int)(waveFormat.SampleRate * 1.2);
            var buf = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / waveFormat.SampleRate;
                double env = Math.Min(1.0, i / (44100.0 * 0.05)) * Math.Min(1.0, (samples - i) / (44100.0 * 0.2));
                buf[i] = (float)(0.5 * env * Math.Sin(2 * Math.PI * 880 * t));
            }
            var ms = new MemoryStream();
            using (var w = new WaveFileWriter(ms, waveFormat))
                w.WriteSamples(buf, 0, buf.Length);
            ms.Position = 0;
            _reader = new WaveFileReader(ms);
            _out = new WaveOutEvent();
            _out.PlaybackStopped += (_, _) => { try { _reader?.Dispose(); } catch { } };
            _out.Init(_reader);
            _out.Play();
        }
        catch (Exception ex) { _log.Write("Sound", "Play: " + ex.Message); try { System.Media.SystemSounds.Exclamation.Play(); } catch { } }
    }

    public void Stop()
    {
        try { _out?.Stop(); _out?.Dispose(); _out = null; } catch { }
    }
    public void Dispose() => Stop();
}
