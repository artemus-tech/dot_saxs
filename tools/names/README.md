# Таблица имён `names.csv`

Каждое публичное имя NumPy и SciPy → имя в C# и как это делается у нас (SPEC.md п. 2; DECISIONS.md п. 10–11, 21–26).
Таблица генерируется, руками её не правят: правятся словарь и ручные решения рядом с генератором.

## Файлы

| Файл | Что это |
|---|---|
| `names.csv` (в корне) | вся таблица: `python, kind, csharp, status, stage, how, call, file, line, note` |
| `tools/names/tokens.csv` | как разбита на смысловые части каждая слитная часть имени (`argmax` → `ArgMax`) и почему: `whole`, `split`, `split-letter`, `override`, `code`, `digits` (часть с цифрами в `scipy.special`), `unknown`, `unknown-long` |
| `tools/names/overrides.csv` | ручные решения: часть имени → как писать в C# |
| `tools/names/jargon.txt` | неделимые смысловые части, которых мало в прозе документации (`arg`, `cum`, `lin`, `nan`…) |
| `tools/names/codes.txt` | части, которые остаются одним словом: коды процедур и слова, которые нельзя делить |
| `tools/names/placement.csv` | ручные решения «как это делается у нас»: имя или `префикс.*` → `how`, `call`, `note`; в `call` можно `{name}` и `{cs}` |
| `tools/names/generate.py` | генератор |

## Откуда берутся имена

- Публичные модули — из тестов самих пакетов: `numpy/tests/test_public_api.py` (`PUBLIC_MODULES`,
  `PUBLIC_ALIASED_MODULES`) и `scipy._lib._public_api.PUBLIC_MODULES`.
- Члены модуля — `__all__`, а для `scipy.linalg.lapack` и `scipy.linalg.blas` — все публичные имена (обёрток нет в `__all__`).
- Члены публичных классов — свои и унаследованные от непубличных предков; унаследованное от встроенных типов Python не берётся.
- Один объект под несколькими именами: каноническое — самое короткое, при равной длине — совпадающее с `__name__` объекта
  (`scipy.special.roots_legendre`, а не `p_roots`); остальные — «псевдоним» с тем же именем в C#.

## Как строится имя в C#

1. Части через `_` пишутся с заглавной: `default_rng` → `DefaultRng`.
2. Слитная часть делится на смысловые: `argmax` → `ArgMax`, `linspace` → `LinSpace`. Словарь — проза документации
   numpy и scipy без примеров кода плюс `jargon.txt`. Обычное английское слово не делится (`format`, `transform`);
   отделяется одна буква с краю, если остаток — надёжное слово (`hstack` → `HStack`, `cdist` → `CDist`).
3. Уже PascalCase — как есть (`KDTree`, `PCG64`). Обёртки LAPACK и BLAS — код одним словом (`dgesv` → `Dgesv`),
   значения `scipy.constants` — исходное написание (`g`, `G`).
4. `scipy.special` делится на слова строже: только известные слова от трёх букв (и `ln`), буква с краю не отделяется —
   `logsumexp` → `LogSumExp`, `gammaln` → `GammaLn`, `betaincinv` → `BetaIncInv`. Одним словом остаются части, которые
   так на слова не делятся: коды Cephes (`ndtri` → `Ndtri`, `nbdtrik` → `Nbdtrik`), части с цифрами (`hyp2f1` → `Hyp2f1`),
   `xlogy` → `Xlogy`. Основы кодов Cephes (`ndtr`, `chndtr`…) — в `codes.txt`: кусками слитных имён они не берутся
   (`chndtrinc` → `Chndtrinc`).
5. Коллизии (DECISIONS.md п. 10): член совпал с контейнером → `_` (`Np.Fft.Fft_`); модуль совпал с классом → `Module`
   (`Np.Polynomial.ChebyshevModule`); объект совпал с классом → `_` (`SciPy.Stats.Uniform_`).

## Как это делается у нас (`how` и `call`)

В публичном API нет `axis`, broadcasting и операторов у массивов; наружу — `Vector`, `Matrix`, `CubeMatrix`,
`Tensor4D` … `Tensor8D`, `SparseMatrix` (DECISIONS.md п. 24–26). `call` — пример вызова: `v` — `Vector`, `m` — `Matrix`,
`a` — любой наш массив, `obj` — объект класса.

| `how` | Что значит | Пример |
|---|---|---|
| функция числа | функция одного числа, к массиву — через `Select` | `numpy.exp` → `v.Select(Np.Exp)` |
| уже есть в .NET | точный двойник в .NET | `numpy.sqrt` → `v.Select(Math.Sqrt)` |
| LINQ | пишется через LINQ | `numpy.add` → `v.Select((x, i) => x + w[i])` |
| метод | метод наших типов | `numpy.sum` → `a.Sum()`, `numpy.linalg.solve` → `m.Solve(…)` |
| метод объекта, свойство объекта | член перенесённого класса | `obj.Query(…)` |
| создание | создаёт массив | `numpy.zeros` → `Vector<double>.Zeros(n)` |
| форма | меняет форму (DECISIONS.md п. 22) | `numpy.delete` → `m.RemoveRow(3)` |
| функция, класс, объект, константа | обычная функция, класс, готовый объект, константа | `SciPy.Integrate.Quad(…)` |
| вид чисел | вид чисел — параметр типа | `numpy.float64` → `double` |
| исключение, предупреждение, модуль, формат файла | как называются | `catch (LinAlgError e)` |
| внутри | переносится, но наружу не выходит | процедуры BLAS и LAPACK |
| не переносится | причина в `note` | `numpy.moveaxis` — `axis` нет |
| решить | решаем на этапе из `stage` | `numpy.ma` — нужны ли маскированные массивы |

Правила — в `placement()` генератора: функции чисел NumPy и SciPy, свёртки (`sum`, `mean`…) — методы всех типов,
функции SciPy с `axis` — методы `Vector`, линейная алгебра — методы `Matrix`, `ndimage` — `Matrix` и `CubeMatrix`,
члены класса — как класс. Всё остальное — `placement.csv`.

## Статусы

`запланировано`, `перенесено` (имя стоит в `[Original("…")]` в `src/**/*.cs`), `псевдоним`, `не переносится` (с причиной
в `note`; это же значение в `how`).
Пометка «разбиение на части: проверить» — в имени есть длинная часть, которую генератор не узнал.

## Проверки

- CI (задание `names` в `ci.yml`) пересчитывает таблицу на закреплённых numpy и scipy и сравнивает с коммитом.
- Тесты `NamesTests`: примеры из SPEC.md п. 2 и решений, уникальность имён, «перенесено» ⇔ член в сборке с `[Original]`,
  у каждой строки известный `how`, «не переносится» в `status` ⇔ в `how`, `call` есть везде, где он нужен.

Запуск: `python -I tools/names/generate.py <корень репозитория> <каталог>` там, где стоят закреплённые numpy и scipy
(эталонный контейнер `reference/`).
