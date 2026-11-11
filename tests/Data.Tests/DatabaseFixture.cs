using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DotSaxs.Data.Tests;

/// <summary>
/// Своя пустая база на класс тестов: создаётся миграциями и удаляется в конце. Строка подключения к серверу —
/// в DOTSAXS_DB; без неё тесты пропускаются, а в CI падают (там база обязана быть).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private string? _connection;

    /// <summary>Новое подключение к базе этого класса тестов.</summary>
    public DotSaxsDb Open()
    {
        if (_connection is not null)
            return DotSaxsDb.Create(_connection);
        if (Environment.GetEnvironmentVariable("CI") == "true")
            throw new InvalidOperationException("В CI нужна база: строка подключения в переменной DOTSAXS_DB");
        Assert.Skip("Нет базы: строка подключения к PostgreSQL — в переменной DOTSAXS_DB");
        throw new UnreachableException("Assert.Skip не возвращается");
    }

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        string? server = Environment.GetEnvironmentVariable("DOTSAXS_DB");
        if (string.IsNullOrEmpty(server))
            return;
        _connection = new NpgsqlConnectionStringBuilder(server) { Database = "dotsaxs_test_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using DotSaxsDb db = DotSaxsDb.Create(_connection);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
            return;
        await using DotSaxsDb db = DotSaxsDb.Create(_connection);
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }
}
