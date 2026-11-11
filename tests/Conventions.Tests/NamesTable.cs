using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DotSaxs.ConventionsTests;

/// <summary>Строка таблицы имён names.csv: имя в Python, вид, имя в C#, статус, как это делается у нас, пример вызова,
/// этап плана и примечание (DECISIONS.md п. 11, 21–26).</summary>
internal sealed record NameRow(string Python, string Kind, string CSharp, string Status, string How = "", string Call = "",
    string Stage = "", string Note = "");

/// <summary>Чтение names.csv и разбор CSV (RFC 4180).</summary>
internal static class NamesTable
{
    public const string Ported = "перенесено";
    public const string Planned = "запланировано";
    public const string NotPorted = "не переносится";
    public const string Alias = "псевдоним";

    /// <summary>Способы «как это делается у нас» — те же, что HOWS в tools/names/generate.py.</summary>
    public static readonly string[] Hows =
    [
        "функция числа", "уже есть в .NET", "LINQ", "метод", "метод объекта", "свойство объекта", "создание", "форма",
        "функция", "класс", "объект", "константа", "вид чисел", "исключение", "предупреждение", "модуль", "формат файла",
        "внутри", "не переносится", "решить",
    ];

    /// <summary>Способы, у которых нет примера вызова.</summary>
    public static readonly string[] HowsWithoutCall = ["не переносится", "решить", "внутри", "предупреждение"];

    public static IReadOnlyList<NameRow> Load()
    {
        List<string[]> rows = Csv(File.ReadAllText(Path.Combine(RepoRoot(), "names.csv")));
        string[] header = rows[0];
        int python = Column(header, "python"), kind = Column(header, "kind"), csharp = Column(header, "csharp"), status = Column(header, "status");
        int how = Column(header, "how"), call = Column(header, "call"), stage = Column(header, "stage"), note = Column(header, "note");
        return rows.Skip(1).Select(r => new NameRow(r[python], r[kind], r[csharp], r[status], r[how], r[call], r[stage], r[note])).ToList();
    }

    /// <summary>Разбор CSV: поля в кавычках, удвоенные кавычки, переводы строк внутри кавычек.</summary>
    public static List<string[]> Csv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    field.Append(text[++i]);
                else
                    quoted = false;
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else if (c != '\r')
                field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }
        return rows;
    }

    /// <summary>Корень репозитория — каталог с DotSaxs.slnx выше каталога тестов.</summary>
    public static string RepoRoot()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "DotSaxs.slnx")))
                return dir;
        throw new DirectoryNotFoundException("Не найден корень репозитория (DotSaxs.slnx) выше " + AppContext.BaseDirectory);
    }

    private static int Column(string[] header, string name)
    {
        int index = Array.IndexOf(header, name);
        return index >= 0 ? index : throw new InvalidDataException($"В names.csv нет колонки «{name}»");
    }
}

/// <summary>Проверки таблицы имён против сборок порта (SPEC.md п. 2).</summary>
internal static partial class NameChecks
{
    /// <summary>Каждое имя Python — одна строка; каждое имя C# среди переносимых имён — одно.</summary>
    public static IEnumerable<string> Duplicates(IReadOnlyList<NameRow> rows)
    {
        foreach (var g in rows.GroupBy(r => r.Python).Where(g => g.Count() > 1))
            yield return $"имя Python {g.Key} встречается {g.Count()} раза";
        foreach (var g in rows.Where(r => r.Status is NamesTable.Ported or NamesTable.Planned).GroupBy(r => r.CSharp).Where(g => g.Count() > 1))
            yield return $"имя C# {g.Key} у нескольких имён Python: {string.Join(", ", g.Select(r => r.Python))}";
    }

    /// <summary>
    /// «Перенесено» в таблице ⇔ член есть в сборке с [Original] того же имени Python.
    /// <paramref name="ported"/> — пары (имя в C# без DotSaxs., имя из [Original]) из сборок порта.
    /// </summary>
    public static IEnumerable<string> Mismatches(IReadOnlyList<NameRow> rows, IEnumerable<(string CSharp, string Python)> ported)
    {
        var byPython = rows.ToDictionary(r => r.Python);
        var originals = ported.GroupBy(p => p.CSharp).ToDictionary(g => g.Key, g => g.Select(p => p.Python).Distinct().ToArray());
        foreach (var (csharp, pythons) in originals.Where(o => o.Value.Length > 1))
            yield return $"{csharp}: несколько разных [Original] — {string.Join(", ", pythons)}";
        foreach (NameRow row in rows.Where(r => r.Status == NamesTable.Ported))
        {
            if (!originals.TryGetValue(row.CSharp, out string[]? pythons))
                yield return $"{row.Python}: в names.csv «перенесено», но в сборках порта нет {row.CSharp}";
            else if (!pythons.Contains(row.Python))
                yield return $"{row.CSharp}: [Original(\"{pythons[0]}\")], а по names.csv это {row.Python}";
        }
        foreach (var (csharp, pythons) in originals)
            foreach (string python in pythons.Where(IsPublic))
            {
                if (!byPython.TryGetValue(python, out NameRow? row))
                    yield return $"{csharp}: [Original(\"{python}\")] — такого публичного имени нет в names.csv";
                else if (row.Status != NamesTable.Ported)
                    yield return $"{csharp}: {python} в names.csv со статусом «{row.Status}», а должно быть «перенесено»";
                else if (row.CSharp != csharp)
                    yield return $"{csharp}: по names.csv имя {python} в C# — {row.CSharp}";
            }
    }

    /// <summary>
    /// «Как это делается у нас»: способ из списка, «не переносится» в статусе ⇔ в способе, пример вызова — везде,
    /// где он нужен (DECISIONS.md п. 24–26).
    /// </summary>
    public static IEnumerable<string> WayErrors(IReadOnlyList<NameRow> rows)
    {
        foreach (NameRow row in rows)
        {
            if (!NamesTable.Hows.Contains(row.How))
                yield return $"{row.Python}: неизвестный способ «{row.How}»";
            if (row.Status != NamesTable.Alias && (row.Status == NamesTable.NotPorted) != (row.How == NamesTable.NotPorted))
                yield return $"{row.Python}: статус «{row.Status}», а способ «{row.How}»";
            if (row.Status != NamesTable.Alias && !NamesTable.HowsWithoutCall.Contains(row.How) && row.Call.Length == 0)
                yield return $"{row.Python}: способ «{row.How}», но нет примера вызова";
        }
    }

    /// <summary>
    /// Этап плана (DECISIONS.md п. 25): у того, что не переносится, этапа нет, у остального — номер от 1 до 8;
    /// «этап N» в примечании совпадает со столбцом stage.
    /// </summary>
    public static IEnumerable<string> StageErrors(IReadOnlyList<NameRow> rows)
    {
        foreach (NameRow row in rows)
        {
            if (row.How == NamesTable.NotPorted)
            {
                if (row.Stage.Length > 0)
                    yield return $"{row.Python}: не переносится, но указан этап {row.Stage}";
                continue;
            }
            if (row.Stage is not ("1" or "2" or "3" or "4" or "5" or "6" or "7" or "8"))
                yield return $"{row.Python}: этап «{row.Stage}», нужен номер от 1 до 8";
            foreach (Match match in StageInNote().Matches(row.Note))
                if (match.Groups[1].Value != row.Stage)
                    yield return $"{row.Python}: в примечании этап {match.Groups[1].Value}, а в столбце stage — {row.Stage}";
        }
    }

    [GeneratedRegex(@"этап\w*\s+(\d)")]
    private static partial Regex StageInNote();

    /// <summary>Публичное имя numpy или scipy: ни одна часть не начинается с подчёркивания.</summary>
    private static bool IsPublic(string python) =>
        (python.StartsWith("numpy", StringComparison.Ordinal) || python.StartsWith("scipy", StringComparison.Ordinal))
        && python.Split('.').All(part => !part.StartsWith('_'));
}
