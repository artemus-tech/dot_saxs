using System;
using System.Linq;
using System.Reflection;
using DotSaxs.ConventionsTests.Samples;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>
/// Ссылка на оригинал (SPEC.md п. 4, DECISIONS.md п. 25): [Original] у публичных членов, которые переносят функцию
/// NumPy или SciPy, сверяется с names.csv; у своих методов без оригинала (GetRow, Select) атрибута нет.
/// </summary>
public sealed class OriginalTests
{
    private static readonly Assembly[] Port = [typeof(Np).Assembly, typeof(SciPy).Assembly];

    [Fact]
    public void OriginalsMatchNamesTable()
    {
        string[] errors = OriginalChecks.TableErrors(Port.SelectMany(a => OriginalChecks.All(a)), NamesTable.Load()).ToArray();
        if (errors.Length > 0)
            Assert.Fail("[Original] не сходится с names.csv (DECISIONS.md п. 25):\n" + string.Join("\n", errors));
    }

    [Fact]
    public void MethodsThatPortFunctionsCarryOriginal()
    {
        string[] errors = OriginalChecks.MissingOriginals(Port.SelectMany(a => a.GetExportedTypes()), NamesTable.Load()).ToArray();
        if (errors.Length > 0)
            Assert.Fail("Нет [Original] (DECISIONS.md п. 25):\n" + string.Join("\n", errors));
    }

    [Fact]
    public void TableCheckFindsUnknownNotPortedAndUnmentioned()
    {
        string[] errors = OriginalChecks.TableErrors(OriginalChecks.All(typeof(Mislabeled)), NamesTable.Load()).ToArray();
        Assert.Equal(3, errors.Length);
        Assert.Contains(errors, e => e.Contains("«numpy.no_such_function» нет в names.csv", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("«numpy.swapaxes» в names.csv не переносится", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("не называют Total", StringComparison.Ordinal));
    }

    [Fact]
    public void TableCheckAcceptsMembersNamedInCallOrNote()
    {
        Assert.Empty(OriginalChecks.TableErrors(OriginalChecks.All(typeof(WellLabeled)), NamesTable.Load()));
    }

    [Fact]
    public void MissingCheckFindsPortedMethodWithoutOriginal()
    {
        string error = Assert.Single(OriginalChecks.MissingOriginals([typeof(Unlabeled)], NamesTable.Load()));
        Assert.StartsWith("Diff: по names.csv так переносится numpy.diff", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginalPointsToExistingLineInPinnedSources()
    {
        string root = SourcesRoot();
        string[] errors = Port.SelectMany(a => OriginalChecks.All(a))
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
