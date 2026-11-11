"""Описание эталонов для файлов cases/*.py (tools/reference/README.md).

Эталон — один вызов функции NumPy или SciPy: входы, результат или ошибка, предупреждения и уровень совпадения,
с которым порт обязан повторить результат (SPEC.md п. 8).
"""
import re
import zlib

import numpy as np

# Уровни совпадения (SPEC.md п. 8). Уровень в эталоне — тот, что ждём по умолчанию; тест может сделать его строже.
BITWISE = {"kind": "bitwise"}  # те же биты, включая знак нуля; любые два NaN равны (DECISIONS.md п. 15)


def ulp(n):
    """Не дальше n шагов между соседними числами double (exp, log, sin…)."""
    return {"kind": "ulp", "ulp": int(n)}


def tolerance(rtol, atol=0.0):
    """|порт − эталон| ≤ atol + rtol·|эталон| (BLAS-3, LAPACK, интеграторы, оптимизаторы, БПФ)."""
    return {"kind": "tolerance", "rtol": float(rtol), "atol": float(atol)}


class Arg:
    """Позиционный аргумент с именем: имя идёт в имя файла .npy и в описание кейса."""

    def __init__(self, name, value):
        self.name, self.value = name, value


def arg(name, value):
    return Arg(name, value)


class View:
    """Вид массива без копирования — срез или перестановка размеров. В .npy пишется исходный массив, в cases.json —
    смещение, размеры и шаги вида в числах исходного массива; функции передаётся сам вид. Так проверяется порядок
    обхода NumPy у массивов, числа которых лежат в памяти не подряд."""

    def __init__(self, base, array):
        self.base, self.array = base, array


def view(base, transpose=None, index=None):
    """Вид base: сначала перестановка размеров (как base.transpose(transpose)), потом срез (как base[index])."""
    base = np.ascontiguousarray(base)
    v = base if transpose is None else base.transpose(transpose)
    return View(base, v if index is None else v[index])


NAME = re.compile(r"[a-z0-9]+(?:-[a-z0-9]+)*")


class Case:
    """Один вызов: Case("numpy.add", "float64", arg("x1", a), arg("x2", b), match=BITWISE)."""

    def __init__(self, python, name, *args, match, **kwargs):
        if not NAME.fullmatch(name):
            raise ValueError(f"{python}: имя кейса «{name}» — только строчные латинские буквы, цифры и дефисы")
        if not all(isinstance(a, Arg) for a in args):
            raise ValueError(f"{python}/{name}: позиционные аргументы — через arg(имя, значение)")
        if not isinstance(match, dict) or match.get("kind") not in ("bitwise", "ulp", "tolerance"):
            raise ValueError(f"{python}/{name}: уровень совпадения — BITWISE, ulp(n) или tolerance(rtol, atol)")
        self.python, self.name, self.args, self.kwargs, self.match = python, name, args, kwargs, match


def rng(key):
    """Свой генератор на каждый кейс: данные кейса не меняются, когда рядом добавляют или убирают другие кейсы."""
    return np.random.default_rng(zlib.crc32(key.encode()))


def special(dtype=np.float64):
    """Особые значения: нули со знаком, бесконечности, NaN, субнормальные, края диапазона, 0.1."""
    info = np.finfo(dtype)
    subnormal = np.nextafter(dtype(0), dtype(1))
    return np.array([0.0, -0.0, 1.0, -1.0, np.inf, -np.inf, np.nan, subnormal, -subnormal, info.tiny, info.max,
                     -info.max, 0.1, info.tiny * 1e4, info.max / 1e4], dtype=dtype)
