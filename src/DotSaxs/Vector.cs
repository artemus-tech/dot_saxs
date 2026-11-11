using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DotSaxs;

/// <summary>
/// Одномерный массив чисел — как одномерный массив NumPy (DECISIONS.md п. 24). Вид чисел — параметр типа:
/// <c>Vector&lt;double&gt;</c>, <c>Vector&lt;long&gt;</c>, <c>Vector&lt;bool&gt;</c>. Поэлементные вычисления —
/// через <see cref="Select{TResult}(Func{T, TResult})"/> и LINQ; суммы — свои, как в NumPy (<see cref="Sums"/>).
/// </summary>
/// <remarks>
/// Вектор не меняется после создания: методы возвращают новый вектор. Строка и столбец матрицы — тоже векторы,
/// без копирования чисел. Имя совпадает с <c>System.Numerics.Vector&lt;T&gt;</c>: в файле с
/// <c>using System.Numerics</c> пишите <c>DotSaxs.Vector&lt;double&gt;</c> или псевдоним через <c>using</c>.
/// </remarks>
/// <typeparam name="T">Вид чисел.</typeparam>
public sealed class Vector<T> : IReadOnlyList<T>
    where T : unmanaged
{
    private readonly T[] _data;
    private readonly int _offset;
    private readonly int _stride;

    /// <summary>Вектор поверх чисел <paramref name="data"/>: <paramref name="count"/> чисел с шагом <paramref name="stride"/>.</summary>
    internal Vector(T[] data, int offset, int count, int stride)
    {
        _data = data;
        _offset = offset;
        Count = count;
        _stride = stride;
    }

    /// <summary>Вектор из чисел по порядку — как <c>np.array(values)</c>: <c>Vector&lt;double&gt;.From([0.1, 0.2])</c>.</summary>
    /// <param name="values">Числа.</param>
    /// <returns>Новый вектор; числа скопированы.</returns>
    public static Vector<T> From(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] data = values.ToArray();
        return new Vector<T>(data, 0, data.Length, 1);
    }

    /// <summary>Сколько чисел в векторе.</summary>
    public int Count { get; }

    /// <summary>Число с номером <paramref name="index"/>, с нуля.</summary>
    /// <param name="index">Номер: от 0 до <see cref="Count"/> − 1.</param>
    /// <exception cref="IndexError">Номер за пределами вектора.</exception>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new IndexError(Messages.IndexOutOfVector(index, Count));
            return _data[_offset + index * _stride];
        }
    }

    /// <summary>Новое значение для каждого числа: <c>v.Select(Np.Exp)</c>, <c>v.Select(x => x * 2)</c>.</summary>
    /// <param name="selector">Что сделать с числом.</param>
    /// <typeparam name="TResult">Вид чисел результата.</typeparam>
    /// <returns>Новый вектор той же длины.</returns>
    public Vector<TResult> Select<TResult>(Func<T, TResult> selector)
        where TResult : unmanaged
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = new TResult[Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = selector(_data[_offset + i * _stride]);
        return new Vector<TResult>(result, 0, result.Length, 1);
    }

    /// <summary>
    /// Новое значение для каждого числа с его номером — тот же цикл <c>for</c>: сложение двух векторов —
    /// <c>v.Select((x, i) => x + w[i])</c> (DECISIONS.md п. 24). Длины других векторов в формуле не сверяются: короче —
    /// <see cref="IndexError"/>, длиннее — возьмётся начало.
    /// </summary>
    /// <param name="selector">Что сделать с числом и его номером.</param>
    /// <typeparam name="TResult">Вид чисел результата.</typeparam>
    /// <returns>Новый вектор той же длины.</returns>
    public Vector<TResult> Select<TResult>(Func<T, int, TResult> selector)
        where TResult : unmanaged
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = new TResult[Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = selector(_data[_offset + i * _stride], i);
        return new Vector<TResult>(result, 0, result.Length, 1);
    }

    /// <summary>
    /// Матрица <paramref name="rows"/> × <paramref name="columns"/> из чисел вектора по строкам — как
    /// <c>np.reshape(v, (rows, columns))</c>.
    /// </summary>
    /// <param name="rows">Сколько строк.</param>
    /// <param name="columns">Сколько столбцов.</param>
    /// <returns>Матрица; числа общие с вектором.</returns>
    /// <exception cref="ValueError">Чисел в векторе не столько, сколько в матрице.</exception>
    public Matrix<T> ToMatrix(int rows, int columns)
    {
        if (rows < 0 || columns < 0 || (long)rows * columns != Count)
            throw new ValueError(Messages.VectorToMatrix(Count, rows, columns));
        if (_stride == 1)
            return new Matrix<T>(_data, _offset, rows, columns);
        var data = new T[Count];
        CopyTo(data, 0);
        return new Matrix<T>(data, 0, rows, columns);
    }

    /// <summary>Числа по порядку — для LINQ и <c>foreach</c>.</summary>
    /// <returns>Перечислитель.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return _data[_offset + i * _stride];
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Числа, на которые смотрит вектор (общие с матрицей, если это её строка или столбец).</summary>
    internal T[] Data => _data;

    /// <summary>Где в <see cref="Data"/> первое число.</summary>
    internal int Offset => _offset;

    /// <summary>Шаг между числами в <see cref="Data"/>.</summary>
    internal int Stride => _stride;

    /// <summary>Как лежат числа вектора в <see cref="Data"/> — для обхода ядра (Core).</summary>
    internal Core.Layout Layout => new(_offset, [Count], [_stride]);

    /// <summary>Скопировать числа подряд в <paramref name="destination"/>, начиная с <paramref name="index"/>.</summary>
    internal void CopyTo(T[] destination, int index)
    {
        if (_stride == 1)
        {
            Array.Copy(_data, _offset, destination, index, Count);
            return;
        }
        for (int i = 0; i < Count; i++)
            destination[index + i] = _data[_offset + i * _stride];
    }
}

/// <summary>Сборка своих типов из последовательностей LINQ (DECISIONS.md п. 24).</summary>
public static class LinqExtensions
{
    /// <summary>Вектор из чисел последовательности: <c>v.Where(x => x > 0).ToVector()</c>.</summary>
    /// <param name="values">Числа.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <returns>Новый вектор.</returns>
    public static Vector<T> ToVector<T>(this IEnumerable<T> values)
        where T : unmanaged =>
        Vector<T>.From(values);
}
