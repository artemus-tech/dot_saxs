using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DotSaxs.Data;

/// <summary>
/// Очередь задач для воркеров в PostgreSQL (SPEC.md п. 9). Воркер берёт самую старую задачу через
/// <c>FOR UPDATE SKIP LOCKED</c>: два воркера никогда не получают одну задачу и не ждут друг друга.
/// </summary>
public static class TaskQueue
{
    /// <summary>Ставит задачу в очередь.</summary>
    /// <param name="db">База.</param>
    /// <param name="kind">Вид задачи, например <c>portal-run</c>.</param>
    /// <param name="payload">Параметры задачи в JSON.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public static async Task<QueueItem> EnqueueAsync(DotSaxsDb db, string kind, string payload, CancellationToken cancellationToken = default)
    {
        var item = new QueueItem { Kind = kind, Payload = payload, Status = QueueStatus.Queued, CreatedAt = DateTimeOffset.UtcNow };
        db.QueueItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    /// <summary>
    /// Берёт самую старую ждущую задачу этого вида и помечает «считается» — одним запросом. <c>null</c> — задач нет
    /// (или все заняты другими воркерами).
    /// </summary>
    /// <param name="db">База.</param>
    /// <param name="kind">Вид задачи.</param>
    /// <param name="worker">Имя воркера — пишется в задачу.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public static async Task<QueueItem?> TryTakeAsync(DotSaxsDb db, string kind, string worker, CancellationToken cancellationToken = default)
    {
        string queued = nameof(QueueStatus.Queued), running = nameof(QueueStatus.Running);
        List<QueueItem> taken = await db.QueueItems.FromSql($"""
            UPDATE "QueueItems"
            SET "Status" = {running}, "StartedAt" = now(), "Worker" = {worker}, "Attempts" = "Attempts" + 1
            WHERE "Id" = (
                SELECT "Id" FROM "QueueItems"
                WHERE "Status" = {queued} AND "Kind" = {kind}
                ORDER BY "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(cancellationToken);
        return taken.SingleOrDefault();
    }

    /// <summary>Отмечает задачу законченной: без ошибки — «готово», с ошибкой — «упала».</summary>
    /// <param name="db">База.</param>
    /// <param name="id">Номер задачи.</param>
    /// <param name="error">Текст ошибки или <c>null</c>.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public static async Task FinishAsync(DotSaxsDb db, long id, string? error = null, CancellationToken cancellationToken = default)
    {
        QueueItem item = await db.QueueItems.AsQueryable().SingleAsync(q => q.Id == id, cancellationToken);
        item.Status = error is null ? QueueStatus.Done : QueueStatus.Failed;
        item.FinishedAt = DateTimeOffset.UtcNow;
        item.Error = error;
        await db.SaveChangesAsync(cancellationToken);
    }
}
