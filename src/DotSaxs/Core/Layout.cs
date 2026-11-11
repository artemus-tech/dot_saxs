using System;
using System.Runtime.CompilerServices;

namespace DotSaxs.Core;

/// <summary>Сколько измерений бывает у массива: до Tensor8D (DECISIONS.md п. 24; в NumPy — до 64).</summary>
internal static class Dimensions
{
    /// <summary>Предел числа измерений — одна константа на всю библиотеку.</summary>
    internal const int Max = 8;
}

/// <summary>По одному числу на измерение — длины или шаги — без выделения памяти.</summary>
[InlineArray(Dimensions.Max)]
internal struct Dims
{
    private int _element0;
}

/// <summary>
/// Как лежат числа массива — то же, что shape и strides у ndarray в NumPy: где первое число, сколько чисел по каждому
/// измерению и шаг между соседними числами измерения, в числах (бывает отрицательным). Первое измерение — внешнее,
/// как в NumPy. Публичные типы (Vector … Tensor8D) — числа и такое описание.
/// </summary>
internal readonly struct Layout
{
    private readonly Dims _shape;
    private readonly Dims _strides;

    /// <summary>Первое число — <paramref name="offset"/>, длины и шаги по измерениям.</summary>
    internal Layout(int offset, ReadOnlySpan<int> shape, ReadOnlySpan<int> strides)
    {
        if (shape.Length > Dimensions.Max || strides.Length != shape.Length)
            throw new ArgumentOutOfRangeException(nameof(shape));
        Dims lengths = default, steps = default;
        int size = 1;
        for (int i = 0; i < shape.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(shape[i], nameof(shape));
            lengths[i] = shape[i];
            steps[i] = strides[i];
            size = checked(size * shape[i]);
        }
        _shape = lengths;
        _strides = steps;
        Offset = offset;
        Rank = shape.Length;
        Size = size;
    }

    /// <summary>Числа подряд по строкам (порядок C) с начала массива.</summary>
    internal static Layout Contiguous(ReadOnlySpan<int> shape)
    {
        Span<int> strides = stackalloc int[shape.Length];
        int step = 1;
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            strides[i] = step;
            step = checked(step * Math.Max(shape[i], 1));
        }
        return new Layout(0, shape, strides);
    }

    /// <summary>Где первое число.</summary>
    internal int Offset { get; }

    /// <summary>Сколько измерений.</summary>
    internal int Rank { get; }

    /// <summary>Сколько всего чисел.</summary>
    internal int Size { get; }

    /// <summary>Длина измерения <paramref name="axis"/>.</summary>
    internal int Shape(int axis) => _shape[axis];

    /// <summary>Шаг измерения <paramref name="axis"/> в числах.</summary>
    internal int Stride(int axis) => _strides[axis];
}
