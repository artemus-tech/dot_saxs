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
    public void LongsMatchNumPy()
    {
        ReferenceCase c = References.Case("numpy.sum", "int64");
        Assert.Equal(c.ResultArray().Get<long>()[0], c.Array("a").Get<long>().ToVector().Sum());
    }

    [Fact]
    public void LongOverflowThrowsUnlessIgnored()
    {
        // NumPy молча идёт по кругу; у нас — исключение (DECISIONS.md п. 32), по кругу — только явно.
        Vector<long> big = new[] { long.MaxValue, 1L }.ToVector();
        Assert.Equal("overflow encountered in reduce", Assert.Throws<FloatingPointError>(() => big.Sum()).Message);
        Vector<int> ints = new[] { int.MaxValue, int.MaxValue }.ToVector();
        Assert.Equal(2L * int.MaxValue, ints.Sum());
        using (Np.ErrState(over: "ignore"))
            Assert.Equal(long.MinValue, big.Sum());
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
        // Суммы всех чисел у матрицы нет (DECISIONS.md п. 22): у матрицы по строкам Flatten() — без копии, и сумма — как np.sum(a).
        Assert.Null(c.Match.Compare(c.ResultArray().Get<double>()[0], ToMatrix(c.Array("a")).Flatten().Sum()));
    }

    [Fact]
    public void ColumnSumsAreAxis0()
    {
        ReferenceCase c = References.Case("numpy.sum", "matrix-axis-0");
        Matrix<double> m = ToMatrix(c.Array("a"));
        Assert.Empty(Compare.Arrays(c.ResultArray(), ToNpy(m.GetRows().ElementWiseSum()), c.Match));
    }

    [Fact]
    public void RowSumsAreAxis1()
    {
        ReferenceCase c = References.Case("numpy.sum", "matrix-axis-1");
        Matrix<double> m = ToMatrix(c.Array("a"));
        Assert.Empty(Compare.Arrays(c.ResultArray(), ToNpy(m.GetRows().Select(r => r.Sum()).ToVector()), c.Match));
    }

    [Fact]
    public void TransposedMatrixSumsLikeNumPyView()
    {
        // view-transposed — a.T от матрицы 40 × 30. Transpose() — вид без копирования: столбец a.T — строка a, числа
        // подряд, и NumPy складывает его попарно, как Sum() вектора. Совпадает с a.T.sum(axis=0) до бита.
        (NpyArray array, NpyView view) = References.Case("numpy.sum", "view-transposed-axis-0").ArrayView("a");
        Matrix<double> t = ToMatrix(array).Transpose();
        Assert.Equal(view.Shape, new[] { t.RowCount, t.ColumnCount });
        ReferenceCase columns = References.Case("numpy.sum", "view-transposed-axis-0");
        Assert.Empty(Compare.Arrays(columns.ResultArray(), ToNpy(t.GetColumns().Select(c => c.Sum()).ToVector()), columns.Match));
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
