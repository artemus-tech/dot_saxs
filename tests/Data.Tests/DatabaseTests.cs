using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotSaxs.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotSaxs.Data.Tests;

/// <summary>База PostgreSQL (SPEC.md п. 9): схема по миграциям, эталоны, очередь, кэш, история прогонов.</summary>
public sealed class DatabaseTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrationsMatchTheModel()
    {
        await using DotSaxsDb db = fixture.Open();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync(Ct));
        Assert.False(db.Database.HasPendingModelChanges(), "модель изменилась без миграции");
    }

    [Fact]
    public async Task ReferencesImportOnceWithHashes()
    {
        await using DotSaxsDb db = fixture.Open();
        ReferenceSet first = await ReferenceImporter.ImportAsync(db, References.Root, Ct);
        ReferenceSet again = await ReferenceImporter.ImportAsync(db, References.Root, Ct);
        Assert.Equal(first.Id, again.Id);

        int expectedCases = References.Functions().Sum(f => References.Load(f).Count);
        Assert.Equal(expectedCases, await db.Cases.AsQueryable().CountAsync(c => c.SetId == first.Id, Ct));
        StoredArray[] arrays = await db.Arrays.AsQueryable().Where(a => a.Case.SetId == first.Id).ToArrayAsync(Ct);
        Assert.NotEmpty(arrays);
        foreach (StoredArray a in arrays)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(References.Root, a.File), Ct);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), a.Sha256);
            Assert.Equal(bytes.LongLength, a.Bytes);
        }

        StoredCase singular = await db.Cases.AsQueryable().Include(c => c.Arrays)
            .SingleAsync(c => c.SetId == first.Id && c.Python == "numpy.linalg.solve" && c.Name == "singular", Ct);
        Assert.Equal(("LinAlgError", "Singular matrix"), (singular.ErrorType, singular.ErrorMessage));
        Assert.Equal(new[] { "args.a", "args.b" }, singular.Arrays.Select(a => a.Place).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(References.ReadEnvironment().Numpy, first.Numpy);
    }

    [Fact]
    public async Task QueueGivesEachTaskToExactlyOneWorker()
    {
        string kind = "test-" + Guid.NewGuid().ToString("N");
        await using (DotSaxsDb db = fixture.Open())
            for (int i = 0; i < 40; i++)
                await TaskQueue.EnqueueAsync(db, kind, $$"""{"n": {{i}}}""", Ct);

        var taken = new ConcurrentBag<long>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            await using DotSaxsDb db = fixture.Open();
            while (await TaskQueue.TryTakeAsync(db, kind, $"worker-{w}", Ct) is { } item)
            {
                taken.Add(item.Id);
                await TaskQueue.FinishAsync(db, item.Id, error: null, Ct);
            }
        }, Ct)));

        Assert.Equal(40, taken.Count);
        Assert.Equal(40, taken.Distinct().Count());
        await using DotSaxsDb check = fixture.Open();
        Assert.True(await check.QueueItems.AsQueryable().Where(q => q.Kind == kind)
            .AllAsync(q => q.Status == QueueStatus.Done && q.Attempts == 1 && q.Worker != null, Ct));
    }

    [Fact]
    public async Task CacheFindsStoredResultInMemoryAndInDatabase()
    {
        string key = ResultCache.Key("scipy.integrate.quad", "1.18.1", new string('a', 64), """{"a": 0, "b": 1}""");
        Assert.NotEqual(key, ResultCache.Key("scipy.integrate.quad", "1.18.1", new string('a', 64), """{"a": 0, "b": 2}"""));
        var entry = new CacheEntry
        {
            Key = key,
            Function = "scipy.integrate.quad",
            Version = "1.18.1",
            DatasetHash = new string('a', 64),
            ParametersHash = key.Split('/').Last(),
            ResultFile = "cache/quad.npy",
            ResultSha256 = new string('b', 64),
            ComputeMilliseconds = 250,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await new ResultCache(fixture.Open).StoreAsync(entry, Ct);

        CacheEntry? fromDatabase = await new ResultCache(fixture.Open).FindAsync(key, Ct);
        Assert.Equal("cache/quad.npy", fromDatabase?.ResultFile);
        Assert.Null(await new ResultCache(fixture.Open).FindAsync(key + "-other", Ct));
        Assert.True(ResultCache.Worth(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(3)));
        Assert.False(ResultCache.Worth(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(3)));
    }

    [Fact]
    public async Task TestRunHistoryIsKept()
    {
        long runId;
        await using (DotSaxsDb db = fixture.Open())
        {
            var run = new TestRun { StartedAt = DateTimeOffset.UtcNow, Commit = "abc1234", Machine = "x86-64" };
            run.Results.Add(new TestResult { Test = "Sum.TenTenths", Outcome = TestOutcome.Passed, Level = "побитово", Milliseconds = 1.5 });
            run.Results.Add(new TestResult { Test = "Exp.Grid", Outcome = TestOutcome.Failed, Level = "до 1 ULP", Message = "2 ULP" });
            db.TestRuns.Add(run);
            await db.SaveChangesAsync(Ct);
            runId = run.Id;
        }
        await using DotSaxsDb check = fixture.Open();
        TestRun stored = await check.TestRuns.AsQueryable().Include(r => r.Results).SingleAsync(r => r.Id == runId, Ct);
        Assert.Equal(2, stored.Results.Count);
        Assert.Equal(TestOutcome.Failed, Assert.Single(stored.Results, r => r.Test == "Exp.Grid").Outcome);
    }
}
