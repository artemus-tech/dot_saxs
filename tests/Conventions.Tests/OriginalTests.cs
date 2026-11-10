using System;
using System.Linq;
using System.Reflection;
using DotSaxs.ConventionsTests.Samples;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>Ссылка на оригинал у каждого публичного члена порта (SPEC.md п. 4).</summary>
public sealed class OriginalTests
{
    private static readonly Assembly[] Port = [typeof(Np).Assembly, typeof(SciPy).Assembly];

    [Fact]
    public void EveryPublicMemberOfThePortHasOriginal()
    {
        string[] missing = Port.SelectMany(OriginalChecks.Missing).ToArray();
        if (missing.Length > 0)
            Assert.Fail("Без [Original] (SPEC.md п. 4):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void CheckFindsEveryMemberWithoutOriginal()
    {
        const string type = "DotSaxs.ConventionsTests.Samples.Unannotated";
        string[] expected = [type, type + "..ctor", type + ".Run", type + ".Value", type + ".op_Addition"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), OriginalChecks.Missing(typeof(Unannotated)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CheckAcceptsFullyAnnotatedType()
    {
        Assert.Empty(OriginalChecks.Missing(typeof(Annotated)));
    }

    [Fact]
    public void OriginalPointsToExistingLineInPinnedSources()
    {
        string root = SourcesRoot();
        string[] errors = Port.SelectMany(OriginalChecks.All)
            .Select(x => OriginalChecks.CheckSource(root, x.Where, x.Original))
            .OfType<string>()
            .ToArray();
        if (errors.Length > 0)
            Assert.Fail("Неверные ссылки [Original]:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void SourceCheckRejectsBrokenReferences()
    {
        string root = SourcesRoot();
        Assert.Null(OriginalChecks.CheckSource(root, "верная", new OriginalAttribute("numpy", "numpy/__init__.py", 2)));
        Assert.NotNull(OriginalChecks.CheckSource(root, "чужая строка", new OriginalAttribute("numpy", "numpy/__init__.py", 1)));
        Assert.NotNull(OriginalChecks.CheckSource(root, "нет строки", new OriginalAttribute("numpy", "numpy/__init__.py", 1_000_000)));
        Assert.NotNull(OriginalChecks.CheckSource(root, "нет файла", new OriginalAttribute("numpy", "numpy/нет_такого.py", 1)));
        Assert.NotNull(OriginalChecks.CheckSource(root, "нет репозитория", new OriginalAttribute("torch", "torch/__init__.py", 1)));
    }

    /// <summary>Каталог исходников из tools/sources/fetch.sh. В CI обязателен, локально без него тест пропускается.</summary>
    private static string SourcesRoot()
    {
        string? root = Environment.GetEnvironmentVariable("DOTSAXS_SOURCES");
        if (string.IsNullOrEmpty(root))
        {
            const string why = "DOTSAXS_SOURCES не задан: исходники оригиналов не загружены (tools/sources/fetch.sh <каталог>)";
            if (Environment.GetEnvironmentVariable("CI") == "true")
                Assert.Fail(why);
            Assert.Skip(why);
        }
        return root!;
    }
}
