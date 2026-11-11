using System.Linq;
using DotSaxs.Testing;
using Xunit;

namespace DotSaxs.Tests;

/// <summary>Своя сумма против эталонов numpy.sum (tests/references/numpy.sum) — до бита (DECISIONS.md п. 24).</summary>
public sealed class SumReferenceTests
{
    // Каждый кейс эталона — в одном из тестов ниже; кейс «axis-out-of-range» у нас не воспроизвести: axis нет (п. 24).
    private static readonly string[] Covered =
    [
        "ten-tenths", "random-100", "random-1000", "empty", "special", "int64", "float32",
        "matrix", "matrix-axis-0", "matrix-axis-1", "axis-out-of-range",
    ];

    [Fact]
    public void EveryReferenceCaseIsCovered()
    {
        // Кейсы «view-…» — суммы по видам с любыми шагами; их проверяет ReductionReferenceTests.
        string[] cases = References.Load("numpy.sum").Select(c => c.Name).Where(n => !n.StartsWith("view-", System.StringComparison.Ordinal)).Order().ToArray();
        Assert.Equal(cases, Covered.Order().ToArray());
    }

    [Theory]
    [InlineData("ten-tenths")]
    [InlineData("random-100")]
    [InlineData("random-1000")]
    [InlineData("empty")]
    public void VectorOfDoubles(string name)
    {
        ReferenceCase c = References.Case("numpy.sum", name);
        double actual = c.Array("a").Get<double>().ToVector().Sum();
        Assert.Null(c.Match.Compare(c.ResultArray().Get<double>()[0], actual));
    }

    [Fact]
    public void TenTenthsGiveOneUnlikeLinq()
    {
        Vector<double> tenths = References.Case("numpy.sum", "ten-tenths").Array("a").Get<double>().ToVector();
        Assert.Equal(1.0, tenths.Sum());
        Assert.Equal(0.9999999999999999, Enumerable.Sum(tenths));
    }

    [Fact]
    public void SpecialValuesThrowByDefaultAndMatchWhenIgnored()
    {
        ReferenceCase c = References.Case("numpy.sum", "special");
        Vector<double> v = c.Array("a").Get<double>().ToVector();
        FloatingPointError error = Assert.Throws<FloatingPointError>(() => v.Sum());
        Assert.Equal(c.Warnings[0].Message, error.Message);
        using (Np.ErrState(all: "ignore"))
            Assert.Null(c.Match.Compare(c.ResultArray().Get<double>()[0], v.Sum()));
    }

    [Fact]
    public void LongsWrapAroundLikeNumPy()
    {
        ReferenceCase c = References.Case("numpy.sum", "int64");
        Assert.Equal(c.ResultArray().Get<long>()[0], c.Array("a").Get<long>().ToVector().Sum());
    }

    [Fact]
    public void FloatsAreSummedInFloat()
    {
        ReferenceCase c = References.Case("numpy.sum", "float32");
        float actual = c.Array("a").Get<float>().ToVector().Sum();
        Assert.Null(c.Match.Compare(c.ResultArray().Get<float>()[0], actual));
    }

    [Fact]
    public void WholeMatrix()
    {
        ReferenceCase c = References.Case("numpy.sum", "matrix");
        Assert.Null(c.Match.Compare(c.ResultArray().Get<double>()[0], ToMatrix(c.Array("a")).Sum()));
    }

    [Fact]
    public void ColumnSumsAreAxis0()
    {
        ReferenceCase c = References.Case("numpy.sum", "matrix-axis-0");
        Matrix<double> m = ToMatrix(c.Array("a"));
        Assert.Empty(Compare.Arrays(c.ResultArray(), ToNpy(m.GetColumnSums()), c.Match));
        Assert.Empty(Compare.Arrays(c.ResultArray(), ToNpy(m.GetRows().Sum()), c.Match));
    }

    [Fact]
    public void RowSumsAreAxis1()
    {
        ReferenceCase c = References.Case("numpy.sum", "matrix-axis-1");
        Matrix<double> m = ToMatrix(c.Array("a"));
        Assert.Empty(Compare.Arrays(c.ResultArray(), ToNpy(m.GetRowSums()), c.Match));
        Assert.Empty(Compare.Arrays(c.ResultArray(), ToNpy(m.GetRows().Select(r => r.Sum()).ToVector()), c.Match));
    }

    [Fact]
    public void TransposedMatrixSumsLikeNumPyView()
    {
        // view-transposed — a.T от матрицы 40 × 30. Transpose() — тот же вид без копирования, и суммы совпадают до бита;
        // у копии того же a.T в 25 из 30 сумм строк другие последние биты.
        ReferenceCase whole = References.Case("numpy.sum", "view-transposed");
        (NpyArray array, NpyView view) = whole.ArrayView("a");
        Matrix<double> t = ToMatrix(array).Transpose();
        Assert.Equal(view.Shape, new[] { t.RowCount, t.ColumnCount });
        Assert.Null(whole.Match.Compare(whole.ResultArray().Get<double>()[0], t.Sum()));
        ReferenceCase columns = References.Case("numpy.sum", "view-transposed-axis-0");
        Assert.Empty(Compare.Arrays(columns.ResultArray(), ToNpy(t.GetColumnSums()), columns.Match));
        ReferenceCase rows = References.Case("numpy.sum", "view-transposed-axis-1");
        Assert.Empty(Compare.Arrays(rows.ResultArray(), ToNpy(t.GetRowSums()), rows.Match));
    }

    [Fact]
    public void AxisOutOfRangeHasNoCounterpart()
    {
        // В NumPy — AxisError: у матрицы нет оси 2. У нас axis нет вовсе (DECISIONS.md п. 24), ошибку не повторить.
        Assert.Equal("AxisError", References.Case("numpy.sum", "axis-out-of-range").Error?.Type);
    }

    internal static Matrix<double> ToMatrix(NpyArray a) => a.Get<double>().ToVector().ToMatrix(a.Shape[0], a.Shape[1]);

    internal static NpyArray ToNpy(Vector<double> v) => NpyArray.Of(v.ToArray(), v.Count);
}
