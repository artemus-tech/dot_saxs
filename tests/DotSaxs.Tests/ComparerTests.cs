using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DotSaxs.Testing;
using Xunit;
using static DotSaxs.Tests.ReferenceChecks;

namespace DotSaxs.Tests;

/// <summary>
/// Равенство массивов по числам — компараторы <c>VectorComparer.ByValues</c> и <c>MatrixComparer.ByValues</c> против
/// <c>np.array_equal(a, b, equal_nan=True)</c> (DECISIONS.md п. 34).
/// </summary>
public sealed class ComparerTests
{
    public static TheoryData<string, string> EqualCases() => Cases("numpy.array_equal");

    [Theory]
    [MemberData(nameof(EqualCases))]
    public void ComparersMatchArrayEqual(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        NpyArray a = c.Array("a1"), b = c.Array("a2");
        bool actual = (a.Shape.Count, b.Shape.Count, a.Type) switch
        {
            (1, 1, "c16") => VectorComparer<Complex>.ByValues.Equals(ToVector<Complex>(a), ToVector<Complex>(b)),
            (1, 1, _) => VectorComparer<double>.ByValues.Equals(ToVector<double>(a), ToVector<double>(b)),
            (2, 2, _) => MatrixComparer<double>.ByValues.Equals(ToMatrix(a), ToMatrix(b)),
            _ => throw new NotSupportedException(c.ToString()),
        };
        Assert.Equal(BoolResult(c), actual);
    }

    [Fact]
    public void EqualsOfVectorItselfComparesReferences()
    {
        // a.Equals(b) не переопределён: как у всех классов .NET, это один ли объект (DECISIONS.md п. 34).
        Vector<double> a = new[] { 1.0, 2.0 }.ToVector(), b = new[] { 1.0, 2.0 }.ToVector();
        Assert.False(a.Equals(b));
        Assert.True(VectorComparer<double>.ByValues.Equals(a, b));
        Assert.Equal(a, b, VectorComparer<double>.ByValues);
    }

    [Fact]
    public void NullsAndTransposedViews()
    {
        Vector<double> v = new[] { 1.0 }.ToVector();
        Assert.True(VectorComparer<double>.ByValues.Equals(null, null));
        Assert.False(VectorComparer<double>.ByValues.Equals(v, null));
        Assert.False(MatrixComparer<double>.ByValues.Equals(null, Matrix<double>.Identity(1)));
        // Транспонированная матрица — вид на те же числа; копия по строкам с теми же числами ей равна.
        Matrix<double> m = Matrix<double>.Create(2, 3, (row, column) => (10 * row) + column);
        Matrix<double> copy = Matrix<double>.Create(3, 2, (row, column) => (10 * column) + row);
        Assert.True(MatrixComparer<double>.ByValues.Equals(m.Transpose(), copy));
        Assert.False(MatrixComparer<double>.ByValues.Equals(m, copy));
    }

    [Fact]
    public void HashCodesAgreeWithEquality()
    {
        // NaN разной «начинки» и 0 с −0 равны, значит, и хеш у них один: компаратор годится для HashSet.
        double otherNaN = BitConverter.Int64BitsToDouble(unchecked((long)0xfff8_0000_0000_0001UL));
        var set = new HashSet<Vector<double>>(VectorComparer<double>.ByValues)
        {
            new[] { double.NaN, 0.0 }.ToVector(),
            new[] { otherNaN, -0.0 }.ToVector(),
            new[] { 1.0, 0.0 }.ToVector(),
        };
        Assert.Equal(2, set.Count);
        var complex = new HashSet<Vector<Complex>>(VectorComparer<Complex>.ByValues)
        {
            new[] { new Complex(double.NaN, 1) }.ToVector(),
            new[] { new Complex(2, double.NaN) }.ToVector(),
        };
        Assert.Single(complex);
        var matrices = new HashSet<Matrix<double>>(MatrixComparer<double>.ByValues)
        {
            Matrix<double>.Identity(2),
            Matrix<double>.FromDiagonal(new[] { 1.0, 1.0 }.ToVector()),
            Matrix<double>.Identity(2).GetRows().First().ToMatrix(1, 2),
        };
        Assert.Equal(2, matrices.Count);
    }

    [Fact]
    public void IntegersAndBooleansCompareByValue()
    {
        Assert.True(VectorComparer<long>.ByValues.Equals(new[] { 1L, 2L }.ToVector(), Enumerable.Range(1, 2).Select(x => (long)x).ToVector()));
        Assert.False(VectorComparer<bool>.ByValues.Equals(new[] { true }.ToVector(), new[] { false }.ToVector()));
    }
}
