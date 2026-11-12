using System;
using System.Threading;

namespace DotSaxs;

/// <summary>Что делать с ошибкой чисел (DECISIONS.md п. 32).</summary>
internal enum ErrorMode
{
    /// <summary>Молчать: результат — inf или nan, как в обычном C#.</summary>
    Ignore,

    /// <summary>Бросить <see cref="FloatingPointError"/>.</summary>
    Raise,
}

/// <summary>Режимы по видам ошибок чисел, как у <c>np.errstate</c>.</summary>
internal readonly record struct ErrorModes(ErrorMode Divide, ErrorMode Over, ErrorMode Under, ErrorMode Invalid);

/// <summary>Какие ошибки чисел случились при расчёте — то, что NumPy читает из флагов процессора.</summary>
[Flags]
internal enum FloatFlags
{
    /// <summary>Ошибок не было.</summary>
    None = 0,

    /// <summary>Деление конечного числа на ноль.</summary>
    Divide = 1,

    /// <summary>Из конечных чисел получилась бесконечность.</summary>
    Over = 2,

    /// <summary>Результат округлился до нуля или субнормального числа с потерей точности.</summary>
    Under = 4,

    /// <summary>Из чисел без nan получился nan: inf − inf, 0 / 0, sqrt(−1).</summary>
    Invalid = 8,
}

/// <summary>
/// Ошибки чисел (DECISIONS.md п. 32): режимы берутся из открытого блока <see cref="Np.ErrState"/>, иначе из конфига
/// программы, иначе по умолчанию (деление, переполнение, недопустимое значение — исключение; underflow — молча).
/// Как и в NumPy, ошибки проверяются после расчёта всего массива.
/// </summary>
internal static class FloatErrors
{
    private static readonly AsyncLocal<ErrorModes?> Scope = new();

    /// <summary>Режимы сейчас.</summary>
    internal static ErrorModes Current => Scope.Value ?? Settings.ErrorModes;

    /// <summary>Открыть блок с другими режимами; вернуть прежний блок, чтобы потом его восстановить.</summary>
    internal static ErrorModes? Enter(ErrorModes modes)
    {
        ErrorModes? previous = Scope.Value;
        Scope.Value = modes;
        return previous;
    }

    /// <summary>Закрыть блок: вернуть прежние режимы.</summary>
    internal static void Leave(ErrorModes? previous) => Scope.Value = previous;

    /// <summary>
    /// Бросить <see cref="FloatingPointError"/>, если случилась ошибка, для которой режим — raise. Текст — как у NumPy:
    /// «invalid value encountered in reduce»; <paramref name="name"/> — имя функции NumPy (reduce, log, exp…).
    /// </summary>
    internal static void Check(FloatFlags flags, string name)
    {
        if (flags == FloatFlags.None)
            return;
        ErrorModes modes = Current;
        // ref: numpy/_core/src/umath/extobj.c#L66-L69 — порядок проверки: деление, переполнение, underflow, недопустимое значение
        Throw(flags, FloatFlags.Divide, modes.Divide, "divide by zero", name);
        Throw(flags, FloatFlags.Over, modes.Over, "overflow", name);
        Throw(flags, FloatFlags.Under, modes.Under, "underflow", name);
        Throw(flags, FloatFlags.Invalid, modes.Invalid, "invalid value", name);
    }

    /// <summary>Режим по слову NumPy: <c>ignore</c> или <c>raise</c>; <c>null</c> — не задан.</summary>
    internal static ErrorMode? Parse(string? mode) => mode switch
    {
        null => null,
        "ignore" => ErrorMode.Ignore,
        "raise" => ErrorMode.Raise,
        "warn" or "print" or "log" or "call" => throw new NotSupportedException(Messages.ErrorModeNotSupported(mode)),
        // ref: numpy/_core/src/umath/extobj.c#L193 — неизвестный режим
        _ => throw new ValueError($"invalid error mode '{mode}'"),
    };

    private static void Throw(FloatFlags flags, FloatFlags flag, ErrorMode mode, string what, string name)
    {
        // ref: numpy/_core/src/umath/extobj.c#L418 — текст исключения в режиме raise
        if ((flags & flag) != 0 && mode == ErrorMode.Raise)
            throw new FloatingPointError($"{what} encountered in {name}");
    }
}

/// <summary>
/// Настройки библиотеки — из конфига программы, файла <c>&lt;программа&gt;.runtimeconfig.json</c>, раздел
/// <c>configProperties</c> (DECISIONS.md п. 32). В проекте программы их задают так:
/// <c>&lt;RuntimeHostConfigurationOption Include="DotSaxs.Language" Value="ru" /&gt;</c>.
/// </summary>
internal static class Settings
{
    /// <summary>Язык своих текстов: <c>en</c> (по умолчанию) или <c>ru</c>.</summary>
    internal const string LanguageKey = "DotSaxs.Language";

    /// <summary>Начало ключей режимов ошибок чисел: <c>DotSaxs.ErrState.Divide</c>, <c>.Over</c>, <c>.Under</c>, <c>.Invalid</c>.</summary>
    internal const string ErrStatePrefix = "DotSaxs.ErrState.";

    /// <summary>Язык своих текстов; без ключа — английский.</summary>
    internal static string Language => AppContext.GetData(LanguageKey) as string ?? "en";

    /// <summary>Режимы ошибок чисел вне блоков <see cref="Np.ErrState"/>.</summary>
    internal static ErrorModes ErrorModes => new(
        Mode("Divide", ErrorMode.Raise),
        Mode("Over", ErrorMode.Raise),
        Mode("Under", ErrorMode.Ignore),
        Mode("Invalid", ErrorMode.Raise));

    private static ErrorMode Mode(string kind, ErrorMode fallback) =>
        FloatErrors.Parse(AppContext.GetData(ErrStatePrefix + kind) as string) ?? fallback;
}

/// <summary>Блок <see cref="Np.ErrState"/>: при закрытии возвращает прежние режимы.</summary>
internal sealed class ErrStateScope : IDisposable
{
    private readonly ErrorModes? _previous;
    private bool _closed;

    internal ErrStateScope(ErrorModes modes) => _previous = FloatErrors.Enter(modes);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        FloatErrors.Leave(_previous);
    }
}

public static partial class Np
{
    /// <summary>
    /// Блок кода со своими режимами ошибок чисел — как <c>with np.errstate(...)</c> в NumPy:
    /// <c>using (Np.ErrState(divide: "ignore")) { … }</c>. Режим — <c>"ignore"</c> (молчать, результат inf или nan)
    /// или <c>"raise"</c> (бросить <see cref="FloatingPointError"/>); <paramref name="all"/> задаёт все четыре сразу,
    /// отдельные параметры сильнее него. Вне блока режимы — из конфига программы (DECISIONS.md п. 32).
    /// </summary>
    /// <param name="all">Режим для всех видов ошибок.</param>
    /// <param name="divide">Деление на ноль.</param>
    /// <param name="over">Переполнение.</param>
    /// <param name="under">Underflow — результат округлился до нуля.</param>
    /// <param name="invalid">Недопустимое значение: inf − inf, 0 / 0.</param>
    /// <returns>Блок; закрывается в конце <c>using</c>.</returns>
    /// <exception cref="ValueError">Неизвестный режим — текст как у NumPy: <c>invalid error mode 'foo'</c>.</exception>
    /// <exception cref="NotSupportedException">Режимы <c>warn</c>, <c>print</c>, <c>log</c>, <c>call</c> пока не поддерживаются.</exception>
    /// <remarks>
    /// Оригинал: numpy.errstate — NumPy v2.5.4, numpy/_core/_ufunc_config.py, строка 387
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/_ufunc_config.py#L387
    /// </remarks>
    [Original("numpy.errstate", "numpy/_core/_ufunc_config.py", 387)]
    public static IDisposable ErrState(string? all = null, string? divide = null, string? over = null, string? under = null,
        string? invalid = null)
    {
        ErrorModes current = FloatErrors.Current;
        ErrorMode? every = FloatErrors.Parse(all);
        var modes = new ErrorModes(
            FloatErrors.Parse(divide) ?? every ?? current.Divide,
            FloatErrors.Parse(over) ?? every ?? current.Over,
            FloatErrors.Parse(under) ?? every ?? current.Under,
            FloatErrors.Parse(invalid) ?? every ?? current.Invalid);
        return new ErrStateScope(modes);
    }
}
