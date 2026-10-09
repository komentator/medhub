using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MedControl.Data;
using MedControl.Logging;
using Image = System.Windows.Controls.Image;

namespace MedControl.Services;

// Фото упаковки/таблеток/инструкций: храним копии в %LOCALAPPDATA%\MedControl\Photos\.
public sealed class PhotoService
{
    private readonly LogService _log;
    private static readonly string[] Exts = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
    public PhotoService(LogService log) => _log = log;

    public static bool IsImage(string path)
        => Exts.Contains(Path.GetExtension(path).ToLowerInvariant());

    // Копирует выбранный файл в хранилище, возвращает относительное имя ("" — ошибка).
    public string Import(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath) || !IsImage(sourcePath)) return "";
            Paths.EnsureAll();
            var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
            File.Copy(sourcePath, Path.Combine(Paths.PhotosDir, name));
            return name;
        }
        catch (Exception ex) { _log.Write("Photo", "Import: " + ex.Message); return ""; }
    }

    public string FullPath(string rel)
        => string.IsNullOrEmpty(rel) ? "" : Path.Combine(Paths.PhotosDir, rel);

    public void Delete(string rel)
    {
        try
        {
            if (!string.IsNullOrEmpty(rel))
                File.Delete(Path.Combine(Paths.PhotosDir, rel));
        }
        catch (Exception ex) { _log.Write("Photo", "Delete: " + ex.Message); }
    }

    public bool Exists(string rel)
        => !string.IsNullOrEmpty(rel) && File.Exists(Path.Combine(Paths.PhotosDir, rel));

    public void Show(Window owner, string rel, string title)
    {
        var full = FullPath(rel);
        if (!Exists(rel)) return;
        var w = new Window
        {
            Owner = owner, Title = title, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 640, Height = 640, ShowInTaskbar = false,
        };
        try
        {
            var img = new Image { Stretch = System.Windows.Media.Stretch.Uniform };
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(full);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            img.Source = bmp;
            w.Content = new ScrollViewer
            {
                Content = img,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            w.ShowDialog();
        }
        catch (Exception ex) { _log.Write("Photo", "Show: " + ex.Message); }
    }
}
