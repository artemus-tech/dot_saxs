using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotSaxs.ConventionsTests;

/// <summary>Проверки атрибута <see cref="OriginalAttribute"/> (SPEC.md п. 4).</summary>
internal static class OriginalChecks
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Публичный тип и его публичные члены без [Original]. Аксессоры свойств проверяются через само свойство.</summary>
    public static IEnumerable<string> Missing(Type type)
    {
        if (type.GetCustomAttribute<OriginalAttribute>() is null)
            yield return DisplayName(type);
        foreach (MemberInfo member in OwnMembers(type))
            if (member.GetCustomAttribute<OriginalAttribute>() is null)
                yield return DisplayName(type) + "." + member.Name;
    }

    /// <summary>Все публичные типы сборки и их члены без [Original].</summary>
    public static IEnumerable<string> Missing(Assembly assembly) => assembly.GetExportedTypes().SelectMany(Missing);

    /// <summary>Все атрибуты [Original] сборки вместе с местом, где они стоят.</summary>
    public static IEnumerable<(string Where, OriginalAttribute Original)> All(Assembly assembly)
    {
        foreach (Type type in assembly.GetExportedTypes())
        {
            if (type.GetCustomAttribute<OriginalAttribute>() is { } own)
                yield return (DisplayName(type), own);
            foreach (MemberInfo member in OwnMembers(type))
                if (member.GetCustomAttribute<OriginalAttribute>() is { } original)
                    yield return (DisplayName(type) + "." + member.Name, original);
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

    private static IEnumerable<MemberInfo> OwnMembers(Type type) =>
        type.GetMembers(Declared).Where(m =>
            m is not Type
            && !(m is MethodInfo { IsSpecialName: true } method && !method.Name.StartsWith("op_", StringComparison.Ordinal))
            && !(m is FieldInfo { IsSpecialName: true })
            && !m.IsDefined(typeof(CompilerGeneratedAttribute)));

    private static string DisplayName(Type type) => type.FullName!.Replace('+', '.');
}
