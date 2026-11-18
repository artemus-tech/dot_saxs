using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DotSaxs.Data;

/// <summary>Загрузка эталонов из каталога tests/references в базу (SPEC.md п. 8–9, tools/reference/README.md).</summary>
public static class ReferenceImporter
{
    /// <summary>
    /// Загружает набор эталонов: кейсы из cases.json, массивы — ссылкой на файл .npy и SHA-256 (сами массивы остаются
    /// в файлах). Тот же набор второй раз не загружается: если в базе уже есть набор с тем же хэшем содержимого,
    /// возвращается он.
    /// </summary>
    /// <param name="db">База.</param>
    /// <param name="root">Каталог эталонов, например tests/references.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public static async Task<ReferenceSet> ImportAsync(DotSaxsDb db, string root, CancellationToken cancellationToken = default)
    {
        byte[] environment = await File.ReadAllBytesAsync(Path.Combine(root, "environment.json"), cancellationToken);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["environment.json"] = Sha256(environment) };
        var cases = new List<StoredCase>();
        string[] folders = Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "cases.json")))
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (string folder in folders)
        {
            string python = Path.GetFileName(folder);
            byte[] json = await File.ReadAllBytesAsync(Path.Combine(folder, "cases.json"), cancellationToken);
            hashes[python + "/cases.json"] = Sha256(json);
            foreach (StoredCase stored in ParseCases(python, json))
            {
                foreach (StoredArray array in stored.Arrays)
                {
                    byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, array.File), cancellationToken);
                    array.Sha256 = Sha256(bytes);
                    array.Bytes = bytes.LongLength;
                    hashes[array.File] = array.Sha256;
                }
                cases.Add(stored);
            }
        }
        string contentHash = Sha256(Encoding.UTF8.GetBytes(string.Join("\n", hashes.Select(h => h.Key + " " + h.Value))));
        ReferenceSet? existing = await db.ReferenceSets.AsQueryable()
            .SingleOrDefaultAsync(s => s.ContentHash == contentHash, cancellationToken);
        if (existing is not null)
            return existing;
        using JsonDocument env = JsonDocument.Parse(environment);
        var set = new ReferenceSet
        {
            Numpy = env.RootElement.GetProperty("numpy").GetString()!,
            Scipy = env.RootElement.GetProperty("scipy").GetString()!,
            Python = env.RootElement.GetProperty("python").GetString()!,
            Environment = Encoding.UTF8.GetString(environment),
            ContentHash = contentHash,
            ImportedAt = DateTimeOffset.UtcNow,
            Cases = cases,
        };
        db.ReferenceSets.Add(set);
        await db.SaveChangesAsync(cancellationToken);
        return set;
    }

    /// <summary>Кейсы из cases.json; у массивов пока только место в кейсе и файл.</summary>
    private static List<StoredCase> ParseCases(string python, byte[] json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var result = new List<StoredCase>();
        foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            JsonElement? error = c.TryGetProperty("error", out JsonElement e) ? e : null;
            result.Add(new StoredCase
            {
                Python = python,
                Name = c.GetProperty("name").GetString()!,
                Match = c.GetProperty("match").GetRawText(),
                Call = c.GetRawText(),
                ErrorType = error?.GetProperty("type").GetString(),
                ErrorMessage = error?.GetProperty("message").GetString(),
                Arrays = ArraysOf(c).Select(a => new StoredArray { Place = a.Place, File = python + "/" + a.File }).ToList(),
            });
        }
        return result;
    }

    /// <summary>Файлы .npy кейса и их место: <c>args.x1</c>, <c>kwargs.axis</c>, <c>result</c>, <c>result.0</c>.</summary>
    private static IEnumerable<(string Place, string File)> ArraysOf(JsonElement c)
    {
        foreach (JsonElement a in c.GetProperty("args").EnumerateArray())
            foreach ((string Place, string File) x in ArraysIn(a.GetProperty("value"), "args." + a.GetProperty("name").GetString()))
                yield return x;
        foreach (JsonProperty k in c.GetProperty("kwargs").EnumerateObject())
            foreach ((string Place, string File) x in ArraysIn(k.Value, "kwargs." + k.Name))
                yield return x;
        if (c.TryGetProperty("result", out JsonElement result))
            foreach ((string Place, string File) x in ArraysIn(result, "result"))
                yield return x;
    }

    private static IEnumerable<(string Place, string File)> ArraysIn(JsonElement value, string place)
    {
        if (value.TryGetProperty("npy", out JsonElement npy))
        {
            yield return (place, npy.GetString()!);
            yield break;
        }
        foreach (string key in new[] { "tuple", "list" })
        {
            if (!value.TryGetProperty(key, out JsonElement items))
                continue;
            int i = 0;
            foreach (JsonElement item in items.EnumerateArray())
                foreach ((string Place, string File) x in ArraysIn(item, place + "." + i++))
                    yield return x;
        }
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
