using System;

namespace DotSaxs;

/// <summary>
/// Ссылка на оригинал в Python: полное имя, файл и строка в зафиксированных исходниках (SPEC.md п. 4).
/// </summary>
/// <remarks>
/// Версии и коммиты исходников — в <c>sources.json</c>. Портал показывает по атрибуту оригинал и ссылку на строку,
/// тесты соглашений проверяют, что атрибут есть у каждого публичного члена порта и указывает на существующую строку.
/// </remarks>
/// <param name="name">Полное имя в Python, например <c>numpy.linalg.solve</c>.</param>
/// <param name="file">Путь к файлу от корня репозитория оригинала, например <c>numpy/linalg/_linalg.py</c>.</param>
/// <param name="line">Номер строки (с 1), на которой начинается определение.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate
    | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false, Inherited = false)]
public sealed class OriginalAttribute(string name, string file, int line) : Attribute
{
    /// <summary>Полное имя в Python, например <c>numpy.linalg.solve</c>.</summary>
    public string Name { get; } = name;

    /// <summary>Путь к файлу от корня репозитория оригинала.</summary>
    public string File { get; } = file;

    /// <summary>Номер строки (с 1), на которой начинается определение.</summary>
    public int Line { get; } = line;
}
