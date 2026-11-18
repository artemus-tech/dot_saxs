using System;
using System.Linq;
using DotSaxs.Testing;
using Xunit;
using static DotSaxs.Tests.ReferenceChecks;

namespace DotSaxs.Tests;

/// <summary>
/// Своя статистика вектора против эталонов NumPy (DECISIONS.md п. 24): Max, Min, IndexOfMax, IndexOfMin, Mean,
/// Variance, StandardDeviation, Median, Product, CumulativeSum, CumulativeProduct, Diff, Sort, IndicesOfValuesOrdered.
/// Каждый кейс эталона — в тестах ниже, до бита; виды с шагом — векторы с тем же шагом.
/// </summary>
public sealed class StatisticsTests
{
    public static TheoryData<string, string> ExtremeCases() => Cases("numpy.max", "numpy.min");

    public static TheoryData<string, string> IndexCases() => Cases("numpy.argmax", "numpy.argmin");

    public static TheoryData<string, string> MomentCases() => Cases("numpy.mean", "numpy.var", "numpy.std");

    public static TheoryData<string, string> MedianCases() => Cases("numpy.median");

    public static TheoryData<string, string> ProductCases() => Cases("numpy.prod", "numpy.cumsum", "numpy.cumprod");

    public static TheoryData<string, string> DiffCases() => Cases("numpy.diff");

    public static TheoryData<string, string> SortCases() => Cases("numpy.sort", "numpy.argsort");

    [Theory]
    [MemberData(nameof(ExtremeCases))]
    public void MaxAndMinMatchNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        bool max = python == "numpy.max";
        Check(c, () => Type(c, "a") switch
        {
            "f8" => Scalar(max ? Input<double>(c, "a").Max() : Input<double>(c, "a").Min()),
            "f4" => Scalar(max ? Input<float>(c, "a").Max() : Input<float>(c, "a").Min()),
            "i8" => Scalar(max ? Input<long>(c, "a").Max() : Input<long>(c, "a").Min()),
            string type => throw new NotSupportedException(type),
        });
    }

    [Theory]
    [MemberData(nameof(IndexCases))]
    public void IndexOfMaxAndMinMatchArgMaxAndArgMin(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        bool max = python == "numpy.argmax";
        Check(c, () => Scalar((long)(Type(c, "a") switch
        {
            "f8" => max ? Input<double>(c, "a").IndexOfMax() : Input<double>(c, "a").IndexOfMin(),
            "f4" => max ? Input<float>(c, "a").IndexOfMax() : Input<float>(c, "a").IndexOfMin(),
            "i8" => max ? Input<long>(c, "a").IndexOfMax() : Input<long>(c, "a").IndexOfMin(),
            string type => throw new NotSupportedException(type),
        })));
    }

    [Theory]
    [MemberData(nameof(MomentCases))]
    public void MeanVarianceAndStandardDeviationMatchNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        int ddof = Int(c, "ddof", 0);
        Check(c, () => (python, Type(c, "a")) switch
        {
            ("numpy.mean", "f8") => Scalar(Input<double>(c, "a").Mean()),
            ("numpy.mean", "f4") => Scalar(Input<float>(c, "a").Mean()),
            ("numpy.mean", "i8") => Scalar(Input<long>(c, "a").Mean()),
            ("numpy.var", "f8") => Scalar(Input<double>(c, "a").Variance(ddof)),
            ("numpy.var", "f4") => Scalar(Input<float>(c, "a").Variance(ddof)),
            ("numpy.var", "i8") => Scalar(Input<long>(c, "a").Variance(ddof)),
            ("numpy.std", "f8") => Scalar(Input<double>(c, "a").StandardDeviation(ddof)),
            ("numpy.std", "f4") => Scalar(Input<float>(c, "a").StandardDeviation(ddof)),
            ("numpy.std", "i8") => Scalar(Input<long>(c, "a").StandardDeviation(ddof)),
            var other => throw new NotSupportedException(other.ToString()),
        });
    }

    [Theory]
    [MemberData(nameof(MedianCases))]
    public void MedianMatchesNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Check(c, () => Type(c, "a") switch
        {
            "f8" => Scalar(Input<double>(c, "a").Median()),
            "f4" => Scalar(Input<float>(c, "a").Median()),
            "i8" => Scalar(Input<long>(c, "a").Median()),
            string type => throw new NotSupportedException(type),
        });
    }

    [Theory]
    [MemberData(nameof(ProductCases))]
    public void ProductsAndCumulativeMatchNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Check(c, () => (python, Type(c, "a")) switch
        {
            ("numpy.prod", "f8") => Scalar(Input<double>(c, "a").Product()),
            ("numpy.prod", "f4") => Scalar(Input<float>(c, "a").Product()),
            ("numpy.prod", "i8") => Scalar(Input<long>(c, "a").Product()),
            ("numpy.cumsum", "f8") => ToNpy(Input<double>(c, "a").CumulativeSum()),
            ("numpy.cumsum", "f4") => ToNpy(Input<float>(c, "a").CumulativeSum()),
            ("numpy.cumsum", "i8") => ToNpy(Input<long>(c, "a").CumulativeSum()),
            ("numpy.cumprod", "f8") => ToNpy(Input<double>(c, "a").CumulativeProduct()),
            ("numpy.cumprod", "f4") => ToNpy(Input<float>(c, "a").CumulativeProduct()),
            ("numpy.cumprod", "i8") => ToNpy(Input<long>(c, "a").CumulativeProduct()),
            var other => throw new NotSupportedException(other.ToString()),
        });
    }

    [Theory]
    [MemberData(nameof(DiffCases))]
    public void DiffMatchesNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Check(c, () => Type(c, "a") switch
        {
            "f8" => ToNpy(Input<double>(c, "a").Diff()),
            "f4" => ToNpy(Input<float>(c, "a").Diff()),
            "i8" => ToNpy(Input<long>(c, "a").Diff()),
            "b1" => ToNpy(Input<bool>(c, "a").Diff()),
            string type => throw new NotSupportedException($"{python}: {type}"),
        });
    }

    [Theory]
    [MemberData(nameof(SortCases))]
    public void SortAndIndicesMatchStableNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        bool descending = Bool(c, "descending", false);
        bool sort = python == "numpy.sort";
        Check(c, () => Type(c, "a") switch
        {
            "f8" => Sorted(Input<double>(c, "a"), sort, descending),
            "f4" => Sorted(Input<float>(c, "a"), sort, descending),
            "i8" => Sorted(Input<long>(c, "a"), sort, descending),
            string type => throw new NotSupportedException(type),
        });
    }

    [Fact]
    public void LinqGivesOtherAnswersThanNumPy()
    {
        // LINQ Max пропускает NaN, NumPy и наш Max — нет; LINQ OrderBy ставит NaN первым, NumPy и наш Sort — последним.
        Vector<double> v = new[] { 1.0, double.NaN, 3.0 }.ToVector();
        Assert.Equal(3.0, Enumerable.Max(v));
        Assert.True(double.IsNaN(v.Max()));
        Assert.True(double.IsNaN(v.OrderBy(x => x).First()));
        Assert.Equal(new[] { 1.0, 3.0 }, v.Sort().Take(2).ToArray());
        Assert.True(double.IsNaN(v.Sort()[2]));
    }

    [Fact]
    public void IntegersOverflowThrowUnlessIgnored()
    {
        // NumPy молча идёт по кругу; у нас — исключение (DECISIONS.md п. 32), по кругу — только явно.
        Vector<long> big = new[] { long.MaxValue, 2L }.ToVector();
        Assert.Equal("overflow encountered in reduce", Assert.Throws<FloatingPointError>(() => big.Product()).Message);
        Assert.Equal("overflow encountered in accumulate", Assert.Throws<FloatingPointError>(() => big.CumulativeSum()).Message);
        Vector<int> ints = new[] { int.MinValue, int.MaxValue }.ToVector();
        Assert.Equal("overflow encountered in subtract", Assert.Throws<FloatingPointError>(() => ints.Diff()).Message);
        // int NumPy складывает в long (int64): переполнения int тут нет.
        Assert.Equal(new[] { (long)int.MaxValue, 2L * int.MaxValue }, new[] { int.MaxValue, int.MaxValue }.ToVector().CumulativeSum().ToArray());
        using (Np.ErrState(over: "ignore"))
        {
            Assert.Equal(-2L, big.Product());
            Assert.Equal(new[] { -1 }, ints.Diff().ToArray());
        }
    }

    [Fact]
    public void SampleVarianceDividesByCountMinusOne()
    {
        Vector<double> v = new[] { 1.0, 2.0, 3.0, 4.0 }.ToVector();
        Assert.Equal(1.25, v.Variance());
        Assert.Equal(5.0 / 3.0, v.Variance(deltaDegreesOfFreedom: 1));
        Assert.Equal(Math.Sqrt(5.0 / 3.0), v.StandardDeviation(deltaDegreesOfFreedom: 1));
        Assert.Equal(2.5, new[] { 4, 1, 3, 2 }.ToVector().Median());
    }

    private static NpyArray Sorted<T>(Vector<T> v, bool sort, bool descending)
        where T : unmanaged, System.Numerics.INumber<T>
    {
        if (sort)
            return ToNpy(descending ? v.SortDescending() : v.Sort());
        return Indices(descending ? v.IndicesOfValuesOrderedDescending() : v.IndicesOfValuesOrdered());
    }
}
