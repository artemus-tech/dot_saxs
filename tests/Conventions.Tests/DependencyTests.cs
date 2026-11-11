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
        string[] errors =
        [
            .. ForeignReferences(typeof(OriginalAttribute).Assembly),
            .. ForeignReferences(typeof(Np).Assembly, "DotSaxs.Std"),
            .. ForeignReferences(typeof(SciPy).Assembly, "DotSaxs.Std", "DotSaxs"),
        ];
        if (errors.Length > 0)
            Assert.Fail("Порт должен обходиться без внешних библиотек (SPEC.md п. 5):\n" + string.Join("\n", errors));
    }

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
