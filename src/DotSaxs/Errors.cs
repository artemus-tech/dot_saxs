using System;

namespace DotSaxs;

// Исключения с именами из Python (DECISIONS.md п. 32): перенесённые функции бросают тот же тип, что NumPy и SciPy.
// Каждое наследует похожее исключение .NET, поэтому ловится и как обычное: ValueError — как ArgumentException.

/// <summary>
/// Значение подходящего вида, но недопустимое — как <c>ValueError</c> в Python. Ловится и как
/// <see cref="ArgumentException"/>.
/// </summary>
public class ValueError : ArgumentException
{
    /// <summary>Исключение с текстом.</summary>
    /// <param name="message">Текст.</param>
    public ValueError(string message)
        : base(message)
    {
    }

    /// <summary>Исключение с текстом и исключением, из-за которого оно возникло.</summary>
    /// <param name="message">Текст.</param>
    /// <param name="innerException">Исходное исключение.</param>
    public ValueError(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Номер за пределами массива — как <c>IndexError</c> в Python. Ловится и как
/// <see cref="ArgumentOutOfRangeException"/>, как у <c>List&lt;T&gt;</c>.
/// </summary>
public class IndexError : ArgumentOutOfRangeException
{
    /// <summary>Исключение с текстом.</summary>
    /// <param name="message">Текст.</param>
    public IndexError(string message)
        : base(null, message)
    {
    }
}

/// <summary>Ошибка в вычислениях — как <c>ArithmeticError</c> в Python. Ловится и как <see cref="ArithmeticException"/>.</summary>
public class ArithmeticError : ArithmeticException
{
    /// <summary>Исключение с текстом.</summary>
    /// <param name="message">Текст.</param>
    public ArithmeticError(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Деление на ноль, переполнение или недопустимое значение в вычислениях с плавающей точкой — как
/// <c>FloatingPointError</c> в NumPy с <c>np.seterr(all="raise")</c>. У нас это поведение по умолчанию; отключается
/// конфигом программы или блоком <see cref="Np.ErrState"/> (DECISIONS.md п. 32). Текст — как у NumPy:
/// <c>invalid value encountered in reduce</c>.
/// </summary>
public class FloatingPointError : ArithmeticError
{
    /// <summary>Исключение с текстом.</summary>
    /// <param name="message">Текст.</param>
    public FloatingPointError(string message)
        : base(message)
    {
    }
}
