using System;
using System.Collections.Generic;

namespace DotSaxs.Data;

/// <summary>Набор эталонов: одна загрузка каталога tests/references (tools/reference/README.md).</summary>
public sealed class ReferenceSet
{
    /// <summary>Номер набора.</summary>
    public long Id { get; set; }

    /// <summary>Версия numpy, на которой посчитаны эталоны.</summary>
    public string Numpy { get; set; } = "";

    /// <summary>Версия scipy.</summary>
    public string Scipy { get; set; } = "";

    /// <summary>Версия Python.</summary>
    public string Python { get; set; } = "";

    /// <summary>environment.json целиком (jsonb): версии, SIMD numpy, OpenBLAS, параметры процессора.</summary>
    public string Environment { get; set; } = "{}";

    /// <summary>SHA-256 всех файлов набора: один и тот же набор второй раз не загружается.</summary>
    public string ContentHash { get; set; } = "";

    /// <summary>Когда загружен.</summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Кейсы набора.</summary>
    public List<StoredCase> Cases { get; set; } = [];
}

/// <summary>Кейс эталона в базе: один вызов функции NumPy или SciPy.</summary>
public sealed class StoredCase
{
    /// <summary>Номер кейса.</summary>
    public long Id { get; set; }

    /// <summary>Набор, к которому относится кейс.</summary>
    public long SetId { get; set; }

    /// <summary>Набор, к которому относится кейс.</summary>
    public ReferenceSet Set { get; set; } = null!;

    /// <summary>Имя функции в Python, например <c>numpy.linalg.solve</c>.</summary>
    public string Python { get; set; } = "";

    /// <summary>Имя кейса, например <c>singular</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Уровень совпадения из cases.json (jsonb): <c>{"kind": "ulp", "ulp": 1}</c>.</summary>
    public string Match { get; set; } = "{}";

    /// <summary>Кейс из cases.json целиком (jsonb): аргументы, результат или ошибка, предупреждения.</summary>
    public string Call { get; set; } = "{}";

    /// <summary>Тип исключения Python у негативного кейса, например <c>LinAlgError</c>.</summary>
    public string? ErrorType { get; set; }

    /// <summary>Текст исключения Python.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Массивы кейса — файлы .npy со ссылкой и хэшем.</summary>
    public List<StoredArray> Arrays { get; set; } = [];
}

/// <summary>Массив кейса: в базе — ссылка на файл .npy и его хэш, сам массив — в файле (SPEC.md п. 9).</summary>
public sealed class StoredArray
{
    /// <summary>Номер массива.</summary>
    public long Id { get; set; }

    /// <summary>Кейс, к которому относится массив.</summary>
    public long CaseId { get; set; }

    /// <summary>Кейс, к которому относится массив.</summary>
    public StoredCase Case { get; set; } = null!;

    /// <summary>Место в кейсе: <c>args.x1</c>, <c>kwargs.axis</c>, <c>result</c>, <c>result.0</c>.</summary>
    public string Place { get; set; } = "";

    /// <summary>Файл относительно каталога эталонов: <c>numpy.add/random.x1.npy</c>.</summary>
    public string File { get; set; } = "";

    /// <summary>SHA-256 файла, шестнадцатеричный.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Размер файла в байтах.</summary>
    public long Bytes { get; set; }
}

/// <summary>Прогон тестов: когда, на каком коммите и машине (история прогонов, SPEC.md п. 9).</summary>
public sealed class TestRun
{
    /// <summary>Номер прогона.</summary>
    public long Id { get; set; }

    /// <summary>Начало прогона.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Конец прогона; пусто, пока идёт.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Коммит, на котором шли тесты.</summary>
    public string Commit { get; set; } = "";

    /// <summary>Машина: процессор, ОС.</summary>
    public string Machine { get; set; } = "";

    /// <summary>Результаты тестов прогона.</summary>
    public List<TestResult> Results { get; set; } = [];
}

/// <summary>Чем закончился тест.</summary>
public enum TestOutcome
{
    /// <summary>Прошёл.</summary>
    Passed,

    /// <summary>Упал.</summary>
    Failed,

    /// <summary>Пропущен.</summary>
    Skipped,
}

/// <summary>Результат одного теста в прогоне.</summary>
public sealed class TestResult
{
    /// <summary>Номер результата.</summary>
    public long Id { get; set; }

    /// <summary>Прогон.</summary>
    public long RunId { get; set; }

    /// <summary>Прогон.</summary>
    public TestRun Run { get; set; } = null!;

    /// <summary>Полное имя теста.</summary>
    public string Test { get; set; } = "";

    /// <summary>Кейс эталона, если тест сверялся с ним.</summary>
    public long? CaseId { get; set; }

    /// <summary>Кейс эталона, если тест сверялся с ним.</summary>
    public StoredCase? Case { get; set; }

    /// <summary>Чем закончился тест.</summary>
    public TestOutcome Outcome { get; set; }

    /// <summary>Достигнутый уровень совпадения: «побитово», «до 1 ULP», «допуск rtol=…».</summary>
    public string Level { get; set; } = "";

    /// <summary>Текст ошибки у упавшего теста.</summary>
    public string? Message { get; set; }

    /// <summary>Время теста, мс.</summary>
    public double Milliseconds { get; set; }
}

/// <summary>Результат расчёта в кэше (SPEC.md п. 9): сам результат — в файле, в базе — ссылка и хэш.</summary>
public sealed class CacheEntry
{
    /// <summary>Ключ: функция, версия, хэш данных и хэш параметров (<see cref="ResultCache.Key"/>).</summary>
    public string Key { get; set; } = "";

    /// <summary>Функция, например <c>scipy.integrate.quad</c>.</summary>
    public string Function { get; set; } = "";

    /// <summary>Версия библиотеки, посчитавшей результат.</summary>
    public string Version { get; set; } = "";

    /// <summary>SHA-256 входных данных — считается один раз при загрузке данных.</summary>
    public string DatasetHash { get; set; } = "";

    /// <summary>SHA-256 параметров вызова.</summary>
    public string ParametersHash { get; set; } = "";

    /// <summary>Файл с результатом.</summary>
    public string ResultFile { get; set; } = "";

    /// <summary>SHA-256 файла с результатом.</summary>
    public string ResultSha256 { get; set; } = "";

    /// <summary>Сколько шёл расчёт, мс: кэшируем, только если расчёт дольше чтения из кэша.</summary>
    public double ComputeMilliseconds { get; set; }

    /// <summary>Когда положен в кэш.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Состояние задачи в очереди.</summary>
public enum QueueStatus
{
    /// <summary>Ждёт воркера.</summary>
    Queued,

    /// <summary>Воркер взял и считает.</summary>
    Running,

    /// <summary>Готово.</summary>
    Done,

    /// <summary>Упала с ошибкой.</summary>
    Failed,
}

/// <summary>Задача для воркеров (SPEC.md п. 9): воркер берёт её через FOR UPDATE SKIP LOCKED (<see cref="TaskQueue"/>).</summary>
public sealed class QueueItem
{
    /// <summary>Номер задачи; берутся по порядку номеров.</summary>
    public long Id { get; set; }

    /// <summary>Вид задачи, например <c>reference</c> или <c>portal-run</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Параметры задачи (jsonb).</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Состояние.</summary>
    public QueueStatus Status { get; set; }

    /// <summary>Когда поставлена.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Когда воркер взял задачу.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Когда закончена.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Воркер, который взял задачу.</summary>
    public string? Worker { get; set; }

    /// <summary>Сколько раз задачу брали.</summary>
    public int Attempts { get; set; }

    /// <summary>Текст ошибки, если задача упала.</summary>
    public string? Error { get; set; }
}
