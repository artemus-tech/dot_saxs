using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DotSaxs;

/// <summary>
/// Тексты своих исключений (DECISIONS.md п. 32) — из файла <c>Messages.json</c>, где у каждого текста оба языка рядом:
/// <c>en</c> и <c>ru</c>. Язык — из конфига программы (<c>DotSaxs.Language</c>), по умолчанию английский. Тексты
/// перенесённых функций сюда не входят: они всегда английские, слово в слово как в NumPy.
/// </summary>
internal static class Messages
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts = Load();

    internal static string IndexOutOfVector(int index, int length) => Get(nameof(IndexOutOfVector), index, length);

    internal static string RowOutOfMatrix(int row, int rows) => Get(nameof(RowOutOfMatrix), row, rows);

    internal static string ColumnOutOfMatrix(int column, int columns) => Get(nameof(ColumnOutOfMatrix), column, columns);

    internal static string VectorToMatrix(int length, int rows, int columns) => Get(nameof(VectorToMatrix), length, rows, columns);

    internal static string RowLengthsDiffer(int first, int row, int length) => Get(nameof(RowLengthsDiffer), first, row, length);

    internal static string ColumnLengthsDiffer(int first, int column, int length) =>
        Get(nameof(ColumnLengthsDiffer), first, column, length);

    internal static string VectorLengthsDiffer(int first, int vector, int length) =>
        Get(nameof(VectorLengthsDiffer), first, vector, length);

    internal static string NoRows() => Get(nameof(NoRows));

    internal static string NoColumns() => Get(nameof(NoColumns));

    internal static string NoVectors() => Get(nameof(NoVectors));

    internal static string ErrorModeNotSupported(string? mode) => Get(nameof(ErrorModeNotSupported), mode ?? "");

    /// <summary>Текст по ключу на языке из конфига; если перевода на этот язык нет — по-английски.</summary>
    internal static string Get(string key, params object[] args) => Translate(key, Settings.Language, args);

    /// <summary>Текст по ключу на заданном языке; числа — без зависимости от культуры (точка, без пробелов).</summary>
    internal static string Translate(string key, string language, object[] args)
    {
        IReadOnlyDictionary<string, string> text = Texts[key];
        string template = text.TryGetValue(language, out string? translated) ? translated : text["en"];
        return string.Format(CultureInfo.InvariantCulture, template, args);
    }

    /// <summary>Все тексты: ключ → язык → шаблон с местами для чисел <c>{0}</c>, <c>{1}</c>…</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> All => Texts;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        using Stream stream = typeof(Messages).Assembly.GetManifestResourceStream("DotSaxs.Messages.json")
            ?? throw new InvalidDataException("в сборке DotSaxs нет ресурса DotSaxs.Messages.json");
        using JsonDocument document = JsonDocument.Parse(stream);
        var texts = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (JsonProperty message in document.RootElement.EnumerateObject())
        {
            var languages = new Dictionary<string, string>();
            foreach (JsonProperty language in message.Value.EnumerateObject())
                languages[language.Name] = language.Value.GetString() ?? "";
            texts[message.Name] = languages;
        }
        return texts;
    }
}
