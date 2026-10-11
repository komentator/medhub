using MedControl.Data;
using MedControl.Models;

namespace MedControl.Repositories;

static class Row
{
    public static string S(object o) => o == DBNull.Value ? "" : Convert.ToString(o) ?? "";
    public static DateTime DT(object o) => DateTime.Parse(Convert.ToString(o) ?? DateTime.Now.ToString("O"));
    public static DateTime? DTN(object o) => o == DBNull.Value || string.IsNullOrEmpty(Convert.ToString(o)) ? null : DateTime.Parse(Convert.ToString(o)!);
}

public sealed class MedicationRepository
{
    private readonly Db _db;
    public MedicationRepository(Db db) => _db = db;

    public List<Medication> GetAll(bool includeInactive = true)
    {
        var list = new List<Medication>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,Description,IsActive,Mode,DaysOfWeek,StartDate,EndDate,UpdatedAt,PhotoPack,PhotoPill,PhotoInstruction,InstructionText FROM Medications" + (includeInactive ? "" : " WHERE IsActive=1");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Medication
            {
                Id = Guid.Parse(r.GetString(0)),
                Name = r.GetString(1),
                Description = Row.S(r.GetValue(2)),
                IsActive = r.GetInt32(3) == 1,
                Mode = (ScheduleMode)r.GetInt32(4),
                DaysOfWeek = r.GetString(5).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                StartDate = string.IsNullOrEmpty(Row.S(r.GetValue(6))) ? null : DateOnly.Parse(Row.S(r.GetValue(6))),
                EndDate = string.IsNullOrEmpty(Row.S(r.GetValue(7))) ? null : DateOnly.Parse(Row.S(r.GetValue(7))),
                UpdatedAt = Row.DTN(r.GetValue(8)),
                PhotoPackPath = Row.S(r.GetValue(9)),
                PhotoPillPath = Row.S(r.GetValue(10)),
                PhotoInstructionPath = Row.S(r.GetValue(11)),
                InstructionText = Row.S(r.GetValue(12)),
            });
        }
        foreach (var m in list) m.Doses = GetDoses(m.Id);
        return list;
    }

    private List<MedicationDose> GetDoses(Guid medId)
    {
        var list = new List<MedicationDose>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,MedicationId,Time,Dosage,UpdatedAt FROM MedicationDoses WHERE MedicationId=$m ORDER BY Time";
        cmd.Parameters.AddWithValue("$m", medId.ToString());
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MedicationDose
            {
                Id = Guid.Parse(r.GetString(0)),
                MedicationId = Guid.Parse(r.GetString(1)),
                Time = TimeOnly.Parse(r.GetString(2)),
                Dosage = r.GetString(3),
                UpdatedAt = Row.DTN(r.GetValue(4)),
            });
        return list;
    }

    public void Save(Medication m)
    {
        var stamp = m.UpdatedAt ?? DateTime.Now;
        m.UpdatedAt = stamp;
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO Medications(Id,Name,Description,IsActive,Mode,DaysOfWeek,StartDate,EndDate,UpdatedAt,PhotoPack,PhotoPill,PhotoInstruction,InstructionText)
 VALUES($id,$n,$d,$a,$mo,$dw,$s,$e,$u,$pp,$pl,$pi,$it) ON CONFLICT(Id) DO UPDATE SET Name=$n,Description=$d,IsActive=$a,Mode=$mo,DaysOfWeek=$dw,StartDate=$s,EndDate=$e,UpdatedAt=$u,PhotoPack=$pp,PhotoPill=$pl,PhotoInstruction=$pi,InstructionText=$it";
        cmd.Parameters.AddWithValue("$id", m.Id.ToString());
        cmd.Parameters.AddWithValue("$n", m.Name.Trim());
        cmd.Parameters.AddWithValue("$d", m.Description ?? "");
        cmd.Parameters.AddWithValue("$a", m.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$mo", (int)m.Mode);
        cmd.Parameters.AddWithValue("$dw", string.Join(",", m.DaysOfWeek));
        cmd.Parameters.AddWithValue("$s", m.StartDate?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$e", m.EndDate?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$u", stamp.ToString("O"));
        cmd.Parameters.AddWithValue("$pp", m.PhotoPackPath ?? "");
        cmd.Parameters.AddWithValue("$pl", m.PhotoPillPath ?? "");
        cmd.Parameters.AddWithValue("$pi", m.PhotoInstructionPath ?? "");
        cmd.Parameters.AddWithValue("$it", m.InstructionText ?? "");
        cmd.ExecuteNonQuery();
        using var del = c.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM MedicationDoses WHERE MedicationId=$m";
        del.Parameters.AddWithValue("$m", m.Id.ToString());
        del.ExecuteNonQuery();
        foreach (var d in m.Doses)
        {
            d.MedicationId = m.Id;
            d.UpdatedAt = stamp;
            using var ic = c.CreateCommand();
            ic.Transaction = tx;
            ic.CommandText = "INSERT INTO MedicationDoses(Id,MedicationId,Time,Dosage,UpdatedAt) VALUES($id,$m,$t,$dg,$u)";
            ic.Parameters.AddWithValue("$id", d.Id.ToString());
            ic.Parameters.AddWithValue("$m", m.Id.ToString());
            ic.Parameters.AddWithValue("$t", d.Time.ToString("HH:mm"));
            ic.Parameters.AddWithValue("$dg", d.Dosage ?? "");
            ic.Parameters.AddWithValue("$u", stamp.ToString("O"));
            ic.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // Замена доз из синхронизации (без сдвига UpdatedAt лекарства).
    public void ReplaceDoses(Guid medId, IEnumerable<MedicationDose> doses)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var del = c.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM MedicationDoses WHERE MedicationId=$m";
        del.Parameters.AddWithValue("$m", medId.ToString());
        del.ExecuteNonQuery();
        foreach (var d in doses)
        {
            using var ic = c.CreateCommand();
            ic.Transaction = tx;
            ic.CommandText = "INSERT INTO MedicationDoses(Id,MedicationId,Time,Dosage,UpdatedAt) VALUES($id,$m,$t,$dg,$u)";
            ic.Parameters.AddWithValue("$id", d.Id.ToString());
            ic.Parameters.AddWithValue("$m", medId.ToString());
            ic.Parameters.AddWithValue("$t", d.Time.ToString("HH:mm"));
            ic.Parameters.AddWithValue("$dg", d.Dosage ?? "");
            ic.Parameters.AddWithValue("$u", (d.UpdatedAt ?? DateTime.Now).ToString("O"));
            ic.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Delete(Guid id)
    {
        // Удаление лекарства не удаляет историю (§8.2) — только само лекарство и дозы.
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM MedicationDoses WHERE MedicationId=$m; DELETE FROM Medications WHERE Id=$m";
        cmd.Parameters.AddWithValue("$m", id.ToString());
        cmd.ExecuteNonQuery();
    }
}

public sealed class IntakeRepository
{
    private readonly Db _db;
    public IntakeRepository(Db db) => _db = db;

    public List<MedicationIntake> GetRange(DateTime from, DateTime to, bool includeDeleted = false)
    {
        var list = new List<MedicationIntake>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,MedicationId,MedicationName,MedicationDescription,Dosage,ScheduledAt,ActualAt,Status,ConfirmMethod,Comment,IsDeleted,CreatedAt,DeletedAt,RestoredAt,ShownAt,SnoozedAt,UpdatedAt FROM MedicationIntakes WHERE ScheduledAt>=$f AND ScheduledAt<$t" + (includeDeleted ? "" : " AND IsDeleted=0") + " ORDER BY ScheduledAt";
        cmd.Parameters.AddWithValue("$f", from.ToString("O"));
        cmd.Parameters.AddWithValue("$t", to.ToString("O"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MedicationIntake
            {
                Id = Guid.Parse(r.GetString(0)),
                MedicationId = Guid.Parse(r.GetString(1)),
                MedicationName = r.GetString(2),
                MedicationDescription = Row.S(r.GetValue(3)),
                Dosage = Row.S(r.GetValue(4)),
                ScheduledAt = Row.DT(r.GetValue(5)),
                ActualAt = Row.DTN(r.GetValue(6)),
                Status = (IntakeStatus)r.GetInt32(7),
                ConfirmMethod = (ConfirmMethod)r.GetInt32(8),
                Comment = Row.S(r.GetValue(9)),
                IsDeleted = r.GetInt32(10) == 1,
                CreatedAt = Row.DT(r.GetValue(11)),
                DeletedAt = Row.DTN(r.GetValue(12)),
                RestoredAt = Row.DTN(r.GetValue(13)),
                ShownAt = Row.DTN(r.GetValue(14)),
                SnoozedAt = Row.DTN(r.GetValue(15)),
                UpdatedAt = Row.DTN(r.GetValue(16)),
            });
        }
        return list;
    }

    public List<MedicationIntake> GetTrash() => GetAllDeleted();
    public List<MedicationIntake> GetAllDeleted()
    {
        var list = new List<MedicationIntake>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,MedicationId,MedicationName,MedicationDescription,Dosage,ScheduledAt,ActualAt,Status,ConfirmMethod,Comment,IsDeleted,CreatedAt,DeletedAt,RestoredAt,ShownAt,SnoozedAt,UpdatedAt FROM MedicationIntakes WHERE IsDeleted=1 ORDER BY ScheduledAt DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MedicationIntake
            {
                Id = Guid.Parse(r.GetString(0)), MedicationId = Guid.Parse(r.GetString(1)),
                MedicationName = r.GetString(2), MedicationDescription = Row.S(r.GetValue(3)), Dosage = Row.S(r.GetValue(4)),
                ScheduledAt = Row.DT(r.GetValue(5)), ActualAt = Row.DTN(r.GetValue(6)),
                Status = (IntakeStatus)r.GetInt32(7), ConfirmMethod = (ConfirmMethod)r.GetInt32(8),
                Comment = Row.S(r.GetValue(9)), IsDeleted = true, CreatedAt = Row.DT(r.GetValue(11)),
                DeletedAt = Row.DTN(r.GetValue(12)), RestoredAt = Row.DTN(r.GetValue(13)),
                ShownAt = Row.DTN(r.GetValue(14)), SnoozedAt = Row.DTN(r.GetValue(15)),
                UpdatedAt = Row.DTN(r.GetValue(16)),
            });
        }
        return list;
    }

    public void Upsert(MedicationIntake i)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        i.UpdatedAt ??= DateTime.Now;
        cmd.CommandText = @"INSERT INTO MedicationIntakes(Id,MedicationId,MedicationName,MedicationDescription,Dosage,ScheduledAt,ActualAt,Status,ConfirmMethod,Comment,IsDeleted,CreatedAt,DeletedAt,RestoredAt,ShownAt,SnoozedAt,UpdatedAt)
 VALUES($id,$mid,$mn,$md,$dg,$s,$a,$st,$cm,$co,$del,$cr,$da,$ra,$sh,$sn,$u)
 ON CONFLICT(Id) DO UPDATE SET MedicationId=$mid,MedicationName=$mn,MedicationDescription=$md,Dosage=$dg,ScheduledAt=$s,ActualAt=$a,Status=$st,ConfirmMethod=$cm,Comment=$co,IsDeleted=$del,DeletedAt=$da,RestoredAt=$ra,ShownAt=$sh,SnoozedAt=$sn,UpdatedAt=$u";
        cmd.Parameters.AddWithValue("$id", i.Id.ToString());
        cmd.Parameters.AddWithValue("$mid", i.MedicationId.ToString());
        cmd.Parameters.AddWithValue("$mn", i.MedicationName);
        cmd.Parameters.AddWithValue("$md", i.MedicationDescription ?? "");
        cmd.Parameters.AddWithValue("$dg", i.Dosage ?? "");
        cmd.Parameters.AddWithValue("$s", i.ScheduledAt.ToString("O"));
        cmd.Parameters.AddWithValue("$a", i.ActualAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$st", (int)i.Status);
        cmd.Parameters.AddWithValue("$cm", (int)i.ConfirmMethod);
        cmd.Parameters.AddWithValue("$co", i.Comment ?? "");
        cmd.Parameters.AddWithValue("$del", i.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$cr", i.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$da", i.DeletedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ra", i.RestoredAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$sh", i.ShownAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$sn", i.SnoozedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$u", i.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool Exists(Guid medId, DateTime scheduledAt)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM MedicationIntakes WHERE MedicationId=$m AND ScheduledAt=$s AND IsDeleted=0";
        cmd.Parameters.AddWithValue("$m", medId.ToString());
        cmd.Parameters.AddWithValue("$s", scheduledAt.ToString("O"));
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void MoveToTrash(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE MedicationIntakes SET IsDeleted=1, DeletedAt=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void Restore(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE MedicationIntakes SET IsDeleted=0, RestoredAt=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void DeleteForever(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM MedicationIntakes WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void ClearTrash()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM MedicationIntakes WHERE IsDeleted=1";
        cmd.ExecuteNonQuery();
    }
}

public sealed class MeasurementRepository
{
    private readonly Db _db;
    public MeasurementRepository(Db db) => _db = db;

    public List<Tonometer> GetTonometers(bool includeDeleted = false)
    {
        var list = new List<Tonometer>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,IsDeleted,UpdatedAt FROM Tonometers" + (includeDeleted ? "" : " WHERE IsDeleted=0") + " ORDER BY Name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Tonometer { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), IsDeleted = r.GetInt32(2) == 1, UpdatedAt = Row.DTN(r.GetValue(3)) });
        return list;
    }
    // Порядок добавления (для привязки блоков дневника к тонометрам).
    public List<Tonometer> GetTonometersByCreation()
    {
        var list = new List<Tonometer>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,IsDeleted,UpdatedAt FROM Tonometers WHERE IsDeleted=0 ORDER BY rowid";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Tonometer { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), IsDeleted = r.GetInt32(2) == 1, UpdatedAt = Row.DTN(r.GetValue(3)) });
        return list;
    }
    public void SaveTonometer(Tonometer t)
    {
        t.UpdatedAt ??= DateTime.Now;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO Tonometers(Id,Name,IsDeleted,UpdatedAt) VALUES($id,$n,$d,$u) ON CONFLICT(Id) DO UPDATE SET Name=$n,IsDeleted=$d,UpdatedAt=$u";
        cmd.Parameters.AddWithValue("$id", t.Id.ToString());
        cmd.Parameters.AddWithValue("$n", t.Name.Trim());
        cmd.Parameters.AddWithValue("$d", t.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$u", t.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<Measurement> GetRange(DateTime from, DateTime to, bool includeDeleted = false)
    {
        var list = new List<Measurement>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,TonometerId,TonometerName,TakenAt,Systolic,Diastolic,Pulse,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt FROM Measurements WHERE TakenAt>=$f AND TakenAt<$t" + (includeDeleted ? "" : " AND IsDeleted=0") + " ORDER BY TakenAt";
        cmd.Parameters.AddWithValue("$f", from.ToString("O"));
        cmd.Parameters.AddWithValue("$t", to.ToString("O"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Measurement
            {
                Id = Guid.Parse(r.GetString(0)), TonometerId = Guid.Parse(r.GetString(1)), TonometerName = r.GetString(2),
                TakenAt = Row.DT(r.GetValue(3)), Systolic = r.GetInt32(4), Diastolic = r.GetInt32(5), Pulse = r.GetInt32(6),
                Comment = Row.S(r.GetValue(7)), CreatedAt = Row.DT(r.GetValue(8)), ModifiedAt = Row.DTN(r.GetValue(9)),
                IsDeleted = r.GetInt32(10) == 1, DeletedAt = Row.DTN(r.GetValue(11)), RestoredAt = Row.DTN(r.GetValue(12)),
                UpdatedAt = Row.DTN(r.GetValue(13)),
            });
        }
        return list;
    }
    public List<Measurement> GetTrash()
    {
        var list = new List<Measurement>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,TonometerId,TonometerName,TakenAt,Systolic,Diastolic,Pulse,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt FROM Measurements WHERE IsDeleted=1 ORDER BY TakenAt DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Measurement
            {
                Id = Guid.Parse(r.GetString(0)), TonometerId = Guid.Parse(r.GetString(1)), TonometerName = r.GetString(2),
                TakenAt = Row.DT(r.GetValue(3)), Systolic = r.GetInt32(4), Diastolic = r.GetInt32(5), Pulse = r.GetInt32(6),
                Comment = Row.S(r.GetValue(7)), CreatedAt = Row.DT(r.GetValue(8)), ModifiedAt = Row.DTN(r.GetValue(9)),
                IsDeleted = true, DeletedAt = Row.DTN(r.GetValue(11)), RestoredAt = Row.DTN(r.GetValue(12)),
                UpdatedAt = Row.DTN(r.GetValue(13)),
            });
        }
        return list;
    }
    public void Upsert(Measurement m)
    {
        m.UpdatedAt ??= DateTime.Now;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO Measurements(Id,TonometerId,TonometerName,TakenAt,Systolic,Diastolic,Pulse,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt)
 VALUES($id,$tid,$tn,$t,$s,$d,$p,$co,$cr,$mo,$del,$da,$ra,$u)
 ON CONFLICT(Id) DO UPDATE SET TonometerId=$tid,TonometerName=$tn,Systolic=$s,Diastolic=$d,Pulse=$p,Comment=$co,ModifiedAt=$mo,IsDeleted=$del,DeletedAt=$da,RestoredAt=$ra,UpdatedAt=$u";
        cmd.Parameters.AddWithValue("$id", m.Id.ToString());
        cmd.Parameters.AddWithValue("$tid", m.TonometerId.ToString());
        cmd.Parameters.AddWithValue("$tn", m.TonometerName);
        cmd.Parameters.AddWithValue("$t", m.TakenAt.ToString("O"));
        cmd.Parameters.AddWithValue("$s", m.Systolic);
        cmd.Parameters.AddWithValue("$d", m.Diastolic);
        cmd.Parameters.AddWithValue("$p", m.Pulse);
        cmd.Parameters.AddWithValue("$co", m.Comment ?? "");
        cmd.Parameters.AddWithValue("$cr", m.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$mo", m.ModifiedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$del", m.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$da", m.DeletedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ra", m.RestoredAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$u", m.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public void MoveToTrash(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Measurements SET IsDeleted=1, DeletedAt=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void Restore(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Measurements SET IsDeleted=0, RestoredAt=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void DeleteForever(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM Measurements WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void ClearTrash()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM Measurements WHERE IsDeleted=1";
        cmd.ExecuteNonQuery();
    }
}

public sealed class WellBeingRepository
{
    private readonly Db _db;
    public WellBeingRepository(Db db) => _db = db;

    public List<WellBeingRating> GetRange(DateTime from, DateTime to, bool includeDeleted = false)
    {
        var list = new List<WellBeingRating>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Score,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt FROM WellBeingRatings WHERE CreatedAt>=$f AND CreatedAt<$t" + (includeDeleted ? "" : " AND IsDeleted=0") + " ORDER BY CreatedAt";
        cmd.Parameters.AddWithValue("$f", from.ToString("O"));
        cmd.Parameters.AddWithValue("$t", to.ToString("O"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new WellBeingRating
            {
                Id = Guid.Parse(r.GetString(0)), Score = r.GetInt32(1), Comment = Row.S(r.GetValue(2)),
                CreatedAt = Row.DT(r.GetValue(3)), ModifiedAt = Row.DTN(r.GetValue(4)),
                IsDeleted = r.GetInt32(5) == 1, DeletedAt = Row.DTN(r.GetValue(6)), RestoredAt = Row.DTN(r.GetValue(7)),
                UpdatedAt = Row.DTN(r.GetValue(8)),
            });
        }
        return list;
    }
    public List<WellBeingRating> GetTrash()
    {
        var list = new List<WellBeingRating>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Score,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt FROM WellBeingRatings WHERE IsDeleted=1 ORDER BY CreatedAt DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new WellBeingRating
            {
                Id = Guid.Parse(r.GetString(0)), Score = r.GetInt32(1), Comment = Row.S(r.GetValue(2)),
                CreatedAt = Row.DT(r.GetValue(3)), ModifiedAt = Row.DTN(r.GetValue(4)),
                IsDeleted = true, DeletedAt = Row.DTN(r.GetValue(6)), RestoredAt = Row.DTN(r.GetValue(7)),
                UpdatedAt = Row.DTN(r.GetValue(8)),
            });
        }
        return list;
    }
    public List<WellBeingRating> GetByIds(IEnumerable<Guid> ids)
    {
        var list = new List<WellBeingRating>();
        var arr = ids.ToList();
        if (arr.Count == 0) return list;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        var p = arr.Select((g, i) => { cmd.Parameters.AddWithValue($"$w{i}", g.ToString()); return $"$w{i}"; });
        cmd.CommandText = $"SELECT Id,Score,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt FROM WellBeingRatings WHERE Id IN ({string.Join(",", p)})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new WellBeingRating
            {
                Id = Guid.Parse(r.GetString(0)), Score = r.GetInt32(1), Comment = Row.S(r.GetValue(2)),
                CreatedAt = Row.DT(r.GetValue(3)), ModifiedAt = Row.DTN(r.GetValue(4)),
                IsDeleted = r.GetInt32(5) == 1, DeletedAt = Row.DTN(r.GetValue(6)), RestoredAt = Row.DTN(r.GetValue(7)),
                UpdatedAt = Row.DTN(r.GetValue(8)),
            });
        }
        return list;
    }
    public void Upsert(WellBeingRating w)
    {
        w.UpdatedAt ??= DateTime.Now;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO WellBeingRatings(Id,Score,Comment,CreatedAt,ModifiedAt,IsDeleted,DeletedAt,RestoredAt,UpdatedAt)
 VALUES($id,$s,$co,$cr,$mo,$del,$da,$ra,$u)
 ON CONFLICT(Id) DO UPDATE SET Score=$s,Comment=$co,ModifiedAt=$mo,IsDeleted=$del,DeletedAt=$da,RestoredAt=$ra,UpdatedAt=$u";
        cmd.Parameters.AddWithValue("$id", w.Id.ToString());
        cmd.Parameters.AddWithValue("$s", w.Score);
        cmd.Parameters.AddWithValue("$co", w.Comment ?? "");
        cmd.Parameters.AddWithValue("$cr", w.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$mo", w.ModifiedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$del", w.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$da", w.DeletedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ra", w.RestoredAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$u", w.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public List<WellBeingLink> GetLinks(Guid wellBeingId)
    {
        var list = new List<WellBeingLink>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT WellBeingId,LinkedId,LinkedKind FROM WellBeingLinks WHERE WellBeingId=$w";
        cmd.Parameters.AddWithValue("$w", wellBeingId.ToString());
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new WellBeingLink { WellBeingId = Guid.Parse(r.GetString(0)), LinkedId = Guid.Parse(r.GetString(1)), LinkedKind = r.GetString(2) });
        return list;
    }
    public Dictionary<Guid, List<int>> GetScoresByMeasurement(IEnumerable<Guid> measIds)
    {
        var res = new Dictionary<Guid, List<int>>();
        var ids = measIds.ToList();
        if (ids.Count == 0) return res;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        var p = ids.Select((g, i) => { cmd.Parameters.AddWithValue($"$p{i}", g.ToString()); return $"$p{i}"; });
        cmd.CommandText = $"SELECT l.LinkedId, w.Score FROM WellBeingLinks l JOIN WellBeingRatings w ON w.Id=l.WellBeingId WHERE l.LinkedKind='Measurement' AND l.LinkedId IN ({string.Join(",", p)}) AND w.IsDeleted=0";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            if (!res.TryGetValue(id, out var l)) res[id] = l = new List<int>();
            l.Add(r.GetInt32(1));
        }
        return res;
    }
    public Dictionary<Guid, List<int>> GetScoresByIntake(IEnumerable<Guid> intakeIds)
    {
        var res = new Dictionary<Guid, List<int>>();
        var ids = intakeIds.ToList();
        if (ids.Count == 0) return res;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        var p = ids.Select((g, i) => { cmd.Parameters.AddWithValue($"$q{i}", g.ToString()); return $"$q{i}"; });
        cmd.CommandText = $"SELECT l.LinkedId, w.Score FROM WellBeingLinks l JOIN WellBeingRatings w ON w.Id=l.WellBeingId WHERE l.LinkedKind='Intake' AND l.LinkedId IN ({string.Join(",", p)}) AND w.IsDeleted=0";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            if (!res.TryGetValue(id, out var l)) res[id] = l = new List<int>();
            l.Add(r.GetInt32(1));
        }
        return res;
    }
    public void SetLinks(Guid wbId, IEnumerable<(Guid Id, string Kind)> links)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var del = c.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM WellBeingLinks WHERE WellBeingId=$w";
        del.Parameters.AddWithValue("$w", wbId.ToString());
        del.ExecuteNonQuery();
        foreach (var (id, kind) in links)
        {
            using var ic = c.CreateCommand();
            ic.Transaction = tx;
            ic.CommandText = "INSERT OR IGNORE INTO WellBeingLinks(WellBeingId,LinkedId,LinkedKind) VALUES($w,$l,$k)";
            ic.Parameters.AddWithValue("$w", wbId.ToString());
            ic.Parameters.AddWithValue("$l", id.ToString());
            ic.Parameters.AddWithValue("$k", kind);
            ic.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public void DeleteLinksOf(Guid linkedId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM WellBeingLinks WHERE LinkedId=$l";
        cmd.Parameters.AddWithValue("$l", linkedId.ToString());
        cmd.ExecuteNonQuery();
    }
    public void MoveToTrash(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE WellBeingRatings SET IsDeleted=1, DeletedAt=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void Restore(Guid id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE WellBeingRatings SET IsDeleted=0, RestoredAt=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
    public void DeleteForever(Guid id)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var a = c.CreateCommand(); a.Transaction = tx;
        a.CommandText = "DELETE FROM WellBeingLinks WHERE WellBeingId=$w";
        a.Parameters.AddWithValue("$w", id.ToString()); a.ExecuteNonQuery();
        using var b = c.CreateCommand(); b.Transaction = tx;
        b.CommandText = "DELETE FROM WellBeingRatings WHERE Id=$w";
        b.Parameters.AddWithValue("$w", id.ToString()); b.ExecuteNonQuery();
        tx.Commit();
    }
    public void ClearTrash()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM WellBeingLinks WHERE WellBeingId IN (SELECT Id FROM WellBeingRatings WHERE IsDeleted=1); DELETE FROM WellBeingRatings WHERE IsDeleted=1";
        cmd.ExecuteNonQuery();
    }
}

public sealed record SyncPeer(string DeviceId, string Name, string Address, DateTime? LastSyncAt);

public sealed class SyncPeerRepository
{
    private readonly Db _db;
    public SyncPeerRepository(Db db) => _db = db;

    public List<SyncPeer> GetAll()
    {
        var list = new List<SyncPeer>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DeviceId,Name,Address,LastSyncAt FROM SyncPeers ORDER BY Name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var s = Row.S(r.GetValue(3));
            list.Add(new SyncPeer(r.GetString(0), Row.S(r.GetValue(1)), Row.S(r.GetValue(2)),
                string.IsNullOrEmpty(s) ? null : DateTime.Parse(s)));
        }
        return list;
    }

    public void Touch(string deviceId, string name, string address, DateTime at)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO SyncPeers(DeviceId,Name,Address,LastSyncAt) VALUES($d,$n,$a,$t) ON CONFLICT(DeviceId) DO UPDATE SET Name=$n,Address=$a,LastSyncAt=$t";
        cmd.Parameters.AddWithValue("$d", deviceId);
        cmd.Parameters.AddWithValue("$n", name ?? "");
        cmd.Parameters.AddWithValue("$a", address ?? "");
        cmd.Parameters.AddWithValue("$t", at.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
