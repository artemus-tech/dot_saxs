using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotSaxs.Data;

/// <summary>База PostgreSQL (SPEC.md п. 9): эталоны, история прогонов тестов, кэш результатов, очередь задач.</summary>
/// <param name="options">Настройки EF Core; обычно — <see cref="Create"/>.</param>
public sealed class DotSaxsDb(DbContextOptions<DotSaxsDb> options) : DbContext(options)
{
    /// <summary>Наборы эталонов.</summary>
    public DbSet<ReferenceSet> ReferenceSets => Set<ReferenceSet>();

    /// <summary>Кейсы эталонов.</summary>
    public DbSet<StoredCase> Cases => Set<StoredCase>();

    /// <summary>Массивы кейсов: ссылки на файлы .npy и хэши.</summary>
    public DbSet<StoredArray> Arrays => Set<StoredArray>();

    /// <summary>Прогоны тестов.</summary>
    public DbSet<TestRun> TestRuns => Set<TestRun>();

    /// <summary>Результаты тестов.</summary>
    public DbSet<TestResult> TestResults => Set<TestResult>();

    /// <summary>Кэш результатов.</summary>
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();

    /// <summary>Очередь задач для воркеров.</summary>
    public DbSet<QueueItem> QueueItems => Set<QueueItem>();

    /// <summary>Подключение к базе по строке Npgsql: <c>Host=…;Database=…;Username=…;Password=…</c>.</summary>
    public static DotSaxsDb Create(string connectionString) =>
        new(new DbContextOptionsBuilder<DotSaxsDb>().UseNpgsql(connectionString).Options);

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReferenceSet>(e =>
        {
            e.Property(x => x.Environment).HasColumnType("jsonb");
            e.HasIndex(x => x.ContentHash).IsUnique();
        });
        modelBuilder.Entity<StoredCase>(e =>
        {
            e.Property(x => x.Match).HasColumnType("jsonb");
            e.Property(x => x.Call).HasColumnType("jsonb");
            e.HasOne(x => x.Set).WithMany(s => s.Cases).HasForeignKey(x => x.SetId);
            e.HasIndex(x => new { x.SetId, x.Python, x.Name }).IsUnique();
        });
        modelBuilder.Entity<StoredArray>(e =>
        {
            e.HasOne(x => x.Case).WithMany(c => c.Arrays).HasForeignKey(x => x.CaseId);
            e.HasIndex(x => new { x.CaseId, x.Place }).IsUnique();
            e.HasIndex(x => x.Sha256);
        });
        modelBuilder.Entity<TestResult>(e =>
        {
            e.Property(x => x.Outcome).HasConversion<string>();
            e.HasOne(x => x.Run).WithMany(r => r.Results).HasForeignKey(x => x.RunId);
            e.HasOne(x => x.Case).WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CacheEntry>(e => e.HasKey(x => x.Key));
        modelBuilder.Entity<QueueItem>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.Status, x.Kind, x.Id });
        });
    }
}

/// <summary>Для <c>dotnet ef</c>: модель без подключения к базе (миграции создаёт CI, src/Data/README.md).</summary>
public sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<DotSaxsDb>
{
    /// <inheritdoc/>
    public DotSaxsDb CreateDbContext(string[] args) => DotSaxsDb.Create("Host=localhost;Database=dotsaxs");
}
