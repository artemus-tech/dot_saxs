using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DotSaxs.Core;

/// <summary>Заголовок файла .npy: вид чисел, порядок, размеры, версия формата.</summary>
internal sealed record NpyHeader(string Descr, bool FortranOrder, int[] Shape, int Major, int Minor)
{
    /// <summary>Сколько чисел в массиве.</summary>
    internal long Count
    {
        get
        {
            long count = 1;
            foreach (int length in Shape)
                count *= length;
            return count;
        }
    }

    /// <summary>Размеры, как их пишет Python: <c>(3,)</c>, <c>(2, 3)</c>, <c>()</c>.</summary>
    internal string ShapeText => NpyFormat.TupleText(Shape);
}

/// <summary>
/// Формат .npy — перенос numpy/lib/_format_impl.py: магическая строка, версия, длина заголовка, заголовок — словарь
/// Python (<c>{'descr': '&lt;f8', 'fortran_order': False, 'shape': (3,), }</c>), выравнивание на 64 байта, потом числа.
/// Файл, записанный здесь, совпадает с файлом <c>np.save</c> байт в байт.
/// </summary>
internal static class NpyFormat
{
    // ref: numpy/lib/_format_impl.py#L178-L183 — константы формата
    internal static readonly byte[] MagicPrefix = [0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y'];
    private const int MagicLength = 8;
    private const int ArrayAlign = 64;
    private const int GrowthAxisMaxDigits = 21;

    // ref: numpy/lib/_format_impl.py#L196 — заголовок длиннее — небезопасно разбирать
    private const int MaxHeaderSize = 10000;

    /// <summary>Вид чисел NumPy без порядка байт → тип .NET.</summary>
    private static readonly Dictionary<string, Type> Kinds = new()
    {
        ["f8"] = typeof(double),
        ["f4"] = typeof(float),
        ["f2"] = typeof(Half),
        ["i8"] = typeof(long),
        ["i4"] = typeof(int),
        ["i2"] = typeof(short),
        ["i1"] = typeof(sbyte),
        ["u8"] = typeof(ulong),
        ["u4"] = typeof(uint),
        ["u2"] = typeof(ushort),
        ["u1"] = typeof(byte),
        ["b1"] = typeof(bool),
        ["c16"] = typeof(Complex),
    };

    /// <summary>Вид чисел в заголовке по типу .NET: <c>&lt;f8</c>, <c>|b1</c>… — как <c>dtype.str</c> у NumPy.</summary>
    internal static string DescrOf(Type type)
    {
        foreach ((string kind, Type t) in Kinds)
        {
            if (t == type)
                return (kind is "i1" or "u1" or "b1" ? "|" : "<") + kind;
        }
        throw new NotSupportedException(Messages.FileKindNotSupported(type.Name));
    }

    /// <summary>Тип .NET по виду чисел из заголовка или <c>null</c>, если такой вид не поддерживается.</summary>
    internal static Type? TypeOf(string descr) => descr.Length > 1 && Kinds.TryGetValue(descr[1..], out Type? type) ? type : null;

    /// <summary>Прочитать магическую строку и заголовок; поток остаётся на первом байте чисел.</summary>
    internal static NpyHeader ReadHeader(Stream stream)
    {
        // ref: numpy/lib/_format_impl.py#L230-L248 — read_magic
        byte[] magic = ReadBytes(stream, MagicLength, "magic string");
        if (!magic.AsSpan(0, MagicPrefix.Length).SequenceEqual(MagicPrefix))
        {
            throw new ValueError(
                $"the magic string is not correct; expected {BytesText(MagicPrefix)}, got {BytesText(magic.AsSpan(0, MagicLength - 2))}");
        }
        int major = magic[6], minor = magic[7];
        // ref: numpy/lib/_format_impl.py#L199-L202 — версии 1.0, 2.0, 3.0
        if (!(minor == 0 && major is 1 or 2 or 3))
        {
            throw new ValueError(string.Create(CultureInfo.InvariantCulture,
                $"we only support format version (1,0), (2,0), and (3,0), not ({major}, {minor})"));
        }
        // ref: numpy/lib/_format_impl.py#L621-L646 — длина заголовка: 2 байта в версии 1.0, 4 — в 2.0 и 3.0
        int lengthSize = major == 1 ? 2 : 4;
        byte[] lengthBytes = ReadBytes(stream, lengthSize, "array header length");
        long headerLength = lengthSize == 2 ? BitConverter.ToUInt16(lengthBytes) : BitConverter.ToUInt32(lengthBytes);
        byte[] headerBytes = ReadBytes(stream, headerLength, "array header");
        string header = major == 3 ? Encoding.UTF8.GetString(headerBytes) : Encoding.Latin1.GetString(headerBytes);
        if (header.Length > MaxHeaderSize)
        {
            throw new ValueError(string.Create(CultureInfo.InvariantCulture,
                $"Header info length ({header.Length}) is large and may not be safe to load securely.\n") +
                "To allow loading, adjust `max_header_size` or fully trust the `.npy` file using `allow_pickle=True`.\n" +
                "For safety against large resource use or crashes, sandboxing may be necessary.");
        }
        return ParseHeader(header, major, minor);
    }

    /// <summary>Разбор словаря заголовка: ключи и значения — так, как их пишет NumPy (и Python 2 с <c>3L</c>).</summary>
    private static NpyHeader ParseHeader(string header, int major, int minor)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        int p = 0;
        try
        {
            SkipSpaces(header, ref p);
            Expect(header, ref p, '{');
            while (true)
            {
                SkipSpaces(header, ref p);
                if (header[p] == '}')
                {
                    p++;
                    break;
                }
                string key = ParseString(header, ref p);
                SkipSpaces(header, ref p);
                Expect(header, ref p, ':');
                SkipSpaces(header, ref p);
                values[key] = ParseValue(header, ref p);
                SkipSpaces(header, ref p);
                if (header[p] == ',')
                    p++;
            }
            SkipSpaces(header, ref p);
            if (p != header.Length)
                throw new FormatException();
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException)
        {
            // ref: numpy/lib/_format_impl.py#L664-L675 — заголовок не разобрать
            throw new ValueError($"Cannot parse header: {StringText(header)}");
        }

        // ref: numpy/lib/_format_impl.py#L680-L697 — ключи и значения
        if (values.Count != 3 || !values.ContainsKey("descr") || !values.ContainsKey("fortran_order") || !values.ContainsKey("shape"))
        {
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            throw new ValueError($"Header does not contain the correct keys: [{string.Join(", ", keys.ConvertAll(StringText))}]");
        }
        if (values["shape"] is not int[] shape)
            throw new ValueError($"shape is not valid: {ValueText(values["shape"])}");
        if (values["fortran_order"] is not bool fortran)
            throw new ValueError($"fortran_order is not a valid bool: {ValueText(values["fortran_order"])}");
        if (values["descr"] is not string descr)
            throw new NotSupportedException(Messages.FileKindNotSupported(ValueText(values["descr"])));
        foreach (int length in shape)
        {
            if (length < 0)
                throw new ValueError("negative dimensions are not allowed");
        }
        return new NpyHeader(descr, fortran, shape, major, minor);
    }

    /// <summary>
    /// Прочитать числа после заголовка: <paramref name="header"/>.Count чисел вида <typeparamref name="T"/> (он должен
    /// совпадать с видом в файле); порядок байт — как в файле.
    /// </summary>
    internal static T[] ReadData<T>(Stream stream, NpyHeader header)
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        T[] data = Memory.Allocate<T>(header.Count);
        Span<byte> bytes = MemoryMarshal.AsBytes(data.AsSpan());
        int read = 0;
        while (read < bytes.Length)
        {
            int n = stream.Read(bytes[read..]);
            if (n == 0)
                break;
            read += n;
        }
        // ref: numpy/lib/_format_impl.py#L876-L882 — файл кончился раньше чисел
        if (read < bytes.Length)
        {
            throw new ValueError(string.Create(CultureInfo.InvariantCulture,
                $"Failed to read all data for array. Expected {header.ShapeText} = {header.Count} elements, could only read {read / size} elements. (file seems not fully written?)"));
        }
        bool bigEndian = header.Descr[0] == '>' || (header.Descr[0] == '=' && !BitConverter.IsLittleEndian);
        if (bigEndian == BitConverter.IsLittleEndian && size > 1)
        {
            // Числа другого порядка байт: у комплексных переворачивается каждая из двух частей.
            int part = typeof(T) == typeof(Complex) ? 8 : size;
            for (int i = 0; i < bytes.Length; i += part)
                bytes.Slice(i, part).Reverse();
        }
        return data;
    }

    /// <summary>
    /// Записать массив: заголовок, как у <c>np.save</c> (версия 1.0, если заголовок помещается, иначе 2.0), и числа
    /// <paramref name="data"/> в порядке C.
    /// </summary>
    internal static void Write<T>(Stream stream, int[] shape, T[] data)
        where T : unmanaged
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("big-endian");
        // ref: numpy/lib/_format_impl.py#L460-L470 — словарь по ключам в алфавитном порядке, repr значений, запас пробелов
        // для роста первого размера
        var header = new StringBuilder();
        header.Append("{'descr': '").Append(DescrOf(typeof(T))).Append("', 'fortran_order': False, 'shape': ")
            .Append(TupleText(shape)).Append(", }");
        if (shape.Length > 0)
            header.Append(' ', GrowthAxisMaxDigits - shape[0].ToString(CultureInfo.InvariantCulture).Length);
        byte[] text = Encoding.Latin1.GetBytes(header.ToString());

        // ref: numpy/lib/_format_impl.py#L397-L418 — длина, выравнивание на 64 байта пробелами и перевод строки
        int lengthSize = 2, major = 1;
        int length = text.Length + 1;
        int padding = ArrayAlign - ((MagicLength + lengthSize + length) % ArrayAlign);
        // ref: numpy/lib/_format_impl.py#L421-L432 — не помещается в 2 байта — версия 2.0
        if (length + padding > ushort.MaxValue)
        {
            lengthSize = 4;
            major = 2;
            padding = ArrayAlign - ((MagicLength + lengthSize + length) % ArrayAlign);
        }
        stream.Write(MagicPrefix);
        stream.Write([(byte)major, 0]);
        if (lengthSize == 2)
            stream.Write(BitConverter.GetBytes((ushort)(length + padding)));
        else
            stream.Write(BitConverter.GetBytes((uint)(length + padding)));
        stream.Write(text);
        for (int i = 0; i < padding; i++)
            stream.WriteByte((byte)' ');
        stream.WriteByte((byte)'\n');
        stream.Write(MemoryMarshal.AsBytes(data.AsSpan()));
    }

    /// <summary>Кортеж целых, как его пишет Python: <c>(3,)</c>, <c>(2, 3)</c>, <c>()</c>.</summary>
    internal static string TupleText(IReadOnlyList<int> values) => values.Count switch
    {
        0 => "()",
        1 => string.Create(CultureInfo.InvariantCulture, $"({values[0]},)"),
        _ => "(" + string.Join(", ", ToStrings(values)) + ")",
    };

    private static IEnumerable<string> ToStrings(IReadOnlyList<int> values)
    {
        foreach (int value in values)
            yield return value.ToString(CultureInfo.InvariantCulture);
    }

    // ref: numpy/lib/_format_impl.py#L1000-L1024 — _read_bytes: файл кончился
    private static byte[] ReadBytes(Stream stream, long size, string what)
    {
        // Длину заголовка в файле может испортить кто угодно: больше, чем осталось в файле, не выделяем.
        if (stream.CanSeek && size > stream.Length - stream.Position)
        {
            long rest = Math.Max(stream.Length - stream.Position, 0);
            throw new ValueError(string.Create(CultureInfo.InvariantCulture, $"EOF: reading {what}, expected {size} bytes got {rest}"));
        }
        var buffer = new byte[size];
        int read = 0;
        while (read < size)
        {
            int n = stream.Read(buffer, read, (int)(size - read));
            if (n == 0)
                break;
            read += n;
        }
        if (read < size)
            throw new ValueError(string.Create(CultureInfo.InvariantCulture, $"EOF: reading {what}, expected {size} bytes got {read}"));
        return buffer;
    }

    private static void SkipSpaces(string s, ref int p)
    {
        while (p < s.Length && char.IsWhiteSpace(s[p]))
            p++;
    }

    private static void Expect(string s, ref int p, char c)
    {
        if (s[p] != c)
            throw new FormatException();
        p++;
    }

    private static string ParseString(string s, ref int p)
    {
        char quote = s[p];
        if (quote is not ('\'' or '"'))
            throw new FormatException();
        int end = s.IndexOf(quote, p + 1);
        if (end < 0)
            throw new FormatException();
        string value = s[(p + 1)..end];
        p = end + 1;
        return value;
    }

    private static object ParseValue(string s, ref int p)
    {
        if (s[p] is '\'' or '"')
            return ParseString(s, ref p);
        if (string.CompareOrdinal(s, p, "True", 0, 4) == 0)
        {
            p += 4;
            return true;
        }
        if (string.CompareOrdinal(s, p, "False", 0, 5) == 0)
        {
            p += 5;
            return false;
        }
        if (s[p] == '(')
        {
            p++;
            var items = new List<int>();
            while (true)
            {
                SkipSpaces(s, ref p);
                if (s[p] == ')')
                {
                    p++;
                    return items.ToArray();
                }
                items.Add(checked((int)ParseInteger(s, ref p)));
                SkipSpaces(s, ref p);
                if (s[p] == ',')
                    p++;
                else if (s[p] != ')')
                    throw new FormatException();
            }
        }
        if (s[p] == '[')
        {
            // Список — описание полей структурированного массива: возвращается текстом, его разбирает вызывающий.
            int depth = 0, start = p;
            for (; p < s.Length; p++)
            {
                if (s[p] == '[')
                    depth++;
                else if (s[p] == ']' && --depth == 0)
                {
                    p++;
                    return new StructuredDescr(s[start..p]);
                }
            }
            throw new FormatException();
        }
        return ParseInteger(s, ref p);
    }

    private static long ParseInteger(string s, ref int p)
    {
        int start = p;
        if (s[p] == '-')
            p++;
        while (p < s.Length && char.IsAsciiDigit(s[p]))
            p++;
        if (p == start || (p == start + 1 && s[start] == '-'))
            throw new FormatException();
        long value = long.Parse(s.AsSpan(start, p - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        // ref: numpy/lib/_format_impl.py#L586-L618 — у целых из Python 2 бывает суффикс L
        if (p < s.Length && s[p] == 'L')
            p++;
        return value;
    }

    /// <summary>Байты, как их пишет Python: <c>b'\x93NUMPY'</c>.</summary>
    private static string BytesText(ReadOnlySpan<byte> bytes)
    {
        bool hasSingle = bytes.Contains((byte)'\''), hasDouble = bytes.Contains((byte)'"');
        char quote = hasSingle && !hasDouble ? '"' : '\'';
        var text = new StringBuilder("b").Append(quote);
        foreach (byte b in bytes)
        {
            text.Append(b switch
            {
                (byte)'\\' => "\\\\",
                (byte)'\n' => "\\n",
                (byte)'\r' => "\\r",
                (byte)'\t' => "\\t",
                _ when b == quote => "\\" + quote,
                >= 0x20 and < 0x7f => ((char)b).ToString(),
                _ => string.Create(CultureInfo.InvariantCulture, $"\\x{b:x2}"),
            });
        }
        return text.Append(quote).ToString();
    }

    /// <summary>Строка, как её пишет Python (<c>repr</c>): в одинарных кавычках или в двойных, если внутри одинарные.</summary>
    internal static string StringText(string s)
    {
        char quote = s.Contains('\'', StringComparison.Ordinal) && !s.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var text = new StringBuilder().Append(quote);
        foreach (char c in s)
        {
            text.Append(c switch
            {
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when c == quote => "\\" + quote,
                < ' ' or '\x7f' => string.Create(CultureInfo.InvariantCulture, $"\\x{(int)c:x2}"),
                _ => c.ToString(),
            });
        }
        return text.Append(quote).ToString();
    }

    private static string ValueText(object value) => value switch
    {
        string s => StringText(s),
        bool b => b ? "True" : "False",
        int[] tuple => TupleText(tuple),
        long n => n.ToString(CultureInfo.InvariantCulture),
        StructuredDescr d => d.Text,
        _ => value.ToString() ?? "",
    };
}

/// <summary>Описание полей структурированного массива из заголовка .npy — пока текстом.</summary>
internal sealed record StructuredDescr(string Text);
