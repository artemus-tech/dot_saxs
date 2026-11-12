using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>Таблица имён names.csv: у каждого публичного имени NumPy/SciPy ровно одно имя в C# (SPEC.md п. 2).</summary>
public sealed class NamesTests
{
    private static readonly Assembly[] Port = Projects.Load(Projects.Port);

    [Theory]
    [InlineData("numpy.linalg.solve", "Np.LinAlg.Solve")]
    [InlineData("numpy.argmax", "Np.ArgMax")]
    [InlineData("numpy.cumsum", "Np.CumSum")]
    [InlineData("numpy.linspace", "Np.LinSpace")]
    [InlineData("numpy.random.default_rng", "Np.Random.DefaultRng")]
    [InlineData("scipy.integrate.solve_ivp", "SciPy.Integrate.SolveIvp")]
    [InlineData("scipy.sparse.linalg.spsolve", "SciPy.Sparse.LinAlg.SpSolve")]
    [InlineData("numpy.ndarray", "Np.NDArray")]
    [InlineData("numpy.dtype", "Np.DType")]
    [InlineData("scipy.spatial.KDTree", "SciPy.Spatial.KDTree")]
    [InlineData("scipy.sparse.linalg.LinearOperator", "SciPy.Sparse.LinAlg.LinearOperator")]
    [InlineData("numpy.random.Generator", "Np.Random.Generator")]
    [InlineData("numpy.random.PCG64", "Np.Random.PCG64")]
    [InlineData("scipy.linalg.blas.dgemm", "SciPy.LinAlg.Blas.Dgemm")]
    [InlineData("scipy.linalg.lapack.dgesv", "SciPy.LinAlg.Lapack.Dgesv")]
    public void ExamplesFromSpecMapAsWritten(string python, string csharp) => AssertMaps(python, csharp);

    /// <summary>Примеры решений DECISIONS.md: коллизии (п. 10), scipy.special (п. 11), псевдонимы (п. 21), вызов интерполятора (п. 28).</summary>
    [Theory]
    [InlineData("numpy.fft.fft", "Np.Fft.Fft_")]
    [InlineData("numpy.polynomial.chebyshev", "Np.Polynomial.ChebyshevModule")]
    [InlineData("scipy.stats.uniform", "SciPy.Stats.Uniform_")]
    [InlineData("scipy.constants.g", "SciPy.Constants.g")]
    [InlineData("scipy.special.logsumexp", "SciPy.Special.LogSumExp")]
    [InlineData("scipy.special.gammaln", "SciPy.Special.GammaLn")]
    [InlineData("scipy.special.betaincinv", "SciPy.Special.BetaIncInv")]
    [InlineData("scipy.special.nbdtrik", "SciPy.Special.Nbdtrik")]
    [InlineData("scipy.special.hyp2f1", "SciPy.Special.Hyp2F1")]
    [InlineData("scipy.special.eval_chebyt", "SciPy.Special.EvalChebyT")]
    [InlineData("scipy.special.ellipk", "SciPy.Special.EllipK")]
    [InlineData("scipy.special.lambertw", "SciPy.Special.LambertW")]
    [InlineData("scipy.special.chndtrinc", "SciPy.Special.Chndtrinc")]
    [InlineData("scipy.special.p_roots", "SciPy.Special.RootsLegendre")]
    [InlineData("numpy.ma.row_stack", "Np.Ma.VStack")]
    [InlineData("scipy.interpolate.PPoly.__call__", "SciPy.Interpolate.PPoly.GetInterpolatedValueAt")]
    public void ExamplesFromDecisionsMapAsWritten(string python, string csharp) => AssertMaps(python, csharp);

    private static void AssertMaps(string python, string csharp)
    {
        NameRow? row = NamesTable.Load().SingleOrDefault(r => r.Python == python);
        Assert.NotNull(row);
        Assert.Equal(csharp, row.CSharp);
    }

    /// <summary>Как это делается у нас (DECISIONS.md п. 24–26): примеры по каждому способу.</summary>
    [Theory]
    [InlineData("numpy.exp", "функция числа", "v.Select(Np.Exp)")]
    [InlineData("numpy.sqrt", "уже есть в .NET", "v.Select(Math.Sqrt)")]
    [InlineData("numpy.add", "LINQ", "v.Select((x, i) => x + w[i])")]
    [InlineData("numpy.sum", "метод", "v.Sum()")]
    [InlineData("numpy.diff", "метод", "v.Diff()")]
    [InlineData("numpy.linalg.solve", "метод", "m.Solve(…)")]
    [InlineData("numpy.matmul", "метод", "m.MatrixProduct(b)")]
    [InlineData("scipy.integrate.trapezoid", "метод", "v.Trapezoid(…)")]
    [InlineData("numpy.zeros", "создание", "Vector<double>.Create(n, i => 0.0)")]
    [InlineData("numpy.delete", "форма", "m.RemoveRow(3)")]
    [InlineData("numpy.float64", "вид чисел", "double")]
    [InlineData("numpy.moveaxis", "форма", "cube.SetIndexOfMatrix(2)")]
    [InlineData("numpy.swapaxes", "не переносится", "")]
    [InlineData("numpy.ma.masked_invalid", "не переносится", "")]
    [InlineData("numpy.strings.upper", "не переносится", "")]
    [InlineData("numpy.lib.recfunctions.structured_to_unstructured", "форма", "data.ToMatrix()")]
    [InlineData("numpy.matrix", "не переносится", "")]
    [InlineData("scipy.sparse.csr_array", "класс", "SparseMatrix<double>.FromTriplets(rows, columns, values)")]
    [InlineData("scipy.linalg.blas.dgemm", "внутри", "")]
    [InlineData("numpy.clip", "функция числа", "v.Select(x => Np.Clip(x, lo, hi))")]
    [InlineData("numpy.absolute", "функция числа", "v.Select(Np.Absolute)")]
    [InlineData("numpy.left_shift", "функция числа", "v.Select((x, i) => Np.LeftShift(x, w[i]))")]
    [InlineData("numpy.roll", "метод объекта", "v.ToDeque().Shift(-1).ToVector()")]
    [InlineData("scipy.interpolate.PPoly.__call__", "метод объекта", "obj.GetInterpolatedValueAt(x)")]
    public void ExamplesOfWays(string python, string how, string call)
    {
        NameRow? row = NamesTable.Load().SingleOrDefault(r => r.Python == python);
        Assert.NotNull(row);
        Assert.Equal((how, call), (row.How, row.Call));
    }

    /// <summary>
    /// Этап плана (DECISIONS.md п. 25): форма, создание, свои Sum…Sort, .npy — этап 2; MatrixProduct — 3; линейная
    /// алгебра — 4; остальной NumPy — 5; SciPy — 6; член класса — на этапе класса; у того, что не переносится, этапа нет.
    /// </summary>
    [Theory]
    [InlineData("numpy.pad", "2")]
    [InlineData("numpy.median", "2")]
    [InlineData("numpy.loadtxt", "2")]
    [InlineData("numpy.save", "2")]
    [InlineData("numpy.lib.npyio.NpzFile.keys", "2")]
    [InlineData("numpy.linalg.matrix_transpose", "2")]
    [InlineData("numpy.matmul", "3")]
    [InlineData("scipy.linalg.blas.dgemm", "3")]
    [InlineData("numpy.linalg.solve", "4")]
    [InlineData("numpy.histogram", "5")]
    [InlineData("numpy.hamming", "5")]
    [InlineData("scipy.special.gamma", "6")]
    [InlineData("numpy.moveaxis", "2")]
    [InlineData("numpy.lib.recfunctions.join_by", "2")]
    [InlineData("numpy.swapaxes", "")]
    [InlineData("numpy.ma.masked_invalid", "")]
    public void ExamplesOfStages(string python, string stage)
    {
        NameRow? row = NamesTable.Load().SingleOrDefault(r => r.Python == python);
        Assert.NotNull(row);
        Assert.Equal(stage, row.Stage);
    }

    [Fact]
    public void EveryRowHasAStage()
    {
        string[] errors = NameChecks.StageErrors(NamesTable.Load()).ToArray();
        if (errors.Length > 0)
            Assert.Fail("names.csv — этапы:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void CheckFindsBadStages()
    {
        NameRow[] rows =
        [
            new("numpy.a", "function", "Np.A", NamesTable.Planned, "метод", "a.A()", "2"),
            new("numpy.b", "function", "Np.B", NamesTable.NotPorted, NamesTable.NotPorted, "", "5"),
            new("numpy.c", "function", "Np.C", NamesTable.Planned, "метод", "a.C()", ""),
            new("numpy.d", "function", "Np.D", NamesTable.Planned, "решить", "", "5", "решить на этапе 2"),
            new("numpy.e", "function", "Np.E", NamesTable.Planned, "решить", "", "2", "решить на этапе 2"),
        ];
        Assert.Equal(3, NameChecks.StageErrors(rows).Count());
    }

    [Fact]
    public void EveryRowHasAWay()
    {
        string[] errors = NameChecks.WayErrors(NamesTable.Load()).ToArray();
        if (errors.Length > 0)
            Assert.Fail("names.csv — способы:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void CheckFindsBadWays()
    {
        NameRow[] rows =
        [
            new("numpy.a", "function", "Np.A", NamesTable.Planned, "как-нибудь", "Np.A()"),
            new("numpy.b", "function", "Np.B", NamesTable.Planned, NamesTable.NotPorted),
            new("numpy.c", "function", "Np.C", NamesTable.Planned, "метод"),
            new("numpy.d", "function", "Np.D", NamesTable.Alias, "метод"),
        ];
        string[] errors = NameChecks.WayErrors(rows).ToArray();
        Assert.Equal(3, errors.Length);
    }

    [Fact]
    public void NamesAreUnique()
    {
        string[] errors = NameChecks.Duplicates(NamesTable.Load()).ToArray();
        if (errors.Length > 0)
            Assert.Fail("Повторы в names.csv:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void PortedRowsMatchThePort()
    {
        var ported = Port.SelectMany(a => OriginalChecks.All(a)).Select(x => (CSharp: WithoutRoot(x.Where), Python: x.Original.Name));
        string[] errors = NameChecks.Mismatches(NamesTable.Load(), ported).ToArray();
        if (errors.Length > 0)
            Assert.Fail("names.csv расходится с портом:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void CheckFindsDuplicates()
    {
        NameRow[] rows =
        [
            new("numpy.a", "function", "Np.A", NamesTable.Planned),
            new("numpy.a", "function", "Np.A2", NamesTable.Planned),
            new("numpy.b", "function", "Np.B", NamesTable.Planned),
            new("numpy.c", "function", "Np.B", NamesTable.Ported),
        ];
        Assert.Equal(2, NameChecks.Duplicates(rows).Count());
    }

    [Fact]
    public void CheckFindsMismatchesBothWays()
    {
        NameRow[] rows =
        [
            new("numpy.ported", "function", "Np.Ported", NamesTable.Ported),
            new("numpy.planned", "function", "Np.Planned", NamesTable.Planned),
            new("numpy.renamed", "function", "Np.Renamed", NamesTable.Ported),
        ];
        (string, string)[] port = [("Np.Planned", "numpy.planned"), ("Np.Other", "numpy.renamed"), ("Np.Private", "numpy._core.x")];
        string[] errors = NameChecks.Mismatches(rows, port).ToArray();
        Assert.Contains(errors, e => e.StartsWith("numpy.ported:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Np.Planned:", StringComparison.Ordinal));
        // Имя в C# своё (DECISIONS.md п. 24–25): numpy.renamed переносит Np.Other — это не ошибка.
        Assert.DoesNotContain(errors, e => e.Contains("Np.Other", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("numpy._core.x", StringComparison.Ordinal));
        Assert.Equal(2, errors.Length);
    }

    [Fact]
    public void CsvParserHandlesQuotes()
    {
        List<string[]> rows = NamesTable.Csv("a,\"b,c\",\"d\"\"e\"\n1,,3\n");
        Assert.Equal(new[] { "a", "b,c", "d\"e" }, rows[0]);
        Assert.Equal(new[] { "1", "", "3" }, rows[1]);
        Assert.Equal(2, rows.Count);
    }

    private static string WithoutRoot(string name) =>
        name.StartsWith("DotSaxs.", StringComparison.Ordinal) ? name["DotSaxs.".Length..] : name;
}
