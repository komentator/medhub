namespace MedControl.Services;

// §18. Чистая валидация, покрыта тестами.
public static class ValidationService
{
    public static (bool Ok, string? Error) ValidatePressure(int sys, int dia)
    {
        if (sys == 0 && dia == 0) return (true, null);
        if (sys == 0 || dia == 0)
            return (false, "Допустимо только 0/0. Запрещены сочетания вида 120/0 или 0/80.");
        if (sys < 50 || sys > 300) return (false, "Верхнее давление должно быть 50–300 мм рт. ст.");
        if (dia < 30 || dia > 200) return (false, "Нижнее давление должно быть 30–200 мм рт. ст.");
        if (sys <= dia) return (false, "Верхнее давление должно быть строго больше нижнего (кроме 0/0).");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidatePulse(int pulse)
    {
        if (pulse == 0) return (true, null); // 0 разрешён независимо
        if (pulse < 20 || pulse > 250) return (false, "Пульс должен быть 20–250 уд/мин (0 — допустимо).");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateMedication(string name, string dosage)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "Укажите название лекарства.");
        if (name.Trim().Length > 100) return (false, "Название — до 100 символов.");
        if ((dosage ?? "").Length > 100) return (false, "Дозировка — до 100 символов.");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateTonometer(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "Укажите название тонометра.");
        if (name.Trim().Length > 50) return (false, "Название тонометра — до 50 символов.");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateWellBeing(int score)
    {
        if (score < 1 || score > 10) return (false, "Оценка самочувствия — от 1 до 10.");
        return (true, null);
    }
}
