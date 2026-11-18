using System;
using System.Collections.Generic;
using System.Linq;
using DotSaxs.Testing;
using Xunit;

namespace DotSaxs.Tests;

/// <summary>
/// Общая сверка с эталоном (SPEC.md п. 8): ошибка Python — то же исключение с тем же текстом; предупреждения — по
/// умолчанию исключение с текстом первого, с <c>Np.ErrState(all: "ignore")</c> — числа (DECISIONS.md п. 32); иначе — числа
/// на уровне совпадения из эталона.
/// </summary>
internal static class ReferenceChecks
{
    /// <summary>Все кейсы функций — для <c>[MemberData]</c>: имя функции Python и имя кейса.</summary>
    public static TheoryData<string, string> Cases(params string[] functions)
    {
        var data = new TheoryData<string, string>();
        foreach (string python in functions)
        {
            foreach (ReferenceCase c in References.Load(python))
                data.Add(python, c.Name);
        }
        return data;
    }

    /// <summary>Сверить вызов порта <paramref name="run"/> с кейсом <paramref name="c"/>.</summary>
    public static void Check(ReferenceCase c, Func<NpyArray> run)
    {
        if (c.Error is { } error)
        {
            Exception e = Assert.ThrowsAny<Exception>(() => run());
            Assert.Equal((error.Type, error.Message), (e.GetType().Name, e.Message));
            return;
        }
        if (c.Warnings.Count > 0)
        {
            FloatingPointError e = Assert.Throws<FloatingPointError>(() => run());
            Assert.Equal(c.Warnings[0].Message, e.Message);
            using (Np.ErrState(all: "ignore"))
                Assert.Empty(Compare.Arrays(c.ResultArray(), run(), c.Match));
            return;
        }
        Assert.Empty(Compare.Arrays(c.ResultArray(), run(), c.Match));
    }

    /// <summary>Число Python из аргумента.</summary>
    public static double Float(ReferenceCase c, string name) => ((FloatValue)c.Argument(name)).Value;

    /// <summary>Целое Python из аргумента.</summary>
    public static int Int(ReferenceCase c, string name) => (int)((IntValue)c.Argument(name)).Value;

    /// <summary>Целое Python из именованного аргумента или значение по умолчанию.</summary>
    public static int Int(ReferenceCase c, string name, int fallback) =>
        c.Kwargs.TryGetValue(name, out ReferenceValue? value) ? (int)((IntValue)value).Value : fallback;

    /// <summary>Размеры: целое Python или кортеж целых.</summary>
    public static int[] Shape(ReferenceValue value) => value switch
    {
        IntValue n => [(int)n.Value],
        TupleValue t => t.Items.Select(x => (int)((IntValue)x).Value).ToArray(),
        _ => throw new InvalidCastException($"размеры — {value.GetType().Name}"),
    };

    /// <summary>Вектор из одномерного массива эталона.</summary>
    public static Vector<T> ToVector<T>(NpyArray a)
        where T : unmanaged => a.Get<T>().ToVector();

    /// <summary>Матрица из двумерного массива эталона.</summary>
    public static Matrix<double> ToMatrix(NpyArray a) => a.Get<double>().ToVector().ToMatrix(a.Shape[0], a.Shape[1]);

    /// <summary>Вектор порта — в виде эталона.</summary>
    public static NpyArray ToNpy<T>(Vector<T> v)
        where T : unmanaged => NpyArray.Of(v.ToArray(), v.Count);

    /// <summary>Матрица порта — в виде эталона.</summary>
    public static NpyArray ToNpy<T>(Matrix<T> m)
        where T : unmanaged => NpyArray.Of(m.Flatten().ToArray(), m.RowCount, m.ColumnCount);

    /// <summary>Булево число Python — результат кейса.</summary>
    public static bool BoolResult(ReferenceCase c) =>
        c.Result is BoolValue b ? b.Value : throw new InvalidCastException($"{c}: результат — не bool");

    /// <summary>Есть ли именованный аргумент.</summary>
    public static bool Has(ReferenceCase c, string name) => c.Kwargs.ContainsKey(name);

    /// <summary>Булево число Python из именованного аргумента или значение по умолчанию.</summary>
    public static bool Bool(ReferenceCase c, string name, bool fallback) =>
        c.Kwargs.TryGetValue(name, out ReferenceValue? value) ? ((BoolValue)value).Value : fallback;

    /// <summary>Имена кейсов функции.</summary>
    public static IEnumerable<string> Names(string python) => References.Load(python).Select(c => c.Name);
}
