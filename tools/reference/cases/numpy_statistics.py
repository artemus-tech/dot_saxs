"""Своя статистика вектора (этап 2, DECISIONS.md п. 24): Max, Min, IndexOfMax, IndexOfMin, Mean, Variance,
StandardDeviation, Median, Product, CumulativeSum, CumulativeProduct, Diff, Sort, IndicesOfValuesOrdered — до бита
как у NumPy. Max и Min повторяют векторный код NumPy (AVX2): при равных 0 и −0 он выбирает одно из них по номеру
полосы, поэтому кейсы с нулями разной длины."""
import numpy as np
from refcase import BITWISE, Case, arg, rng, special, view

S, S32 = special(np.float64), special(np.float32)


def spread(key, n, dtype=np.float64):
    """Числа от 1e-3 до 1e3 со знаком: при другом порядке действий последние биты почти наверняка разойдутся."""
    r = rng(key)
    return (r.standard_normal(n) * 10.0 ** r.integers(-3, 4, n)).astype(dtype)


def zeros(key, n, dtype=np.float64):
    """0, −0 и ±1 вперемешку: при равных нулях NumPy выбирает тот, что попал в нужную полосу вектора."""
    return rng(key).choice(np.array([0.0, -0.0, 0.0, -0.0, -1.0, 1.0]), n).astype(dtype)


R1000 = spread("stats/random-1000", 1000)
R32 = spread("stats/float32", 1000, np.float32)
WITH_NAN = R1000.copy()
WITH_NAN[[17, 400, 401]] = np.nan
BIG = rng("stats/big").standard_normal(40001) * 1e3
INTS = rng("stats/int64").integers(-10**6, 10**6, 1001, dtype=np.int64)
BIG_INTS = rng("stats/int64-big").integers(-2**62, 2**62, 50001, dtype=np.int64)
ZEROS = {n: zeros(f"stats/zeros-{n}", n) for n in (2, 5, 37, 100, 131)}
ZEROS32 = {n: zeros(f"stats/zeros32-{n}", n, np.float32) for n in (9, 77, 200)}
EMPTY = np.zeros(0)


def minmax(python):
    """Кейсы np.max и np.min: подряд и с шагом, NaN, нули разного знака, float32 и int64."""
    cases = [
        Case(python, "random-1000", arg("a", R1000), match=BITWISE),
        Case(python, "big", arg("a", BIG), match=BITWISE),
        Case(python, "special", arg("a", S), match=BITWISE),
        Case(python, "nan", arg("a", WITH_NAN), match=BITWISE),
        Case(python, "one", arg("a", np.array([-0.0])), match=BITWISE),
        Case(python, "float32", arg("a", R32), match=BITWISE),
        Case(python, "int64", arg("a", INTS), match=BITWISE),
        Case(python, "step", arg("a", view(BIG, index=np.s_[::3])), match=BITWISE),
        Case(python, "empty", arg("a", EMPTY), match=BITWISE, raises="ValueError"),
    ]
    cases += [Case(python, f"zeros-{n}", arg("a", a), match=BITWISE) for n, a in ZEROS.items()]
    cases += [Case(python, f"zeros-step-{n}", arg("a", view(a, index=np.s_[::2])), match=BITWISE)
              for n, a in ZEROS.items() if n > 20]
    cases += [Case(python, f"zeros32-{n}", arg("a", a), match=BITWISE) for n, a in ZEROS32.items()]
    return cases


def argminmax(python):
    """Кейсы np.argmax и np.argmin: первый максимум, первый NaN, равные числа."""
    return [
        Case(python, "random-1000", arg("a", R1000), match=BITWISE),
        Case(python, "nan", arg("a", WITH_NAN), match=BITWISE),
        Case(python, "ties", arg("a", np.array([1.0, 3.0, -2.0, 3.0, -2.0, 0.0, -0.0])), match=BITWISE),
        Case(python, "zeros", arg("a", ZEROS[131]), match=BITWISE),
        Case(python, "float32", arg("a", R32), match=BITWISE),
        Case(python, "int64", arg("a", INTS), match=BITWISE),
        Case(python, "step", arg("a", view(BIG, index=np.s_[::3])), match=BITWISE),
        Case(python, "empty", arg("a", EMPTY), match=BITWISE, raises="ValueError"),
    ]


def moments(python, **kwargs):
    """Кейсы np.mean, np.var, np.std: попарная сумма NumPy, float32, целые (сумма в double кусками по 8192)."""
    return [
        Case(python, "random-1000", arg("a", R1000), match=BITWISE, **kwargs),
        Case(python, "big", arg("a", BIG), match=BITWISE, **kwargs),
        Case(python, "step", arg("a", view(BIG, index=np.s_[::3])), match=BITWISE, **kwargs),
        Case(python, "float32", arg("a", R32), match=BITWISE, **kwargs),
        Case(python, "int64", arg("a", INTS), match=BITWISE, **kwargs),
        Case(python, "int64-big", arg("a", BIG_INTS), match=BITWISE, **kwargs),
        Case(python, "nan", arg("a", WITH_NAN), match=BITWISE, **kwargs),
        Case(python, "one", arg("a", np.array([2.5])), match=BITWISE, **kwargs),
        Case(python, "empty", arg("a", EMPTY), match=BITWISE, **kwargs),
        Case(python, "overflow", arg("a", np.array([1e308, -1e308, 1e308])), match=BITWISE, **kwargs),
        Case(python, "infinities", arg("a", np.array([1.0, np.inf, -np.inf])), match=BITWISE, **kwargs),
    ]


CASES = [*minmax("numpy.max"), *minmax("numpy.min"), *argminmax("numpy.argmax"), *argminmax("numpy.argmin")]

CASES += moments("numpy.mean") + moments("numpy.var") + moments("numpy.std")
CASES += [
    # Выборочная дисперсия — deltaDegreesOfFreedom: 1; степеней свободы не осталось — деление на ноль.
    Case("numpy.var", "sample", arg("a", R1000), ddof=1, match=BITWISE),
    Case("numpy.var", "no-freedom", arg("a", np.array([1.0, 2.0])), ddof=2, match=BITWISE),
    Case("numpy.var", "one-sample", arg("a", np.array([5.0])), ddof=1, match=BITWISE),
    Case("numpy.std", "sample", arg("a", R1000), ddof=1, match=BITWISE),
    Case("numpy.std", "float32-sample", arg("a", R32), ddof=1, match=BITWISE),
]

CASES += [
    Case("numpy.median", "odd", arg("a", R1000[:999]), match=BITWISE),
    Case("numpy.median", "even", arg("a", R1000), match=BITWISE),
    Case("numpy.median", "zeros", arg("a", ZEROS[100]), match=BITWISE),
    Case("numpy.median", "nan", arg("a", WITH_NAN), match=BITWISE),
    Case("numpy.median", "float32", arg("a", R32), match=BITWISE),
    Case("numpy.median", "int64", arg("a", INTS), match=BITWISE),
    Case("numpy.median", "step", arg("a", view(BIG, index=np.s_[::3])), match=BITWISE),
    Case("numpy.median", "one", arg("a", np.array([-0.0])), match=BITWISE),
    Case("numpy.median", "empty", arg("a", EMPTY), match=BITWISE),
    Case("numpy.median", "infinities", arg("a", np.array([-np.inf, np.inf])), match=BITWISE),
]

PROD = spread("stats/prod", 300) / 3  # множители около 1: произведение не уходит в 0 и ∞
CASES += [
    Case("numpy.prod", "random", arg("a", PROD), match=BITWISE),
    Case("numpy.prod", "step", arg("a", view(PROD, index=np.s_[::2])), match=BITWISE),
    Case("numpy.prod", "float32", arg("a", PROD.astype(np.float32)), match=BITWISE),
    Case("numpy.prod", "int64", arg("a", np.arange(1, 21, dtype=np.int64)), match=BITWISE),
    Case("numpy.prod", "empty", arg("a", EMPTY), match=BITWISE),
    Case("numpy.prod", "overflow", arg("a", np.array([1e200, 1e200, 0.5])), match=BITWISE),
    Case("numpy.prod", "zero-times-infinity", arg("a", np.array([0.0, np.inf])), match=BITWISE),
]

for python in ("numpy.cumsum", "numpy.cumprod"):
    a = R1000 if python == "numpy.cumsum" else PROD
    CASES += [
        Case(python, "random", arg("a", a), match=BITWISE),
        Case(python, "step", arg("a", view(a, index=np.s_[::2])), match=BITWISE),
        Case(python, "float32", arg("a", a.astype(np.float32)), match=BITWISE),
        Case(python, "int64", arg("a", np.arange(1, 21, dtype=np.int64)), match=BITWISE),
        Case(python, "negative-zero", arg("a", np.array([-0.0, -0.0, 1.0])), match=BITWISE),
        Case(python, "empty", arg("a", EMPTY), match=BITWISE),
        Case(python, "overflow", arg("a", np.array([1e308, 1e308, -np.inf])), match=BITWISE),
    ]

CASES += [
    Case("numpy.diff", "random", arg("a", R1000), match=BITWISE),
    Case("numpy.diff", "step", arg("a", view(BIG, index=np.s_[::3])), match=BITWISE),
    Case("numpy.diff", "float32", arg("a", R32), match=BITWISE),
    Case("numpy.diff", "int64", arg("a", INTS), match=BITWISE),
    Case("numpy.diff", "bool", arg("a", np.array([True, True, False, True])), match=BITWISE),
    Case("numpy.diff", "one", arg("a", np.array([1.0])), match=BITWISE),
    Case("numpy.diff", "overflow", arg("a", np.array([-1e308, 1e308, np.inf, np.inf])), match=BITWISE),
]

# Сортировка — устойчивая (stable=True): у равных чисел, в том числе 0 и −0, порядок как в векторе. Обычный
# np.sort переставляет 0 и −0 как придётся (DECISIONS.md п. 34). NaN — в конце при обоих порядках.
MIXED = np.array([3.0, np.nan, -0.0, 1.0, 0.0, 3.0, np.nan, -1.0, -0.0, np.inf, -np.inf])
for python in ("numpy.sort", "numpy.argsort"):
    for order, extra in (("", {}), ("-descending", {"descending": True})):
        CASES += [
            Case(python, f"random{order}", arg("a", R1000), stable=True, match=BITWISE, **extra),
            Case(python, f"mixed{order}", arg("a", MIXED), stable=True, match=BITWISE, **extra),
            Case(python, f"zeros{order}", arg("a", ZEROS[131]), stable=True, match=BITWISE, **extra),
            Case(python, f"float32{order}", arg("a", R32), stable=True, match=BITWISE, **extra),
            Case(python, f"int64{order}", arg("a", INTS), stable=True, match=BITWISE, **extra),
            Case(python, f"step{order}", arg("a", view(BIG, index=np.s_[::3])), stable=True, match=BITWISE,
                 **extra),
            Case(python, f"empty{order}", arg("a", EMPTY), stable=True, match=BITWISE, **extra),
        ]
