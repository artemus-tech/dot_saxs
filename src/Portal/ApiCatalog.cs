using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotSaxs.Portal;

/// <summary>Член нашего API, который переносит функцию: объявление на C# и описание из XML-документации.</summary>
internal sealed record ApiMember(string Declaration, string Summary);

/// <summary>Перенесённая функция NumPy или SciPy: как она делается у нас (names.csv), оригинал ([Original]) и члены API.</summary>
internal sealed record ApiFunction(
    string Python, string How, string Call, string Note, string SourceFile, int SourceLine, string SourceUrl,
    IReadOnlyList<ApiMember> Members);

/// <summary>
/// Всё, что показывает портал. Собирается при запуске из сборки DotSaxs (атрибуты [Original]), её XML-документации,
/// names.csv и sources.json — они лежат рядом с программой.
/// </summary>
internal sealed partial class ApiCatalog(IReadOnlyList<ApiFunction> functions)
{
    /// <summary>Перенесённые функции по имени Python.</summary>
    public IReadOnlyList<ApiFunction> Functions { get; } = functions;

    /// <summary>Функция по имени Python или <c>null</c>.</summary>
    public ApiFunction? Find(string python) => Functions.FirstOrDefault(f => f.Python == python);

    /// <summary>Каталог из файлов в <paramref name="directory"/>.</summary>
    public static ApiCatalog Load(string directory)
    {
        Assembly assembly = typeof(Vector<>).Assembly;
        Dictionary<string, Dictionary<string, string>> names = ReadNames(Path.Combine(directory, "names.csv"));
        Dictionary<string, string> tags = ReadTags(Path.Combine(directory, "sources.json"));
        Dictionary<string, string> docs = ReadDocs(Path.Combine(directory, assembly.GetName().Name + ".xml"));
        var members = new List<(OriginalAttribute Original, ApiMember Member)>();
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (Type type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (MethodInfo method in type.GetMethods(declared).OrderBy(m => m.MetadataToken))
            {
                if (method.GetCustomAttribute<OriginalAttribute>() is { } original)
                    members.Add((original, new ApiMember(Declaration(method), docs.GetValueOrDefault(DocId(method), ""))));
            }
        }
        List<ApiFunction> functions = members
            .GroupBy(m => m.Original.Name, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                OriginalAttribute original = g.First().Original;
                Dictionary<string, string> row = names.GetValueOrDefault(g.Key) ?? new Dictionary<string, string>();
                string repo = g.Key.Split('.')[0];
                string url = $"https://github.com/{repo}/{repo}/blob/{tags.GetValueOrDefault(repo, "main")}/{original.File}#L{original.Line}";
                return new ApiFunction(
                    g.Key, row.GetValueOrDefault("how", ""), row.GetValueOrDefault("call", ""), row.GetValueOrDefault("note", ""),
                    original.File, original.Line, url, g.Select(m => m.Member).ToList());
            })
            .ToList();
        return new ApiCatalog(functions);
    }

    /// <summary>Строки names.csv: имя Python → столбец → значение.</summary>
    private static Dictionary<string, Dictionary<string, string>> ReadNames(string path)
    {
        List<string[]> rows = Csv(File.ReadAllText(path));
        string[] header = rows[0];
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string[] row in rows.Skip(1))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length && i < row.Length; i++)
                fields[header[i]] = row[i];
            result[row[0]] = fields;
        }
        return result;
    }

    /// <summary>Тег закреплённой версии каждого оригинала из sources.json: numpy → v2.5.4.</summary>
    private static Dictionary<string, string> ReadTags(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty source in doc.RootElement.EnumerateObject())
        {
            if (source.Value.TryGetProperty("tag", out JsonElement tag) && tag.ValueKind == JsonValueKind.String)
                tags[source.Name] = tag.GetString()!;
        }
        return tags;
    }

    /// <summary>Описания из XML-документации: id члена (<c>M:DotSaxs.Sums.Sum(DotSaxs.Vector{System.Double})</c>) → текст.</summary>
    private static Dictionary<string, string> ReadDocs(string path)
    {
        var docs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return docs;
        XElement? members = XDocument.Load(path).Root?.Element("members");
        foreach (XElement member in members?.Elements("member") ?? Enumerable.Empty<XElement>())
        {
            string? id = member.Attribute("name")?.Value;
            XElement? summary = member.Element("summary");
            if (id is not null && summary is not null)
                docs[id] = Spaces().Replace(Text(summary), " ").Trim();
        }
        return docs;
    }

    /// <summary>Текст описания: &lt;c&gt; — как есть, &lt;see cref&gt; — последнее имя ссылки.</summary>
    private static string Text(XElement element)
    {
        var text = new StringBuilder();
        foreach (XNode node in element.Nodes())
        {
            if (node is XText plain)
                text.Append(plain.Value);
            else if (node is XElement { Name.LocalName: "see" } see && see.Attribute("cref") is { } cref)
                text.Append(ShortCref(cref.Value));
            else if (node is XElement { Name.LocalName: "paramref" or "typeparamref" } reference)
                text.Append(reference.Attribute("name")?.Value);
            else if (node is XElement inner)
                text.Append(Text(inner));
        }
        return text.ToString();
    }

    /// <summary><c>T:DotSaxs.FloatingPointError</c> → FloatingPointError, <c>M:DotSaxs.Np.ErrState(…)</c> → ErrState.</summary>
    private static string ShortCref(string cref)
    {
        string name = cref.Contains(':', StringComparison.Ordinal) ? cref[(cref.IndexOf(':', StringComparison.Ordinal) + 1)..] : cref;
        int parenthesis = name.IndexOf('(', StringComparison.Ordinal);
        if (parenthesis >= 0)
            name = name[..parenthesis];
        name = name[(name.LastIndexOf('.') + 1)..];
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 ? name[..tick] : name;
    }

    /// <summary>Объявление на C#: <c>double Sum(this Vector&lt;double&gt; v)</c>.</summary>
    private static string Declaration(MethodInfo method)
    {
        bool extension = method.IsDefined(typeof(ExtensionAttribute), inherit: false);
        var nullability = new NullabilityInfoContext();
        IEnumerable<string> parameters = method.GetParameters().Select((p, i) =>
        {
            string type = CSharp(p.ParameterType);
            if (!p.ParameterType.IsValueType && nullability.Create(p).ReadState == NullabilityState.Nullable)
                type += "?";
            string text = (i == 0 && extension ? "this " : "") + type + " " + p.Name;
            return p.HasDefaultValue ? text + " = " + Literal(p.DefaultValue) : text;
        });
        string owner = extension ? "" : method.DeclaringType!.Name + ".";
        return $"{CSharp(method.ReturnType)} {owner}{method.Name}({string.Join(", ", parameters)})";
    }

    /// <summary>Имя типа, как его пишут в C#: double, Vector&lt;double&gt;, IEnumerable&lt;Vector&lt;double&gt;&gt;.</summary>
    private static string CSharp(Type type)
    {
        if (type.IsArray)
            return CSharp(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(CSharp)) + ">";
        }
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Double => "double",
            TypeCode.Single => "float",
            TypeCode.Int64 => "long",
            TypeCode.Int32 => "int",
            TypeCode.Boolean => "bool",
            TypeCode.String => "string",
            _ when type == typeof(void) => "void",
            _ when type == typeof(object) => "object",
            _ => type.Name,
        };
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Id члена в XML-документации: <c>M:DotSaxs.Sums.Sum(DotSaxs.Vector{System.Double})</c>.</summary>
    private static string DocId(MethodInfo method)
    {
        string id = "M:" + method.DeclaringType!.FullName!.Replace('+', '.') + "." + method.Name;
        if (method.IsGenericMethod)
            id += "``" + method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture);
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 0 ? id : id + "(" + string.Join(",", parameters.Select(p => TypeId(p.ParameterType))) + ")";
    }

    private static string TypeId(Type type)
    {
        if (type.IsGenericParameter)
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition.ToString(CultureInfo.InvariantCulture);
        if (type.IsByRef)
            return TypeId(type.GetElementType()!) + "@";
        if (type.IsArray)
            return TypeId(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            string name = definition.FullName!.Replace('+', '.');
            name = name[..name.IndexOf('`', StringComparison.Ordinal)];
            return name + "{" + string.Join(",", type.GetGenericArguments().Select(TypeId)) + "}";
        }
        return type.FullName!.Replace('+', '.');
    }

    /// <summary>Разбор CSV (RFC 4180): поля в кавычках, удвоенные кавычки, переводы строк внутри кавычек.</summary>
    private static List<string[]> Csv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    field.Append(text[++i]);
                else
                    quoted = false;
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }
        return rows;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
