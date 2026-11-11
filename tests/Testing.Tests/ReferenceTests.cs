using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace DotSaxs.Testing.Tests;

/// <summary>Эталоны tests/references: окружение закреплено, все кейсы читаются, данные такие, как записал NumPy.</summary>
public sealed class ReferenceTests
{
    [Fact]
    public void EnvironmentIsPinned()
    {
        ReferenceEnvironment env = References.ReadEnvironment();
        using JsonDocument sources = JsonDocument.Parse(File.ReadAllText(Path.Combine(References.RepoRoot, "sources.json")));
        JsonElement pins = sources.RootElement;
        Assert.Equal(pins.GetProperty("numpy").GetProperty("version").GetString(), env.Numpy);
        Assert.Equal(pins.GetProperty("scipy").GetProperty("version").GetString(), env.Scipy);
        Assert.Equal(pins.GetProperty("openblas").GetProperty("version").GetString(),
            Assert.Single(env.OpenBlas, b => b.Library == "numpy.libs").Version);

        // Параметры процессора из DECISIONS.md п. 4: без них эталоны зависели бы от машины.
        Assert.Equal("X86_V4 AVX512_ICL AVX512_SPR", env.Params["NPY_DISABLE_CPU_FEATURES"]);
        Assert.Equal("Haswell", env.Params["OPENBLAS_CORETYPE"]);
        Assert.Equal("1", env.Params["OPENBLAS_NUM_THREADS"]);
        Assert.Empty(env.SimdFound.Intersect(["X86_V4", "AVX512_ICL", "AVX512_SPR"]));
        Assert.All(env.OpenBlas, b => Assert.Equal(("Haswell", 1), (b.Architecture, b.Threads)));
    }

    [Fact]
    public void EveryCaseLoads()
    {
        IReadOnlyList<string> functions = References.Functions();
        Assert.NotEmpty(functions);
        var errors = new List<string>();
        foreach (string python in functions)
        {
            IReadOnlyList<ReferenceCase> cases = References.Load(python);
            if (cases.Count == 0)
                errors.Add($"{python}: нет кейсов");
            foreach (ReferenceCase c in cases.Where(x => (x.Result is null) == (x.Error is null)))
                errors.Add($"{c}: должен быть ровно один из result и error");
            foreach (IGrouping<string, ReferenceCase> g in cases.GroupBy(x => x.Name).Where(x => x.Count() > 1))
                errors.Add($"{python}: кейс «{g.Key}» повторяется");
        }
        if (errors.Count > 0)
            Assert.Fail(string.Join("\n", errors));
    }

    /// <summary>Почему у нас свой Sum (DECISIONS.md п. 24): NumPy складывает попарно, LINQ — подряд.</summary>
    [Fact]
    public void TenTenthsSumToOneInNumPyButNotInLinq()
    {
        ReferenceCase c = References.Case("numpy.sum", "ten-tenths");
        double[] tenths = c.Array("a").Get<double>();
        Assert.Equal(Enumerable.Repeat(0.1, 10), tenths);
        Assert.Equal(1.0, c.ResultArray().Get<double>()[0]);
        Assert.Equal(0.9999999999999999, tenths.Sum());
    }

    [Fact]
    public void ErrorsKeepPythonTypeAndText()
    {
        Assert.Equal(new ReferenceError("LinAlgError", "numpy.linalg", "Singular matrix"),
            References.Case("numpy.linalg.solve", "singular").Error);
        Assert.Equal(new ReferenceError("AxisError", "numpy.exceptions", "axis 2 is out of bounds for array of dimension 2"),
            References.Case("numpy.sum", "axis-out-of-range").Error);
        Assert.Equal(new ReferenceError("ValueError", "builtins", "operands could not be broadcast together with shapes (3,) (4,) "),
            References.Case("numpy.add", "shapes-differ").Error);
    }

    [Fact]
    public void WarningsAreRecorded()
    {
        Assert.Equal(new[] { new ReferenceWarning("RuntimeWarning", "overflow encountered in exp") },
            References.Case("numpy.exp", "special").Warnings);
        Assert.Empty(References.Case("numpy.exp", "random").Warnings);
    }

    [Fact]
    public void MatchLevelsComeFromCases()
    {
        Assert.Equal(MatchLevel.Bitwise, References.Case("numpy.add", "random").Match);
        Assert.Equal(MatchLevel.Ulp(1), References.Case("numpy.exp", "random").Match);
        Assert.Equal(MatchLevel.Tolerance(1e-12, 1e-14), References.Case("numpy.linalg.solve", "random-10").Match);
    }

    /// <summary>
    /// numpy.add у нас — сложение в LINQ (names.csv: <c>v.Select((x, i) => x + w[i])</c>): .NET складывает так же,
    /// как NumPy, до бита — вместе с NaN, бесконечностями, нулями со знаком и переполнением целых.
    /// </summary>
    [Fact]
    public void DotNetAdditionMatchesNumPyBitwise()
    {
        var errors = new List<string>();
        foreach (ReferenceCase c in References.Load("numpy.add").Where(x => x.Error is null))
        {
            NpyArray x1 = c.Array("x1"), x2 = c.Array("x2"), expected = c.ResultArray();
            Array sum = (x1.Data, x2.Data) switch
            {
                (double[] a, double[] b) => a.Select((x, i) => x + b[i]).ToArray(),
                (float[] a, float[] b) => a.Select((x, i) => x + b[i]).ToArray(),
                (long[] a, long[] b) => a.Select((x, i) => unchecked(x + b[i])).ToArray(),
                _ => throw new NotSupportedException($"{c}: {x1.Descr} + {x2.Descr}"),
            };
            errors.AddRange(Compare.Arrays(expected, NpyArray.Of(sum, [.. x1.Shape]), c.Match).Select(e => $"{c}: {e}"));
        }
        if (errors.Count > 0)
            Assert.Fail(string.Join("\n", errors));
    }
}
