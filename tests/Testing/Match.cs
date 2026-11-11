using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace DotSaxs.Testing;

/// <summary>
/// Уровень совпадения порта с эталоном (SPEC.md п. 8): побитово, до N ULP или по допуску. Целые и bool всегда
/// сравниваются точно. Любой NaN совпадает с любым NaN на всех уровнях: знак и «начинка» NaN зависят от процессора
/// (DECISIONS.md п. 15). Бесконечность — только с такой же бесконечностью.
/// </summary>
public abstract record MatchLevel
{
    /// <summary>Побитово: те же биты, включая знак нуля; любые два NaN равны (арифметика, суммы, целые, random).</summary>
    public static MatchLevel Bitwise { get; } = new BitwiseMatch();

    /// <summary>Не дальше <paramref name="ulps"/> шагов между соседними числами (exp, log, sin…).</summary>
    public static MatchLevel Ulp(int ulps) =>
        ulps >= 0 ? new UlpMatch(ulps) : throw new ArgumentOutOfRangeException(nameof(ulps), ulps, "число ULP не может быть отрицательным");

    /// <summary>|порт − эталон| ≤ atol + rtol·|эталон| (BLAS-3, LAPACK, интеграторы, оптимизаторы, БПФ).</summary>
    public static MatchLevel Tolerance(double rtol, double atol = 0) =>
        rtol >= 0 && atol >= 0 ? new ToleranceMatch(rtol, atol) : throw new ArgumentOutOfRangeException(nameof(rtol), "допуски не могут быть отрицательными");

    /// <summary>Расхождение двух double или <c>null</c>, если на этом уровне они совпали.</summary>
    public abstract string? Compare(double expected, double actual);

    /// <summary>Расхождение двух float (ULP — в шагах float) или <c>null</c>.</summary>
    public abstract string? Compare(float expected, float actual);

    /// <summary>Расхождение двух Half (ULP — в шагах Half) или <c>null</c>.</summary>
    public abstract string? Compare(Half expected, Half actual);

    /// <summary>Уровень из cases.json: <c>{"kind": "ulp", "ulp": 1}</c>.</summary>
    public static MatchLevel FromJson(JsonElement e) => e.GetProperty("kind").GetString() switch
    {
        "bitwise" => Bitwise,
        "ulp" => Ulp(e.GetProperty("ulp").GetInt32()),
        "tolerance" => Tolerance(e.GetProperty("rtol").GetDouble(), e.GetProperty("atol").GetDouble()),
        var kind => throw new InvalidDataException($"неизвестный уровень совпадения «{kind}»"),
    };

    /// <summary>Общие правила ULP и допуска: NaN совпадает только с NaN, бесконечность — с той же бесконечностью.</summary>
    private protected static bool? Special(double expected, double actual)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual))
            return double.IsNaN(expected) && double.IsNaN(actual);
        if (double.IsInfinity(expected) || double.IsInfinity(actual))
            return expected == actual;
        return null;
    }

    private protected static string Show(double x) =>
        $"{x.ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.DoubleToUInt64Bits(x):x16})";

    private protected static string Show(float x) =>
        $"{x.ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.SingleToUInt32Bits(x):x8})";

    private protected static string Show(Half x) =>
        $"{x.ToString(CultureInfo.InvariantCulture)} (0x{BitConverter.HalfToUInt16Bits(x):x4})";
}

/// <summary>
/// Побитовое совпадение. Любые два NaN равны: ∞ − ∞ на x86 даёт NaN с битами fff8…, на arm64 — 7ff8…
/// (DECISIONS.md п. 15). Всё остальное, включая знак нуля, — до бита.
/// </summary>
public sealed record BitwiseMatch : MatchLevel
{
    /// <inheritdoc/>
    public override string? Compare(double expected, double actual) =>
        BitConverter.DoubleToUInt64Bits(expected) == BitConverter.DoubleToUInt64Bits(actual) || (double.IsNaN(expected) && double.IsNaN(actual))
            ? null
            : $"ожидали {Show(expected)}, получили {Show(actual)}";

    /// <inheritdoc/>
    public override string? Compare(float expected, float actual) =>
        BitConverter.SingleToUInt32Bits(expected) == BitConverter.SingleToUInt32Bits(actual) || (float.IsNaN(expected) && float.IsNaN(actual))
            ? null
            : $"ожидали {Show(expected)}, получили {Show(actual)}";

    /// <inheritdoc/>
    public override string? Compare(Half expected, Half actual) =>
        BitConverter.HalfToUInt16Bits(expected) == BitConverter.HalfToUInt16Bits(actual) || (Half.IsNaN(expected) && Half.IsNaN(actual))
            ? null
            : $"ожидали {Show(expected)}, получили {Show(actual)}";

    /// <inheritdoc/>
    public override string ToString() => "побитово";
}

/// <summary>
/// Совпадение до <paramref name="Ulps"/> шагов между соседними числами. Два нуля совпадают, только если у них один знак:
/// у sin, tanh, expm1 и других функций знак нуля задан точно (sin(−0) = −0).
/// </summary>
public sealed record UlpMatch(int Ulps) : MatchLevel
{
    /// <inheritdoc/>
    public override string? Compare(double expected, double actual) =>
        Verdict(Special(expected, actual) ?? Zeros(expected, actual) ?? DotSaxs.Testing.Ulps.Between(expected, actual) <= (ulong)Ulps,
            () => $"ожидали {Show(expected)}, получили {Show(actual)}: {DotSaxs.Testing.Ulps.Between(expected, actual)} ULP");

    /// <inheritdoc/>
    public override string? Compare(float expected, float actual) =>
        Verdict(Special(expected, actual) ?? Zeros(expected, actual) ?? DotSaxs.Testing.Ulps.Between(expected, actual) <= (ulong)Ulps,
            () => $"ожидали {Show(expected)}, получили {Show(actual)}: {DotSaxs.Testing.Ulps.Between(expected, actual)} ULP");

    /// <inheritdoc/>
    public override string? Compare(Half expected, Half actual) =>
        Verdict(Special((double)expected, (double)actual) ?? Zeros((double)expected, (double)actual) ?? DotSaxs.Testing.Ulps.Between(expected, actual) <= (ulong)Ulps,
            () => $"ожидали {Show(expected)}, получили {Show(actual)}: {DotSaxs.Testing.Ulps.Between(expected, actual)} ULP");

    /// <inheritdoc/>
    public override string ToString() => $"до {Ulps} ULP";

    private string? Verdict(bool same, Func<string> why) => same ? null : $"{why()} (можно {Ulps})";

    /// <summary>Оба числа — нули: совпадают, если знак один; иначе — <c>null</c>, решает расстояние в ULP.</summary>
    private static bool? Zeros(double expected, double actual) =>
        expected == 0 && actual == 0 ? double.IsNegative(expected) == double.IsNegative(actual) : null;
}

/// <summary>Совпадение по допуску: |порт − эталон| ≤ <paramref name="Atol"/> + <paramref name="Rtol"/>·|эталон|.</summary>
public sealed record ToleranceMatch(double Rtol, double Atol) : MatchLevel
{
    /// <inheritdoc/>
    public override string? Compare(double expected, double actual) =>
        (Special(expected, actual) ?? Math.Abs(actual - expected) <= Atol + Rtol * Math.Abs(expected))
            ? null
            : $"ожидали {Show(expected)}, получили {Show(actual)}: разница {Math.Abs(actual - expected):R}, можно {Atol + Rtol * Math.Abs(expected):R}";

    /// <inheritdoc/>
    public override string? Compare(float expected, float actual) => Compare((double)expected, (double)actual);

    /// <inheritdoc/>
    public override string? Compare(Half expected, Half actual) => Compare((double)expected, (double)actual);

    /// <inheritdoc/>
    public override string ToString() => $"допуск rtol={Rtol:R}, atol={Atol:R}";
}

/// <summary>Расстояние в ULP: сколько представимых чисел между двумя значениями (±0 — одно и то же место).</summary>
public static class Ulps
{
    /// <summary>Шаги double между a и b.</summary>
    public static ulong Between(double a, double b) =>
        Distance(Ordered(BitConverter.DoubleToInt64Bits(a), long.MinValue), Ordered(BitConverter.DoubleToInt64Bits(b), long.MinValue));

    /// <summary>Шаги float между a и b.</summary>
    public static ulong Between(float a, float b) =>
        Distance(Ordered(BitConverter.SingleToInt32Bits(a), int.MinValue), Ordered(BitConverter.SingleToInt32Bits(b), int.MinValue));

    /// <summary>Шаги Half между a и b.</summary>
    public static ulong Between(Half a, Half b) =>
        Distance(Ordered(BitConverter.HalfToInt16Bits(a), short.MinValue), Ordered(BitConverter.HalfToInt16Bits(b), short.MinValue));

    /// <summary>Биты числа → целое того же порядка, что и числа: отрицательные идут вниз от нуля.</summary>
    private static long Ordered(long bits, long signBit) => bits < 0 ? signBit - bits : bits;

    private static ulong Distance(long x, long y) => x >= y ? unchecked((ulong)x - (ulong)y) : unchecked((ulong)y - (ulong)x);
}

/// <summary>Сравнение массива порта с эталоном (SPEC.md п. 8).</summary>
public static class Compare
{
    /// <summary>
    /// Расхождения: размеры, вид чисел и элементы на уровне <paramref name="level"/> (не больше <paramref name="max"/>
    /// описаний и итог). Пустой список — совпали.
    /// </summary>
    public static IReadOnlyList<string> Arrays(NpyArray expected, NpyArray actual, MatchLevel level, int max = 10)
    {
        if (!expected.Shape.SequenceEqual(actual.Shape))
            return [$"размеры: у эталона ({string.Join(", ", expected.Shape)}), у порта ({string.Join(", ", actual.Shape)})"];
        if (expected.Data.GetType() != actual.Data.GetType())
            return [$"вид чисел: у эталона {expected.Descr}, у порта {actual.Descr}"];
        bool single = expected.Type == "c8";
        var errors = new List<string>();
        int mismatches = 0;
        for (int i = 0; i < expected.Data.Length; i++)
        {
            string? why = Element(expected.Data, actual.Data, i, level, single);
            if (why is not null && ++mismatches <= max)
                errors.Add($"[{Index(i, expected.Shape)}] {why}");
        }
        if (mismatches > max)
            errors.Add($"… всего расхождений {mismatches} из {expected.Data.Length} ({level})");
        return errors;
    }

    private static string? Element(Array expected, Array actual, int i, MatchLevel level, bool single) => (expected, actual) switch
    {
        (double[] e, double[] a) => level.Compare(e[i], a[i]),
        (float[] e, float[] a) => level.Compare(e[i], a[i]),
        (Half[] e, Half[] a) => level.Compare(e[i], a[i]),
        (Complex[] e, Complex[] a) when single => Both(level.Compare((float)e[i].Real, (float)a[i].Real), level.Compare((float)e[i].Imaginary, (float)a[i].Imaginary)),
        (Complex[] e, Complex[] a) => Both(level.Compare(e[i].Real, a[i].Real), level.Compare(e[i].Imaginary, a[i].Imaginary)),
        _ => Equals(expected.GetValue(i), actual.GetValue(i)) ? null : $"ожидали {expected.GetValue(i)}, получили {actual.GetValue(i)}",
    };

    private static string? Both(string? real, string? imaginary) =>
        real is null && imaginary is null ? null : $"действительная часть: {real ?? "совпала"}; мнимая: {imaginary ?? "совпала"}";

    /// <summary>Номер элемента в порядке C → индексы: 7 в (3, 4) → «1, 3».</summary>
    private static string Index(int flat, IReadOnlyList<int> shape)
    {
        if (shape.Count == 0)
            return "";
        int[] index = new int[shape.Count];
        for (int k = shape.Count - 1; k >= 0; k--)
        {
            index[k] = flat % shape[k];
            flat /= shape[k];
        }
        return string.Join(", ", index);
    }
}
