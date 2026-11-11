# Эталоны `tests/references`

Эталон — один вызов функции NumPy или SciPy в эталонном контейнере: входы, результат или ошибка, предупреждения и
уровень совпадения (SPEC.md п. 8). Тесты порта читают эталоны из файлов; база (этап 1, Postgres) хранит их же.
Руками эталоны не правят: правятся описания кейсов, CI пересчитывает и сравнивает с коммитом.

## Файлы

| Файл | Что это |
|---|---|
| `tools/reference/generate.py` | генератор: `python -I tools/reference/generate.py <корень репозитория> <пустой каталог>` |
| `tools/reference/refcase.py` | как описывать кейсы: `Case`, `arg`, уровни `BITWISE`, `ulp(n)`, `tolerance(rtol, atol)`, `rng`, `special` |
| `tools/reference/cases/*.py` | кейсы, список `CASES` в каждом файле |
| `tools/reference/fixtures.py` | файлы .npy всех поддерживаемых видов для проверки чтения .npy в C# |
| `tests/references/<имя Python>/` | `cases.json` и файлы .npy одной функции, например `numpy.linalg.solve/` |
| `tests/references/npy-format/` | файлы .npy и `fixtures.json` с их точным содержимым |
| `tests/references/environment.json` | версии Python, numpy, scipy, OpenBLAS, SIMD numpy и параметры процессора |

## Кейс

```python
Case("numpy.sum", "matrix-axis-0", arg("a", rng("sum/matrix").standard_normal((40, 30))), axis=0, match=BITWISE)
```

- Имя функции — из `names.csv`, только переносимое и не псевдоним; генератор это проверяет.
- Имя кейса — строчные латинские буквы, цифры и дефисы; оно же начало имён файлов: `matrix-axis-0.a.npy`,
  `matrix-axis-0.result.npy`.
- Позиционные аргументы — через `arg(имя, значение)`, именованные — как обычно.
- Случайные данные — только через `rng("ключ")`: у каждого кейса свой генератор, данные не меняются, когда рядом
  добавляют другие кейсы.
- Уровень совпадения — тот, что ждём по умолчанию: `BITWISE` (арифметика, суммы, целые), `ulp(n)` (exp, log, sin…),
  `tolerance(rtol, atol)` (BLAS-3, LAPACK, интеграторы, БПФ): |порт − эталон| ≤ atol + rtol·|эталон|.

## `cases.json`

```json
{"name": "singular", "args": [{"name": "a", "value": {"npy": "singular.a.npy"}}, …], "kwargs": {},
 "match": {"kind": "tolerance", "rtol": 1e-12, "atol": 1e-14},
 "error": {"type": "LinAlgError", "module": "numpy.linalg", "message": "Singular matrix"}, "warnings": []}
```

- У кейса либо `result`, либо `error` (тип, модуль и текст исключения Python — для негативных тестов).
- `warnings` — предупреждения при вызове: `{"type": "RuntimeWarning", "message": "overflow encountered in exp"}`.
- Значения: массив — `{"npy": файл}` (скаляр NumPy — ещё `"scalar": true`), `{"int": 3}`, `{"float": "3ff0000000000000"}`
  (биты double), `{"complex": [биты, биты]}`, `{"bool": true}`, `{"str": "…"}`, `{"none": true}`,
  `{"tuple": […], "fields": […]}`, `{"list": […]}`, `{"dtype": "<f4"}`.
- Входы записываются до вызова: если функция меняет аргумент, в файле — то, что было на входе.

## Окружение

Генератор сам ставит параметры процессора из DECISIONS.md п. 4 (`NPY_DISABLE_CPU_FEATURES`, `OPENBLAS_CORETYPE`,
`OPENBLAS_NUM_THREADS`) и падает, если numpy их не применил: иначе эталоны зависели бы от машины, на которой их
считали. Всё это записано в `environment.json`.

## Проверки

- CI (задание `references` в `ci.yml`) пересчитывает эталоны в эталонном контейнере и сравнивает с коммитом.
  Если они разошлись, новые и изменённые файлы — в логе прогона (tar.gz в base64 между метками
  `=== НАЧАЛО references.tar.gz` и `=== КОНЕЦ`) и в артефакте `references`.
- Локально (numpy без закреплённой версии) генератор работает так же; первый раз на numpy 2.5.3 разошёлся только
  номер версии в `environment.json`, все массивы совпали до бита.
- Тесты `DotSaxs.Testing.Tests`: все файлы `npy-format` читаются точно как записаны, все кейсы загружаются,
  окружение — закреплённые версии и параметры процессора.
