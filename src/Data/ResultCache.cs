using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DotSaxs.Data;

/// <summary>
/// Кэш результатов (SPEC.md п. 9): сначала память процесса, потом PostgreSQL (общий для воркеров). Сам результат
/// лежит в файле, в кэше — ссылка и хэш. Кладём в кэш, только если расчёт дольше чтения из кэша.
/// </summary>
/// <param name="database">Как открыть базу: новый <see cref="DotSaxsDb"/> на каждый запрос.</param>
public sealed class ResultCache(Func<DotSaxsDb> database)
{
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new(StringComparer.Ordinal);

    /// <summary>Ключ: функция, версия, хэш входных данных (считается один раз при загрузке) и SHA-256 параметров.</summary>
    /// <param name="function">Функция, например <c>scipy.integrate.quad</c>.</param>
    /// <param name="version">Версия библиотеки.</param>
    /// <param name="datasetHash">SHA-256 входных данных.</param>
    /// <param name="parameters">Параметры вызова в каноническом JSON.</param>
    public static string Key(string function, string version, string datasetHash, string parameters) =>
        $"{function}@{version}/{datasetHash}/{Sha256(parameters)}";

    /// <summary>Класть ли в кэш: только если расчёт дольше, чем чтение из кэша (порог — по замеру чтения).</summary>
    public static bool Worth(TimeSpan compute, TimeSpan read) => compute > read;

    /// <summary>Результат по ключу: из памяти, иначе из базы (и запоминается в памяти); <c>null</c> — нет.</summary>
    /// <param name="key">Ключ <see cref="Key"/>.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task<CacheEntry?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_memory.TryGetValue(key, out CacheEntry? hit))
            return hit;
        await using DotSaxsDb db = database();
        CacheEntry? found = await db.CacheEntries.AsQueryable().AsNoTracking()
            .SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        if (found is not null)
            _memory[key] = found;
        return found;
    }

    /// <summary>Кладёт результат в базу и в память; тот же ключ — запись заменяется.</summary>
    /// <param name="entry">Запись кэша.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task StoreAsync(CacheEntry entry, CancellationToken cancellationToken = default)
    {
        await using DotSaxsDb db = database();
        CacheEntry? old = await db.CacheEntries.AsQueryable().SingleOrDefaultAsync(e => e.Key == entry.Key, cancellationToken);
        if (old is null)
            db.CacheEntries.Add(entry);
        else
            db.Entry(old).CurrentValues.SetValues(entry);
        await db.SaveChangesAsync(cancellationToken);
        _memory[entry.Key] = entry;
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
