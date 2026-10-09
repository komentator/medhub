using System.IO;
using MedControl.Data;
using Microsoft.Data.Sqlite;

namespace MedControl.Logging;

// §33. Технический журнал без медицинских данных, ротация 30 дней.
public sealed class LogService
{
    private readonly Db _db;
    private readonly string _file;
    public LogService(Db db)
    {
        _db = db;
        _file = Path.Combine(Paths.LogDir, "medcontrol.log");
    }
    public void Write(string category, string message)
    {
        try
        {
            using var c = _db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO TechnicalLogs(CreatedAt,Category,Message) VALUES($t,$c,$m)";
            cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$c", category);
            cmd.Parameters.AddWithValue("$m", message.Length > 2000 ? message[..2000] : message);
            cmd.ExecuteNonQuery();
            using var cmd2 = c.CreateCommand();
            cmd2.CommandText = "DELETE FROM TechnicalLogs WHERE CreatedAt < $d";
            cmd2.Parameters.AddWithValue("$d", DateTime.Now.AddDays(-30).ToString("O"));
            cmd2.ExecuteNonQuery();
        }
        catch { /* лог не должен ронять приложение */ }
        try { File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{category}] {message}{Environment.NewLine}"); } catch { }
    }
}
