using System;

namespace DotSaxs.Core;

/// <summary>Новая память под числа массива.</summary>
internal static class Memory
{
    /// <summary>
    /// Массив из <paramref name="count"/> чисел в нулях. Больше <see cref="Array.MaxLength"/> чисел массив .NET не
    /// держит — <see cref="MemoryError"/> (DECISIONS.md п. 14).
    /// </summary>
    internal static T[] Allocate<T>(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Array.MaxLength)
            throw new MemoryError(Messages.TooManyNumbers(count, Array.MaxLength));
        return new T[count];
    }
}
