# Отчёт 00 — окружение (п. 14 SPEC.md)

2026-11-08 · [PR #1](https://github.com/artemus-tech/dot_saxs/pull/1) · прогон env-check
[42520640231](https://github.com/artemus-tech/dot_saxs/actions/runs/42520640231) (коммит `a8c7f19`) — все 6 разделов OK

> **Изменено 2026-11-09.** По решению пользователя Reference-LAPACK (netlib) убран: сверка только с NumPy и SciPy
> (DECISIONS.md, п. 8). Эталонный контейнер — только numpy и scipy из PyPI. Проверка после изменения: прогон
> [42531026568](https://github.com/artemus-tech/dot_saxs/actions/runs/42531026568) (коммит `47313eb`) — все 6 разделов OK,
> контейнер собирается за 13 с вместо ~3 мин. Внутри OpenBLAS из колеса numpy 2.5.4 — LAPACK 3.12.0 (`ilaver`).
> Источник переноса BLAS и LAPACK — OpenBLAS коммита `446c436e`, из которого собран OpenBLAS в колёсах numpy 2.5.4
> (DECISIONS.md, п. 8); проверка окружения загружает его по коммиту и сверяет версию OpenBLAS в колесе numpy.
> Строки ниже про Reference-LAPACK — история первых прогонов.

## Что сделано

- В репозитории: `SPEC.md` (в `main`), `DECISIONS.md` — ответы на 19 вопросов (4 открыты до этапов 3, 4, 8), `LICENSE` (BSD-3-Clause).
- Проверка окружения в GitHub Actions: `.github/workflows/env-check.yml` и `tools/env/check.sh` — шесть независимых разделов
  с итогом по каждому; полный отчёт приходит комментарием в PR.
- Эталонный контейнер `reference/`: numpy и scipy из PyPI (только колёса), Reference-LAPACK со своим BLAS
  (gfortran, `-O2 -ffp-contract=off`), сверка через ctypes.
- Всё закреплено: .NET SDK — в `global.json`, Docker-образы — по digest, действия Actions — по SHA,
  теги numpy, scipy и LAPACK — по SHA коммита.

## Что проверено

| Раздел | Результат |
|---|---|
| Раннер | ubuntu-24.04, x86-64, 4 vCPU, 15 ГБ, Docker 28.0.4. Процессор от прогона к прогону разный: AMD EPYC 7763 (AVX2 + FMA) или EPYC 9V74 (ещё и AVX-512) |
| .NET | SDK 10.0.403; рантаймы 10.0.8, 10.0.11, 10.0.14, по умолчанию 10.0.14. Пустое решение с xUnit: 0 предупреждений, 0 ошибок; тестов 1 из 1 |
| Исходники | numpy v2.5.4 → `a3d0ef1` (7 подмодулей), scipy v1.18.1 → `e4e854e` (8), Reference-LAPACK v3.12.1 → `6ec7f2b` |
| PostgreSQL | 18.6 |
| Эталонный контейнер | Python 3.13.18, numpy 2.5.4, scipy 1.18.1, OpenBLAS 0.3.34, gfortran 14.2.0, Reference-LAPACK `6ec7f2b`. `dgesv` из netlib и `numpy.linalg.solve` на системе 4×4 совпали побитово (разница 0.0) |
| Параметры эталона (DECISIONS.md, п. 4) | На EPYC 9V74 по умолчанию numpy берёт X86_V3 + X86_V4 + AVX512_ICL, OpenBLAS — SkylakeX и 4 потока; с параметрами — только X86_V3, OpenBLAS Haswell, 1 поток |
| Образы | `python:3.13-slim-trixie@sha256:e48f59cd…` и `postgres:18@sha256:e3d24f69…` — скачаны и запущены именно они |

## Найдено и исправлено по ходу (3 прогона)

1. Прогон 42519223946 показал success при упавшем разделе: без явного `shell` GitHub запускает bash без `pipefail`,
   и код возврата брался от `tee`. Исправлено; следующий прогон с ошибкой помечен как failure.
2. `liblapack.so` не находил `libblas.so.3` — путь `/opt/netlib/lib` добавлен в `ldconfig`.
3. В теге Reference-LAPACK v3.12.1 константы версии не обновлены: `ilaver()` возвращает 3.12.0.
   Версия сверяется по коммиту тега.
4. `docker ps` показывает сервисный Postgres как `postgres:18` без digest — сверка идёт по ID образа контейнера.

## Важно для следующих этапов

- Раннеры GitHub попадаются и с AVX-512, и без него. Без параметров из п. 4 DECISIONS.md numpy выбирает разные ядра,
  и эталоны зависели бы от того, какая машина досталась. На раннере без AVX-512 отключение отсутствующих функций numpy
  молча пропускает (v2.5.4, `numpy/_core/src/common/npy_cpu_features.c`, строки 318–366), так что параметры работают
  на обоих типах раннеров.
- Тесты запускаются на последнем патче рантайма, установленном на раннере (сейчас 10.0.14). Закрепить рантайм — этап 1.
- Облачная среда Claude не собирает C# (DECISIONS.md, п. 2): всё .NET — в Actions. Логи Actions и комментарии PR
  Claude читает из сессии напрямую.

## Дальше — этап 1

Каркас решения (Np, SciPy, Std и тесты; предупреждения = ошибки, закреплённый рантайм), `names.csv` на утверждение,
генератор эталонов в эталонном контейнере, Postgres + EF Core, сквозной срез `Np.Add`, `Np.Sum`, `Np.LinAlg.Solve`.
