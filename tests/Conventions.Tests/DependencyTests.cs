using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>Порт обходится без внешних библиотек (SPEC.md п. 5) и берёт из Std только то, что разрешено слоями (п. 10).</summary>
public sealed class DependencyTests
{
    [Fact]
    public void PortHasNoExternalDependencies()
    {
        string[] own = [.. Projects.Port, .. Projects.Library];
        string[] errors = Projects.Load(own)
            .SelectMany(a => ForeignReferences(a, Allowed(a.GetName().Name!, own)))
            .ToArray();
        if (errors.Length > 0)
            Assert.Fail("Порт должен обходиться без внешних библиотек (SPEC.md п. 5):\n" + string.Join("\n", errors));
    }

    /// <summary>Слои (SPEC.md п. 10): Std ни на что не ссылается, Np — только на Std, остальные — на сборки библиотеки.</summary>
    private static string[] Allowed(string assembly, string[] own) => assembly switch
    {
        "DotSaxs.Std" => [],
        "DotSaxs.Np" => ["DotSaxs.Std"],
        _ => own.Where(n => n != assembly).ToArray(),
    };

    [Fact]
    public void CheckFindsExternalDependency()
    {
        Assert.Contains(ForeignReferences(typeof(DependencyTests).Assembly), e => e.Contains("xunit", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ссылки сборки на всё, кроме базовой библиотеки .NET и разрешённых слоёв порта.</summary>
    private static IEnumerable<string> ForeignReferences(Assembly assembly, params string[] allowed) =>
        from reference in assembly.GetReferencedAssemblies()
        let name = reference.Name!
        where !IsBaseLibrary(name) && !allowed.Contains(name)
        select $"{assembly.GetName().Name} → {name}";

    private static bool IsBaseLibrary(string name) =>
        name is "System" or "netstandard" or "mscorlib" || name.StartsWith("System.", StringComparison.Ordinal);
}
