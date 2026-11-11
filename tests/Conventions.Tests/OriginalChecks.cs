using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DotSaxs.ConventionsTests;

/// <summary>
/// Проверки атрибута <see cref="OriginalAttribute"/> (SPEC.md п. 4, DECISIONS.md п. 25): он стоит у публичных членов,
/// которые переносят функцию NumPy или SciPy, и сверяется с names.csv. У своих методов без оригинала (GetRow, Select)
/// атрибута нет.
/// </summary>
internal static partial class OriginalChecks
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Все атрибуты [Original] сборки: где стоят, короткое имя типа или члена и сам атрибут.</summary>
    public static IEnumerable<(string Where, string Member, OriginalAttribute Original)> All(Assembly assembly) =>
        assembly.GetExportedTypes().SelectMany(t => All(t));

    /// <summary>Атрибуты [Original] одного типа и его членов.</summary>
    public static IEnumerable<(string Where, string Member, OriginalAttribute Original)> All(Type type)
    {
        if (type.GetCustomAttribute<OriginalAttribute>() is { } own)
            yield return (DisplayName(type), ShortName(type), own);
        foreach (MemberInfo member in OwnMembers(type))
            if (member.GetCustomAttribute<OriginalAttribute>() is { } original)
                yield return (DisplayName(type) + "." + member.Name, member.Name, original);
    }

    /// <summary>
    /// Ошибки сверки с names.csv: имя из [Original] есть в таблице и переносится, а наш член назван в её примере вызова
    /// или примечании — так таблица говорит, что этот метод переносит эту функцию.
    /// </summary>
    public static IEnumerable<string> TableErrors(
        IEnumerable<(string Where, string Member, OriginalAttribute Original)> originals, IReadOnlyList<NameRow> table)
    {
        Dictionary<string, NameRow> rows = table.ToDictionary(r => r.Python, StringComparer.Ordinal);
        foreach ((string where, string member, OriginalAttribute original) in originals)
        {
            if (!rows.TryGetValue(original.Name, out NameRow? row))
                yield return $"{where}: «{original.Name}» нет в names.csv";
            else if (row.How == NamesTable.NotPorted)
                yield return $"{where}: «{original.Name}» в names.csv не переносится";
            else if (!row.Call.Contains(member, StringComparison.Ordinal) && !row.Note.Contains(member, StringComparison.Ordinal))
                yield return $"{where}: в names.csv у «{original.Name}» ни пример вызова, ни примечание не называют {member}";
        }
    }

    /// <summary>
    /// Методы без оригинала, хотя names.csv говорит, что этим методом переносится функция («метод» с вызовом
    /// <c>a.Sum()</c>): хотя бы у одного метода с таким именем должен быть [Original] на одну из этих функций.
    /// </summary>
    public static IEnumerable<string> MissingOriginals(IEnumerable<Type> types, IReadOnlyList<NameRow> table)
    {
        Dictionary<string, HashSet<string>> ported = new(StringComparer.Ordinal);
        foreach (NameRow row in table.Where(r => r.How == "метод" && r.Status != NamesTable.NotPorted))
            foreach (Match call in MethodCall().Matches(row.Call))
            {
                if (!ported.TryGetValue(call.Groups[1].Value, out HashSet<string>? names))
                    ported[call.Groups[1].Value] = names = new HashSet<string>(StringComparer.Ordinal);
                names.Add(row.Python);
            }
        var methods = types
            .SelectMany(t => t.GetMethods(Declared).Where(m => !m.IsSpecialName && !m.IsDefined(typeof(CompilerGeneratedAttribute))))
            .Where(m => ported.ContainsKey(m.Name))
            .GroupBy(m => m.Name, StringComparer.Ordinal);
        foreach (IGrouping<string, MethodInfo> group in methods)
        {
            HashSet<string> names = ported[group.Key];
            if (!group.Any(m => m.GetCustomAttribute<OriginalAttribute>() is { } original && names.Contains(original.Name)))
                yield return $"{group.Key}: по names.csv так переносится {string.Join(", ", names.Order(StringComparer.Ordinal))}, " +
                             "но [Original] нет ни у одного метода с этим именем";
        }
    }

    /// <summary>
    /// Ошибка ссылки или <c>null</c>: файл должен существовать в исходниках на закреплённом коммите,
    /// строка — быть в файле и содержать последнюю часть имени.
    /// </summary>
    public static string? CheckSource(string sourcesRoot, string where, OriginalAttribute original)
    {
        string repo = original.Name.Split('.')[0];
        if (repo is not ("numpy" or "scipy"))
            return $"{where}: неизвестный репозиторий «{repo}» в имени «{original.Name}»";
        string path = Path.Combine(sourcesRoot, repo, original.File);
        if (!File.Exists(path))
            return $"{where}: файла {repo}/{original.File} нет";
        string[] lines = File.ReadAllLines(path);
        if (original.Line < 1 || original.Line > lines.Length)
            return $"{where}: в {repo}/{original.File} нет строки {original.Line} (всего строк: {lines.Length})";
        string shortName = original.Name.Split('.')[^1];
        string text = lines[original.Line - 1];
        return text.Contains(shortName, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{where}: строка {original.Line} в {repo}/{original.File} не содержит «{shortName}»: {text.Trim()}";
    }

    /// <summary>Имя метода в примере вызова: <c>a.Sum()</c> → Sum, <c>m.GetRows().Select(r => r.Diff())</c> → GetRows, Diff.</summary>
    [GeneratedRegex(@"\.(\w+)\(")]
    private static partial Regex MethodCall();

    private static string ShortName(Type type) => type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;

    private static IEnumerable<MemberInfo> OwnMembers(Type type) =>
        type.GetMembers(Declared).Where(m =>
            m is not Type
            && !(m is MethodInfo { IsSpecialName: true } method && !method.Name.StartsWith("op_", StringComparison.Ordinal))
            && !(m is FieldInfo { IsSpecialName: true })
            && !m.IsDefined(typeof(CompilerGeneratedAttribute)));

    private static string DisplayName(Type type) => type.FullName!.Replace('+', '.');
}
