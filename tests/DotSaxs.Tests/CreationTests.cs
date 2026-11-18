using System;
using System.Linq;
using System.Numerics;
using DotSaxs.Testing;
using Xunit;
using static DotSaxs.Tests.ReferenceChecks;

namespace DotSaxs.Tests;

/// <summary>
/// Создание векторов и матриц против эталонов NumPy (DECISIONS.md п. 22): <c>Create</c> — как zeros, ones и full;
/// <c>Identity</c>, <c>FromDiagonal</c>, <c>GetDiagonal</c>, <c>Range</c>, <c>LinSpace</c> — как identity, diag,
/// diagonal, arange и linspace. Каждый кейс эталона — в тестах ниже, до бита.
/// </summary>
public sealed class CreationTests
{
    public static TheoryData<string, string> FillCases() => Cases("numpy.zeros", "numpy.ones", "numpy.full");

    public static TheoryData<string, string> IdentityCases() => Cases("numpy.identity");

    public static TheoryData<string, string> DiagCases() => Cases("numpy.diag");

    public static TheoryData<string, string> DiagonalCases() => Cases("numpy.diagonal");

    public static TheoryData<string, string> RangeCases() => Cases("numpy.arange");

    public static TheoryData<string, string> LinSpaceCases() => Cases("numpy.linspace");

    [Theory]
    [MemberData(nameof(FillCases))]
    public void CreateMatchesZerosOnesAndFull(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        double fill = python switch
        {
            "numpy.zeros" => 0.0,
            "numpy.ones" => 1.0,
            _ => Float(c, "fill_value"),
        };
        int[] shape = Shape(c.Argument("shape"));
        Check(c, shape.Length == 1
            ? () => ToNpy(Vector<double>.Create(shape[0], i => fill))
            : () => ToNpy(Matrix<double>.Create(shape[0], shape[1], (row, column) => fill)));
    }

    [Fact]
    public void CreateGivesNumberByItsIndices()
    {
        // np.fromfunction(lambda i: i * 0.5, (5,)) и np.fromfunction(lambda i, j: 10 * i + j, (2, 3)).
        Assert.Equal(new[] { 0.0, 0.5, 1.0, 1.5, 2.0 }, Vector<double>.Create(5, i => i * 0.5).ToArray());
        Matrix<int> m = Matrix<int>.Create(2, 3, (row, column) => (10 * row) + column);
        Assert.Equal(new[] { 0, 1, 2, 10, 11, 12 }, m.Flatten().ToArray());
        Assert.Empty(Vector<bool>.Create(0, i => true));
        Assert.Throws<ArgumentNullException>(() => Vector<double>.Create(3, null!));
    }

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public void IdentityMatchesNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        int n = Int(c, "n");
        Check(c, () => ToNpy(Matrix<double>.Identity(n)));
    }

    [Fact]
    public void IdentityWorksForAnyNumbers()
    {
        Assert.Equal(new[] { 1, 0, 0, 1 }, Matrix<int>.Identity(2).Flatten().ToArray());
        Assert.Equal(new[] { Complex.One, Complex.Zero, Complex.Zero, Complex.One }, Matrix<Complex>.Identity(2).Flatten().ToArray());
    }

    [Theory]
    [MemberData(nameof(DiagCases))]
    public void FromDiagonalMatchesDiag(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Vector<double> v = ToVector<double>(c.Array("v"));
        int k = Int(c, "k", 0);
        Check(c, () => ToNpy(Matrix<double>.FromDiagonal(v, k)));
    }

    [Theory]
    [MemberData(nameof(DiagonalCases))]
    public void GetDiagonalMatchesDiagonal(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Matrix<double> m;
        if (c.Argument("a") is NpyValue { View: { } view } argument)
        {
            // Кейс «transposed» — a.T: вид без копирования, как наш Transpose().
            m = ToMatrix(argument.Value).Transpose();
            Assert.Equal(view.Shape, new[] { m.RowCount, m.ColumnCount });
        }
        else
        {
            m = ToMatrix(c.Array("a"));
        }
        int offset = Int(c, "offset", 0);
        Check(c, () => ToNpy(m.GetDiagonal(offset)));
    }

    [Fact]
    public void DiagonalSharesNumbersAndRoundTrips()
    {
        Vector<double> v = new[] { 1.0, -2.0, 0.5 }.ToVector();
        Assert.Equal(v.ToArray(), Matrix<double>.FromDiagonal(v).GetDiagonal().ToArray());
        Assert.Equal(v.ToArray(), Matrix<double>.FromDiagonal(v, -2).GetDiagonal(-2).ToArray());
        Matrix<double> m = Matrix<double>.Create(3, 4, (row, column) => (10 * row) + column);
        Assert.Equal(new[] { 0.0, 11.0, 22.0 }, m.GetDiagonal().ToArray());
        Assert.Equal(new[] { 3.0 }, m.GetDiagonal(3).ToArray());
        Assert.Equal(new[] { 1.0, 12.0, 23.0 }, m.Transpose().GetDiagonal(-1).ToArray());
        Assert.Equal(33.0, m.GetDiagonal().Sum());
    }

    [Theory]
    [MemberData(nameof(RangeCases))]
    public void RangeMatchesArange(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        double start = Float(c, "start"), stop = Float(c, "stop"), step = Float(c, "step");
        Check(c, () => ToNpy(Vector<double>.Range(start, stop, step)));
    }

    [Fact]
    public void RangeLongerThanDotNetArrayIsMemoryError()
    {
        // NumPy выделил бы 3·10⁹ чисел, если хватит памяти; в массив .NET столько не помещается (DECISIONS.md п. 14).
        MemoryError error = Assert.Throws<MemoryError>(() => Vector<double>.Range(0, 3e9, 1));
        Assert.Equal($"3000000000 numbers do not fit into one .NET array: at most {Array.MaxLength}", error.Message);
    }

    [Theory]
    [MemberData(nameof(LinSpaceCases))]
    public void LinSpaceMatchesNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        double start = Float(c, "start"), stop = Float(c, "stop");
        int count = Int(c, "num", 50);
        bool endpoint = Bool(c, "endpoint", true);
        Check(c, () => ToNpy(Vector<double>.LinSpace(start, stop, count, endpoint)));
    }
}
