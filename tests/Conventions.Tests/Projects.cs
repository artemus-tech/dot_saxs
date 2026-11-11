using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace DotSaxs.ConventionsTests;

/// <summary>
/// Проекты из src и что с ними проверяют тесты соглашений. Список сверяется с src/*/*.csproj
/// (<see cref="ProjectsTests"/>): новый проект без категории — ошибка, и ни одна сборка не выпадает из проверки.
/// </summary>
internal static class Projects
{
    /// <summary>Перенос NumPy и SciPy: [Original] у публичных членов (SPEC.md п. 4), без внешних зависимостей (п. 5).</summary>
    public static readonly string[] Port = ["DotSaxs", "DotSaxs.SciPy", "DotSaxs.Sparse"];

    /// <summary>
    /// Библиотека, но не перенос: без внешних зависимостей; [Original] у Std — только там, где поведение 1:1 с CPython
    /// (DECISIONS.md п. 16), у Extensions не нужен (п. 24).
    /// </summary>
    public static readonly string[] Library = ["DotSaxs.Std", "DotSaxs.Extensions"];

    /// <summary>Служебные проекты — база и портал (SPEC.md п. 9): им можно пакеты NuGet, порт на них не ссылается.</summary>
    public static readonly string[] Service = ["DotSaxs.Data", "DotSaxs.Portal"];

    /// <summary>Сборки по именам; проекты подключены к тестам ссылками, их сборки лежат рядом.</summary>
    public static Assembly[] Load(IEnumerable<string> names) => names.Select(n => Assembly.Load(new AssemblyName(n))).ToArray();

    /// <summary>
    /// Имена сборок всех проектов из src, и во вложенных папках (сборка на формат файла — src/Formats/…, п. 24):
    /// AssemblyName из .csproj, без него — имя проекта.
    /// </summary>
    public static string[] InSource()
    {
        string src = Path.Combine(NamesTable.RepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) == ".csproj"
                           && !Path.GetRelativePath(src, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => XDocument.Load(path).Descendants("AssemblyName").FirstOrDefault()?.Value
                            ?? Path.GetFileNameWithoutExtension(path))
            .ToArray();
    }
}
