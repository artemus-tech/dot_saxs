using System.Linq;
using System.Numerics;
using DotSaxs.Core;
using DotSaxs.Testing;
using Xunit;

namespace DotSaxs.Tests;

/// <summary>
/// Обход ядра (Core/Reduction.cs) против эталонов numpy.sum по видам — кейсы «view-…»: срезы, шаги, перестановка
/// размеров. Суммы совпадают до бита, только если и порядок сложений — как у NumPy.
/// </summary>
public sealed class ReductionReferenceTests
{
    /// <summary>Все кейсы эталона numpy.sum по видам.</summary>
    public static TheoryData<string> ViewCases() => new(ViewCaseNames());

    [Fact]
    public void EveryViewCaseIsAView()
    {
        string[] names = ViewCaseNames();
        Assert.True(names.Length >= 24, $"кейсов по видам: {names.Length}");
        Assert.All(names, name => References.Case("numpy.sum", name).ArrayView("a"));
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public void SumMatchesNumPyBitForBit(string name)
    {
        ReferenceCase c = References.Case("numpy.sum", name);
        (NpyArray array, NpyView view) = c.ArrayView("a");
        var layout = new Layout(view.Offset, view.Shape.ToArray(), view.Strides.ToArray());
        int reduced = c.Kwargs.TryGetValue("axis", out ReferenceValue? axis)
            ? 1 << (int)((IntValue)axis).Value
            : (1 << view.Shape.Count) - 1;
        int[] kept = Enumerable.Range(0, view.Shape.Count).Where(a => (reduced & (1 << a)) == 0).Select(a => view.Shape[a]).ToArray();
        NpyArray actual = array.Type switch
        {
            "f8" => NpyArray.Of(Sum(array.Get<double>(), layout, reduced), kept),
            "f4" => NpyArray.Of(Sum(array.Get<float>(), layout, reduced), kept),
            _ => throw new System.NotSupportedException(array.Descr),
        };
        Assert.Empty(Compare.Arrays(c.ResultArray(), actual, c.Match));
    }

    [Fact]
    public void ResultIsInOrderCWhateverTheWalkOrder()
    {
        // Матрица 2 × 3, взятая по столбцам (шаги 1 и 2): NumPy кладёт суммы в порядке обхода, мы отдаём в порядке C.
        double[] data = [1, 10, 2, 20, 3, 30];
        var columns = new Layout(0, [2, 3], [1, 2]);
        var plain = default(PlainAddition<double>);
        Assert.Equal(new[] { 6.0, 60.0 }, Reduction.Sum(data, columns, 0b10, ref plain));
        Assert.Equal(new[] { 11.0, 22.0, 33.0 }, Reduction.Sum(data, columns, 0b01, ref plain));
        Assert.Equal(66.0, Assert.Single(Reduction.Sum(data, columns, 0b11, ref plain)));

        // Куб 2 × 3 × 2 в порядке Фортрана (число [i, j, k] — это i + 2j + 6k): сумма по оси 2 — 2i + 4j + 6.
        double[] cube = Enumerable.Range(0, 12).Select(x => (double)x).ToArray();
        var fortran = new Layout(0, [2, 3, 2], [1, 2, 6]);
        Assert.Equal(new[] { 6.0, 10.0, 14.0, 8.0, 12.0, 16.0 }, Reduction.Sum(cube, fortran, 0b100, ref plain));
    }

    [Fact]
    public void EmptyAxesGiveZerosOrNothing()
    {
        var plain = default(PlainAddition<double>);
        var empty = new Layout(0, [0, 3], [3, 1]);
        Assert.Equal([0.0, 0.0, 0.0], Reduction.Sum(System.Array.Empty<double>(), empty, 0b01, ref plain));
        Assert.Empty(Reduction.Sum(System.Array.Empty<double>(), empty, 0b10, ref plain));
        double zero = Assert.Single(Reduction.Sum(System.Array.Empty<double>(), empty, 0b11, ref plain));
        Assert.False(double.IsNegative(zero));
    }

    private static string[] ViewCaseNames() =>
        References.Load("numpy.sum").Select(c => c.Name).Where(n => n.StartsWith("view-", System.StringComparison.Ordinal)).ToArray();

    private static T[] Sum<T>(T[] data, in Layout layout, int reduced)
        where T : IFloatingPointIeee754<T>
    {
        var plain = default(PlainAddition<T>);
        return Reduction.Sum(data, layout, reduced, ref plain);
    }
}
