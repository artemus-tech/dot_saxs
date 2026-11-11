using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace DotSaxs.Testing;

/// <summary>Массив из файла .npy: вид чисел, размеры и элементы в порядке C.</summary>
public sealed class NpyArray
{
    internal NpyArray(string descr, int[] shape, bool fortranOrder, (int Major, int Minor) version, Array data)
    {
        Descr = descr;
        Shape = shape;
        FortranOrder = fortranOrder;
        Version = version;
        Data = data;
    }

    /// <summary>Вид чисел, как в заголовке файла: <c>&lt;f8</c>, <c>|b1</c>, <c>&gt;i4</c>…</summary>
    public string Descr { get; }

    /// <summary>Вид чисел без порядка байт: <c>f8</c>, <c>i4</c>, <c>b1</c>, <c>c16</c>…</summary>
    public string Type => Descr[1..];

    /// <summary>Размеры; у 0-мерного массива (скаляра NumPy) — пусто.</summary>
    public IReadOnlyList<int> Shape { get; }

    /// <summary>В файле элементы лежали в порядке Fortran; в <see cref="Data"/> они уже в порядке C.</summary>
    public bool FortranOrder { get; }

    /// <summary>Версия формата из заголовка: 1.0, 2.0 или 3.0.</summary>
    public (int Major, int Minor) Version { get; }

    /// <summary>
    /// Элементы в порядке C: <c>double[]</c>, <c>float[]</c>, <c>Half[]</c>, <c>long[]</c>, <c>int[]</c>, <c>short[]</c>,
    /// <c>sbyte[]</c>, <c>ulong[]</c>, <c>uint[]</c>, <c>ushort[]</c>, <c>byte[]</c>, <c>bool[]</c> или <c>Complex[]</c>.
    /// </summary>
    public Array Data { get; }

    // Вид NumPy по типу элементов .NET.
    private static readonly Dictionary<Type, string> Descrs = new()
    {
        [typeof(double)] = "<f8",
        [typeof(float)] = "<f4",
        [typeof(Half)] = "<f2",
        [typeof(long)] = "<i8",
        [typeof(int)] = "<i4",
        [typeof(short)] = "<i2",
        [typeof(sbyte)] = "|i1",
        [typeof(ulong)] = "<u8",
        [typeof(uint)] = "<u4",
        [typeof(ushort)] = "<u2",
        [typeof(byte)] = "|u1",
        [typeof(bool)] = "|b1",
        [typeof(Complex)] = "<c16",
    };

    /// <summary>
    /// Элементы как массив нужного типа; другой тип — ошибка с понятным текстом. Тип сверяется точно: среда .NET
    /// считает совместимыми <c>int[]</c> и <c>uint[]</c> (так же long/ulong, short/ushort, sbyte/byte), и
    /// <c>as T[]</c> молча прочитал бы uint64 как long.
    /// </summary>
    public T[] Get<T>() =>
        Data.GetType() == typeof(T[])
            ? (T[])Data
            : throw new InvalidCastException($"в файле .npy вид {Descr} ({Data.GetType().Name}), а запрошен {typeof(T).Name}[]");

    /// <summary>Результат порта в том же виде, что эталон, — для <see cref="Compare.Arrays"/>. Элементы — в порядке C.</summary>
    public static NpyArray Of(Array data, params int[] shape)
    {
        ArgumentNullException.ThrowIfNull(data);
        // Вид — по точному типу элементов: сопоставление с образцом (data is long[]) приняло бы и ulong[].
        if (data.Rank != 1 || !Descrs.TryGetValue(data.GetType().GetElementType()!, out string? descr))
            throw new NotSupportedException($"массив {data.GetType().Name} не поддерживается");
        long count = 1;
        foreach (int d in shape)
            count = checked(count * d);
        if (count != data.Length)
            throw new ArgumentException($"размеры ({string.Join(", ", shape)}) — это {count} элементов, а в массиве {data.Length}", nameof(shape));
        return new NpyArray(descr, (int[])shape.Clone(), fortranOrder: false, (1, 0), data);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Descr} ({string.Join(", ", Shape)})";
}

/// <summary>
/// Чтение файлов .npy (формат NumPy, версии 1.0–3.0). Свой код для тестов, а не <c>Np.Load</c>: эталон не должен
/// читаться тем же кодом, который проверяется.
/// </summary>
public static class Npy
{
    private static readonly byte[] Magic = [0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y'];

    /// <summary>Читает файл .npy.</summary>
    public static NpyArray Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        try
        {
            return Read(stream);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
        catch (NotSupportedException e)
        {
            throw new NotSupportedException($"{path}: {e.Message}", e);
        }
    }

    /// <summary>Читает .npy из потока до конца.</summary>
    public static NpyArray Read(Stream stream)
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("чтение .npy написано для процессоров с порядком байт little-endian");
        byte[] prefix = ReadExactly(stream, 8, "начало файла");
        if (!prefix.AsSpan(0, 6).SequenceEqual(Magic))
            throw new InvalidDataException("это не файл .npy: нет метки \\x93NUMPY в начале");
        (int Major, int Minor) version = (prefix[6], prefix[7]);
        int headerLength = version switch
        {
            (1, 0) => BitConverter.ToUInt16(ReadExactly(stream, 2, "длина заголовка")),
            (2, 0) or (3, 0) => checked((int)BitConverter.ToUInt32(ReadExactly(stream, 4, "длина заголовка"))),
            _ => throw new NotSupportedException($"версия формата .npy {version.Major}.{version.Minor} не поддерживается"),
        };
        byte[] headerBytes = ReadExactly(stream, headerLength, "заголовок");
        string header = (version.Major == 3 ? Encoding.UTF8 : Encoding.Latin1).GetString(headerBytes);
        if (PyLiteral.Parse(header.TrimEnd(' ', '\n')) is not Dictionary<string, object?> fields)
            throw new InvalidDataException($"заголовок .npy — не словарь Python: {header.Trim()}");
        string descr = fields.GetValueOrDefault("descr") switch
        {
            string s => s,
            List<object?> => throw new NotSupportedException("структурные виды чисел (descr — список полей) не поддерживаются"),
            _ => throw new InvalidDataException($"в заголовке нет вида чисел descr: {header.Trim()}"),
        };
        if (fields.GetValueOrDefault("fortran_order") is not bool fortran)
            throw new InvalidDataException($"в заголовке нет fortran_order: {header.Trim()}");
        if (fields.GetValueOrDefault("shape") is not object[] dims)
            throw new InvalidDataException($"в заголовке нет размеров shape: {header.Trim()}");
        int[] shape = Array.ConvertAll(dims, d => d is long n && n >= 0 && n <= int.MaxValue
            ? (int)n
            : throw new InvalidDataException($"неверный размер в shape: {d}"));
        long count = 1;
        foreach (int d in shape)
            count = checked(count * d);
        (char kind, int size, bool big) = ParseDescr(descr);
        if (count * size > Array.MaxLength)
            throw new NotSupportedException($"массив {count} × {size} байт слишком велик для чтения в тестах");
        byte[] raw = ReadExactly(stream, (int)(count * size), "данные");
        if (stream.ReadByte() != -1)
            throw new InvalidDataException($"после данных ({count} элементов {descr}) в файле есть лишние байты");
        if (big)
            SwapBytes(raw, kind == 'c' ? size / 2 : size);
        Array data = Decode(kind, size, raw, descr);
        if (fortran && shape.Length > 1)
            data = ToCOrder(data, shape);
        return new NpyArray(descr, shape, fortran, version, data);
    }

    /// <summary>Вид чисел NumPy: <c>&lt;f8</c> → ('f', 8, little-endian).</summary>
    private static (char Kind, int Size, bool BigEndian) ParseDescr(string descr)
    {
        if (descr.Length < 3 || descr[0] is not ('<' or '>' or '|' or '=')
            || !int.TryParse(descr.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int size))
            throw new NotSupportedException($"вид чисел {descr} не поддерживается");
        char kind = descr[1];
        bool supported = (kind, size) is ('b', 1) or ('i' or 'u', 1 or 2 or 4 or 8) or ('f', 2 or 4 or 8) or ('c', 8 or 16);
        if (!supported)
            throw new NotSupportedException($"вид чисел {descr} не поддерживается: только bool, целые, float16/32/64 и complex64/128");
        return (kind, size, descr[0] == '>' && size > 1);
    }

    private static Array Decode(char kind, int size, byte[] raw, string descr)
    {
        ReadOnlySpan<byte> bytes = raw;
        return (kind, size) switch
        {
            ('b', 1) => Array.ConvertAll(raw, b => b != 0),
            ('i', 1) => MemoryMarshal.Cast<byte, sbyte>(bytes).ToArray(),
            ('u', 1) => raw,
            ('i', 2) => MemoryMarshal.Cast<byte, short>(bytes).ToArray(),
            ('u', 2) => MemoryMarshal.Cast<byte, ushort>(bytes).ToArray(),
            ('i', 4) => MemoryMarshal.Cast<byte, int>(bytes).ToArray(),
            ('u', 4) => MemoryMarshal.Cast<byte, uint>(bytes).ToArray(),
            ('i', 8) => MemoryMarshal.Cast<byte, long>(bytes).ToArray(),
            ('u', 8) => MemoryMarshal.Cast<byte, ulong>(bytes).ToArray(),
            ('f', 2) => MemoryMarshal.Cast<byte, Half>(bytes).ToArray(),
            ('f', 4) => MemoryMarshal.Cast<byte, float>(bytes).ToArray(),
            ('f', 8) => MemoryMarshal.Cast<byte, double>(bytes).ToArray(),
            ('c', 8) => ToComplex(MemoryMarshal.Cast<byte, float>(bytes)),
            ('c', 16) => ToComplex(MemoryMarshal.Cast<byte, double>(bytes)),
            _ => throw new NotSupportedException($"вид чисел {descr} не поддерживается"),
        };
    }

    private static Complex[] ToComplex(ReadOnlySpan<float> parts)
    {
        var result = new Complex[parts.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = new Complex(parts[2 * i], parts[2 * i + 1]);
        return result;
    }

    private static Complex[] ToComplex(ReadOnlySpan<double> parts)
    {
        var result = new Complex[parts.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = new Complex(parts[2 * i], parts[2 * i + 1]);
        return result;
    }

    private static void SwapBytes(byte[] raw, int size)
    {
        for (int i = 0; i < raw.Length; i += size)
            raw.AsSpan(i, size).Reverse();
    }

    /// <summary>Элементы, записанные в порядке Fortran (первый индекс меняется быстрее), — в порядок C.</summary>
    private static Array ToCOrder(Array fortran, int[] shape)
    {
        Array result = (Array)fortran.Clone();
        int[] index = new int[shape.Length];
        for (int c = 0; c < fortran.Length; c++)
        {
            int f = 0, stride = 1;
            for (int k = 0; k < shape.Length; k++)
            {
                f += index[k] * stride;
                stride *= shape[k];
            }
            Array.Copy(fortran, f, result, c, 1);
            for (int k = shape.Length - 1; k >= 0 && ++index[k] == shape[k]; k--)
                index[k] = 0;
        }
        return result;
    }

    private static byte[] ReadExactly(Stream stream, int count, string what)
    {
        byte[] buffer = new byte[count];
        int done = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        return done == count
            ? buffer
            : throw new InvalidDataException($"файл обрывается: {what} — нужно байт {count}, есть {done}");
    }
}

/// <summary>
/// Разбор литерала Python из заголовка .npy: словарь, кортеж, список, строка, целое, True, False, None.
/// Кортеж — <c>object?[]</c>, список — <c>List&lt;object?&gt;</c>, целое — <c>long</c>.
/// </summary>
internal sealed class PyLiteral
{
    private readonly string _text;
    private int _pos;

    private PyLiteral(string text) => _text = text;

    public static object? Parse(string text)
    {
        var parser = new PyLiteral(text);
        object? value = parser.Value();
        parser.SkipSpaces();
        return parser._pos == text.Length ? value : throw parser.Error("лишний текст после значения");
    }

    private object? Value()
    {
        SkipSpaces();
        if (_pos >= _text.Length)
            throw Error("заголовок обрывается");
        char c = _text[_pos];
        return c switch
        {
            '{' => Dict(),
            '(' => Sequence(')').ToArray(),
            '[' => Sequence(']'),
            '\'' or '"' => Str(),
            '-' or (>= '0' and <= '9') => Int(),
            _ => Word(),
        };
    }

    private Dictionary<string, object?> Dict()
    {
        _pos++;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (true)
        {
            SkipSpaces();
            if (Peek() == '}')
            {
                _pos++;
                return result;
            }
            if (Value() is not string key)
                throw Error("ключ словаря — не строка");
            Expect(':');
            result[key] = Value();
            if (!Separator('}'))
                return result;
        }
    }

    private List<object?> Sequence(char close)
    {
        _pos++;
        var result = new List<object?>();
        while (true)
        {
            SkipSpaces();
            if (Peek() == close)
            {
                _pos++;
                return result;
            }
            result.Add(Value());
            if (!Separator(close))
                return result;
        }
    }

    /// <summary>После элемента: запятая — продолжаем, закрывающая скобка — конец.</summary>
    private bool Separator(char close)
    {
        SkipSpaces();
        char c = Peek();
        _pos++;
        return c == ',' || (c == close ? false : throw Error($"ждали «,» или «{close}»"));
    }

    private string Str()
    {
        char quote = _text[_pos++];
        var result = new StringBuilder();
        while (_pos < _text.Length && _text[_pos] != quote)
        {
            if (_text[_pos] == '\\' && _pos + 1 < _text.Length)
                _pos++;
            result.Append(_text[_pos++]);
        }
        if (_pos >= _text.Length)
            throw Error("строка не закрыта");
        _pos++;
        return result.ToString();
    }

    private long Int()
    {
        int start = _pos;
        if (_text[_pos] == '-')
            _pos++;
        while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos]))
            _pos++;
        long value = long.TryParse(_text.AsSpan(start, _pos - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)
            ? n
            : throw Error("неверное целое");
        if (_pos < _text.Length && _text[_pos] is 'L' or 'l')
            _pos++; // Python 2 писал длинные целые как 3L
        return value;
    }

    private object? Word()
    {
        foreach ((string word, object? value) in new (string, object?)[] { ("True", true), ("False", false), ("None", null) })
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) == 0)
            {
                _pos += word.Length;
                return value;
            }
        throw Error("неизвестное значение");
    }

    private void Expect(char c)
    {
        SkipSpaces();
        if (Peek() != c)
            throw Error($"ждали «{c}»");
        _pos++;
    }

    private char Peek() => _pos < _text.Length ? _text[_pos] : throw Error("заголовок обрывается");

    private void SkipSpaces()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            _pos++;
    }

    private InvalidDataException Error(string what) =>
        new($"заголовок .npy, позиция {_pos}: {what} — {_text}");
}
