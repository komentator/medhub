using System.IO;
namespace MedControl.Data;

public static class Paths
{
    public static string Base => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedControl");
    public static string DataDir => Path.Combine(Base, "Data");
    public static string DbFile => Path.Combine(DataDir, "medcontrol.db");
    public static string BackupDir => Path.Combine(Base, "Backups");
    public static string ReportDir => Path.Combine(Base, "Reports");
    public static string LogDir => Path.Combine(Base, "Logs");
    public static string PhotosDir => Path.Combine(Base, "Photos");
    public static void EnsureAll()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);
        Directory.CreateDirectory(ReportDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(PhotosDir);
    }
}
