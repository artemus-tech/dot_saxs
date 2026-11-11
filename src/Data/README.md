# DotSaxs.Data — база PostgreSQL

Сервисный слой (SPEC.md п. 9): порт (Np, SciPy, Std) с базой не работает никогда, с ней работают портал, API и воркеры.
EF Core 10 и Npgsql; версии — в `Directory.Packages.props`.

## Что лежит в базе

| Таблица | Что это |
|---|---|
| `ReferenceSets`, `Cases`, `Arrays` | эталоны из `tests/references`: набор (версии, окружение, хэш содержимого), кейсы (уровень совпадения, вызов целиком, ошибка Python), массивы — ссылка на файл .npy и SHA-256; сами массивы остаются в файлах |
| `TestRuns`, `TestResults` | история прогонов тестов: коммит, машина, результат и достигнутый уровень совпадения по каждому тесту |
| `CacheEntries` | кэш результатов: ключ — функция, версия, хэш данных и хэш параметров; результат — файл и его хэш |
| `QueueItems` | очередь задач для воркеров: задачу берут через `FOR UPDATE SKIP LOCKED` |

## Код

- `DotSaxsDb` — контекст EF Core; `DotSaxsDb.Create(строка подключения)`.
- `ReferenceImporter.ImportAsync` — загрузка эталонов; тот же набор (тот же хэш) второй раз не загружается.
- `TaskQueue` — `EnqueueAsync`, `TryTakeAsync` (одним запросом `UPDATE … WHERE Id = (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *`),
  `FinishAsync`.
- `ResultCache` — сначала память процесса, потом база; `ResultCache.Key`, `ResultCache.Worth` (кэшировать, только если
  расчёт дольше чтения из кэша).

## Миграции

Миграции создаёт CI: локально в облачной среде нет .NET (DECISIONS.md п. 2). Задание `migrations` в `ci.yml`
сравнивает модель с миграциями (`dotnet ef migrations has-pending-model-changes`); если модель изменилась, оно создаёт
миграцию и печатает каталог `Migrations` в лог — tar.gz в base64 между метками `=== НАЧАЛО migrations.tar.gz`
и `=== КОНЕЦ`. Миграцию из лога кладут в `src/Data/Migrations` и коммитят. Инструмент `dotnet-ef` закреплён
в `.config/dotnet-tools.json`.

## Тесты

`tests/Data.Tests` — интеграционные: каждому классу тестов своя пустая база, схема — миграциями, в конце база
удаляется. Строка подключения к серверу — в переменной `DOTSAXS_DB`
(`Host=localhost;Port=5432;Username=postgres;Password=postgres`). Без неё тесты пропускаются, в CI — падают.
