using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DotSaxs.Testing.Tests;

/// <summary>Уровни совпадения (SPEC.md п. 8): побитово, до N ULP, по допуску; сравнение массивов.</summary>
public sealed class MatchTests
{
    private static readonly double PositiveNaN = BitConverter.UInt64BitsToDouble(0x7FF8_0000_0000_0000);
    private static readonly double NegativeNaN = BitConverter.UInt64BitsToDouble(0xFFF8_0000_0000_0000);

    [Fact]
    public void UlpsCountRepresentableNumbersBetween()
    {
        Assert.Equal(0UL, Ulps.Between(1.0, 1.0));
        Assert.Equal(1UL, Ulps.Between(1.0, Math.BitIncrement(1.0)));
        Assert.Equal(1UL, Ulps.Between(Math.BitDecrement(1.0), 1.0));
        Assert.Equal(0UL, Ulps.Between(0.0, -0.0));
        Assert.Equal(2UL, Ulps.Between(double.Epsilon, -double.Epsilon));
        Assert.Equal(1UL, Ulps.Between(double.MaxValue, double.PositiveInfinity));
        Assert.Equal(1UL, Ulps.Between(1.0f, MathF.BitIncrement(1.0f)));
        Assert.Equal(1UL, Ulps.Between((Half)1.0f, BitConverter.UInt16BitsToHalf(0x3C01)));
    }

    [Fact]
    public void BitwiseSeesSignOfZeroButAnyNaNIsEqual()
    {
        Assert.Null(MatchLevel.Bitwise.Compare(0.1, 0.1));
        Assert.Null(MatchLevel.Bitwise.Compare(PositiveNaN, PositiveNaN));
        Assert.Null(MatchLevel.Bitwise.Compare(PositiveNaN, NegativeNaN));
        Assert.Null(MatchLevel.Bitwise.Compare(float.NaN, -float.NaN));
        Assert.Null(MatchLevel.Bitwise.Compare(Half.NaN, BitConverter.UInt16BitsToHalf(0x7E00)));
        Assert.NotNull(MatchLevel.Bitwise.Compare(0.0, -0.0));
        Assert.NotNull(MatchLevel.Bitwise.Compare(PositiveNaN, 1.0));
        Assert.NotNull(MatchLevel.Bitwise.Compare(1.0, Math.BitIncrement(1.0)));
        Assert.NotNull(MatchLevel.Bitwise.Compare(1.0f, MathF.BitIncrement(1.0f)));
    }

    [Fact]
    public void UlpAllowsNeighboursButNotOverflow()
    {
        MatchLevel one = MatchLevel.Ulp(1);
        Assert.Null(one.Compare(1.0, Math.BitIncrement(1.0)));
        Assert.NotNull(one.Compare(1.0, Math.BitIncrement(Math.BitIncrement(1.0))));
        Assert.Null(one.Compare(0.0, -0.0));
        Assert.Null(one.Compare(PositiveNaN, NegativeNaN));
        Assert.NotNull(one.Compare(PositiveNaN, 1.0));
        Assert.NotNull(one.Compare(double.PositiveInfinity, double.MaxValue));
        Assert.Contains("2 ULP", one.Compare(1.0, Math.BitIncrement(Math.BitIncrement(1.0))), StringComparison.Ordinal);
    }

    [Fact]
    public void ToleranceIsRelativeToReference()
    {
        MatchLevel level = MatchLevel.Tolerance(rtol: 1e-12, atol: 1e-14);
        Assert.Null(level.Compare(1e6, 1e6 + 1e-7));
        Assert.NotNull(level.Compare(1.0, 1.0 + 1e-11));
        Assert.Null(level.Compare(0.0, 1e-15));
        Assert.NotNull(level.Compare(0.0, 1e-13));
        Assert.NotNull(level.Compare(double.NegativeInfinity, -double.MaxValue));
    }

    [Fact]
    public void LevelsCannotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchLevel.Ulp(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchLevel.Tolerance(-1e-12));
    }

    [Fact]
    public void ArraysReportPlaceAndValues()
    {
        NpyArray expected = NpyArray.Of(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, 2, 3);
        NpyArray actual = NpyArray.Of(new[] { 1.0, 2.0, 3.0, 4.0, 5.5, 6.0 }, 2, 3);
        IReadOnlyList<string> errors = Compare.Arrays(expected, actual, MatchLevel.Bitwise);
        Assert.StartsWith("[1, 1] ожидали 5 ", Assert.Single(errors), StringComparison.Ordinal);
        Assert.Empty(Compare.Arrays(expected, expected, MatchLevel.Bitwise));
    }

    [Fact]
    public void ArraysCheckShapeAndKindFirst()
    {
        NpyArray matrix = NpyArray.Of(new double[6], 2, 3);
        Assert.StartsWith("размеры", Assert.Single(Compare.Arrays(matrix, NpyArray.Of(new double[6], 3, 2), MatchLevel.Bitwise)), StringComparison.Ordinal);
        Assert.StartsWith("вид чисел", Assert.Single(Compare.Arrays(matrix, NpyArray.Of(new float[6], 2, 3), MatchLevel.Bitwise)), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => NpyArray.Of(new double[5], 2, 3));
    }

    [Fact]
    public void IntegersAlwaysCompareExactly()
    {
        NpyArray expected = NpyArray.Of(new long[] { long.MaxValue, 0 }, 2);
        Assert.Single(Compare.Arrays(expected, NpyArray.Of(new long[] { long.MaxValue - 1, 0 }, 2), MatchLevel.Tolerance(1e-3)));
    }

    [Fact]
    public void ManyMismatchesAreSummed()
    {
        IReadOnlyList<string> errors = Compare.Arrays(NpyArray.Of(new double[100], 100), NpyArray.Of(Enumerable.Repeat(1.0, 100).ToArray(), 100), MatchLevel.Bitwise, max: 3);
        Assert.Equal(4, errors.Count);
        Assert.Contains("всего расхождений 100 из 100", errors[3], StringComparison.Ordinal);
    }
}
