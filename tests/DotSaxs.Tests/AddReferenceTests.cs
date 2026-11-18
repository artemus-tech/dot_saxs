using System;
using System.Linq;
using DotSaxs.Testing;
using Xunit;

namespace DotSaxs.Tests;

/// <summary>
/// Сложение массивов — через LINQ: <c>v.Select((x, i) => x + w[i])</c> (DECISIONS.md п. 24, names.csv) — против
/// эталонов numpy.add, до бита. Это обычная арифметика C#: ошибок чисел она не бросает (п. 32), inf и nan — молча.
/// </summary>
public sealed class AddReferenceTests
{
    private static readonly string[] Covered = ["special-pairs", "random", "float32", "int64-wraparound", "matrix", "shapes-differ"];

    [Fact]
    public void EveryReferenceCaseIsCovered()
    {
        string[] cases = References.Load("numpy.add").Select(c => c.Name).Order().ToArray();
        Assert.Equal(cases, Covered.Order().ToArray());
    }

    [Theory]
    [InlineData("special-pairs")]
    [InlineData("random")]
    public void Doubles(string name)
    {
        ReferenceCase c = References.Case("numpy.add", name);
        Vector<double> x1 = c.Array("x1").Get<double>().ToVector();
        Vector<double> x2 = c.Array("x2").Get<double>().ToVector();
        Vector<double> sum = x1.Select((x, i) => x + x2[i]);
        Assert.Empty(Compare.Arrays(c.ResultArray(), NpyArray.Of(sum.ToArray(), sum.Count), c.Match));
    }

    [Fact]
    public void Floats()
    {
        ReferenceCase c = References.Case("numpy.add", "float32");
        Vector<float> x1 = c.Array("x1").Get<float>().ToVector();
        Vector<float> x2 = c.Array("x2").Get<float>().ToVector();
        Vector<float> sum = x1.Select((x, i) => x + x2[i]);
        Assert.Empty(Compare.Arrays(c.ResultArray(), NpyArray.Of(sum.ToArray(), sum.Count), c.Match));
    }

    [Fact]
    public void LongsWrapAround()
    {
        ReferenceCase c = References.Case("numpy.add", "int64-wraparound");
        Vector<long> x1 = c.Array("x1").Get<long>().ToVector();
        Vector<long> x2 = c.Array("x2").Get<long>().ToVector();
        Vector<long> sum = x1.Select((x, i) => unchecked(x + x2[i]));
        Assert.Equal(c.ResultArray().Get<long>(), sum.ToArray());
    }

    [Fact]
    public void Matrices()
    {
        ReferenceCase c = References.Case("numpy.add", "matrix");
        Matrix<double> a = SumReferenceTests.ToMatrix(c.Array("x1"));
        Matrix<double> b = SumReferenceTests.ToMatrix(c.Array("x2"));
        Matrix<double> sum = a.Select((x, row, column) => x + b[row, column]);
        Assert.Empty(Compare.Arrays(c.ResultArray(), NpyArray.Of(sum.Flatten().ToArray(), sum.RowCount, sum.ColumnCount), c.Match));
    }

    [Fact]
    public void ShapesAreNotCheckedInsideTheFormula()
    {
        // NumPy не складывает векторы длины 3 и 4 (ValueError). У нас размеры в формуле не сверяются (DECISIONS.md п. 24):
        // второй длиннее — берётся его начало, второй короче — IndexError на первом лишнем номере.
        ReferenceCase c = References.Case("numpy.add", "shapes-differ");
        Assert.Equal("ValueError", c.Error?.Type);
        Vector<double> three = c.Array("x1").Get<double>().ToVector();
        Vector<double> four = c.Array("x2").Get<double>().ToVector();
        Assert.Equal([2.0, 2.0, 2.0], three.Select((x, i) => x + four[i]).ToArray());
        IndexError error = Assert.Throws<IndexError>(() => four.Select((x, i) => x + three[i]));
        Assert.IsAssignableFrom<ArgumentOutOfRangeException>(error);
    }
}
