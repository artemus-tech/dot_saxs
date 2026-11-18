"""Первые эталоны NumPy — по одному примеру на каждый уровень совпадения (SPEC.md п. 8):
сложение и сумма — побитово, экспонента — в ULP. Сумма показывает, почему у нас свой Sum (DECISIONS.md п. 24):
десять раз по 0.1 у NumPy дают 1.0, а сложение подряд — 0.9999999999999999."""
import numpy as np
from refcase import BITWISE, Case, arg, rng, special, ulp, view

S, S32 = special(np.float64), special(np.float32)

CASES = [
    # numpy.add: все пары особых значений (inf + -inf → nan с предупреждением), случайные, float32, целые с переполнением.
    Case("numpy.add", "special-pairs", arg("x1", np.repeat(S, len(S))), arg("x2", np.tile(S, len(S))), match=BITWISE),
    Case("numpy.add", "random", arg("x1", rng("add/random/x1").standard_normal(1000) * 1e3),
         arg("x2", rng("add/random/x2").standard_normal(1000)), match=BITWISE),
    Case("numpy.add", "float32", arg("x1", np.repeat(S32, len(S32))), arg("x2", np.tile(S32, len(S32))), match=BITWISE),
    Case("numpy.add", "int64-wraparound", arg("x1", np.array([2**63 - 1, -2**63, 5], dtype=np.int64)),
         arg("x2", np.array([1, -1, -7], dtype=np.int64)), match=BITWISE),
    Case("numpy.add", "matrix", arg("x1", rng("add/matrix/x1").standard_normal((3, 4))),
         arg("x2", rng("add/matrix/x2").standard_normal((3, 4))), match=BITWISE),
    Case("numpy.add", "shapes-differ", arg("x1", np.ones(3)), arg("x2", np.ones(4)), match=BITWISE,
         raises="ValueError"),

    # numpy.sum: попарное суммирование; по строкам и по столбцам матрицы — разный порядок сложения.
    Case("numpy.sum", "ten-tenths", arg("a", np.full(10, 0.1)), match=BITWISE),
    Case("numpy.sum", "random-100", arg("a", rng("sum/random-100").standard_normal(100)), match=BITWISE),
    Case("numpy.sum", "random-1000", arg("a", rng("sum/random-1000").standard_normal(1000)), match=BITWISE),
    Case("numpy.sum", "special", arg("a", S), match=BITWISE),
    Case("numpy.sum", "empty", arg("a", np.zeros(0)), match=BITWISE),
    Case("numpy.sum", "int64", arg("a", np.arange(-5, 1000, 7, dtype=np.int64)), match=BITWISE),
    Case("numpy.sum", "float32", arg("a", rng("sum/float32").standard_normal(1000).astype(np.float32)), match=BITWISE),
    Case("numpy.sum", "matrix", arg("a", rng("sum/matrix").standard_normal((40, 30))), match=BITWISE),
    Case("numpy.sum", "matrix-axis-0", arg("a", rng("sum/matrix").standard_normal((40, 30))), axis=0, match=BITWISE),
    Case("numpy.sum", "matrix-axis-1", arg("a", rng("sum/matrix").standard_normal((40, 30))), axis=1, match=BITWISE),
    Case("numpy.sum", "axis-out-of-range", arg("a", np.ones((2, 2))), axis=2, match=BITWISE, raises="AxisError"),

    # numpy.exp: переполнение (inf с предупреждением), исчезновение порядка (0 без предупреждения), особые значения.
    Case("numpy.exp", "grid", arg("x", np.linspace(-750.0, 715.0, 2001)), match=ulp(1)),
    Case("numpy.exp", "special", arg("x", S), match=ulp(1)),
    Case("numpy.exp", "random", arg("x", rng("exp/random").standard_normal(1000) * 10), match=ulp(1)),
    Case("numpy.exp", "float32", arg("x", np.linspace(-110.0, 90.0, 1001, dtype=np.float32)), match=ulp(1)),
]


def spread(key, shape):
    """Случайные числа от 1e-6 до 1e6: при другом порядке сложения сумма почти наверняка разойдётся в последних битах."""
    r = rng(key)
    return r.standard_normal(shape) * 10.0 ** r.integers(-6, 7, shape)


# numpy.sum по видам — массивам, числа которых лежат в памяти не подряд: срезы, шаги, перестановка размеров.
# NumPy обходит их по-своему (порядок осей по шагам, слияние осей, буфер по 8192 числа), и сумма зависит от этого
# порядка. Порт повторяет обход (src/DotSaxs/Core/Reduction.cs), эти кейсы его проверяют.
M = spread("sum/view/matrix", (40, 30))
WIDE = spread("sum/view/wide", (300, 200))
LONG = spread("sum/view/long", (3, 20000))
CUBE = spread("sum/view/cube", (20, 30, 40))
T4 = spread("sum/view/tensor4d", (6, 7, 8, 9))
VEC = spread("sum/view/vector", (40001,))


def view_cases(name, v, axes):
    """Кейсы суммы вида: всего и по каждой оси из axes."""
    cases = [Case("numpy.sum", name, arg("a", v), match=BITWISE)]
    cases += [Case("numpy.sum", f"{name}-axis-{a}", arg("a", v), axis=a, match=BITWISE) for a in axes]
    return cases


CASES += [
    *view_cases("view-transposed", view(M, transpose=(1, 0)), [0, 1]),
    # 300 × 50 = 15 000 чисел не подряд: NumPy складывает кусками по 163 строки (8150 чисел) — буфер 8192.
    *view_cases("view-columns", view(WIDE, index=np.s_[:, 10:60]), [0, 1]),
    # Строка длиннее буфера (9000 > 8192): NumPy складывает каждую строку попарно, а строки — подряд.
    *view_cases("view-long-rows", view(LONG, index=np.s_[:, :9000]), []),
    *view_cases("view-step", view(VEC, index=np.s_[::3]), []),
    *view_cases("view-reversed", view(WIDE, index=np.s_[::-1, ::2]), [1]),
    *view_cases("view-cube-moveaxis", view(CUBE, transpose=(2, 0, 1)), [0, 1, 2]),
    *view_cases("view-cube-sliced", view(CUBE, index=np.s_[2:18:3, :, 5:35]), [0, 1, 2]),
    *view_cases("view-tensor4d", view(T4, transpose=(2, 0, 3, 1), index=np.s_[:, 1:6, ::2, :]), [2]),
    *view_cases("view-one-row", view(M, index=np.s_[3:4, :]), [0, 1]),
    *view_cases("view-column", view(WIDE, index=np.s_[:, 7]), []),
    # Все 20 слоёв куба по 30 чисел из 40 в строке: сумма по оси 0 идёт кусками по 9 слоёв — 8100 чисел в буфере.
    *view_cases("view-cube-inner", view(CUBE, index=np.s_[:, :, 5:35]), [0]),
    # Четыре оси обхода и после слияния: две внешние оси перебираются вне буфера.
    *view_cases("view-tensor4d-moved", view(T4, transpose=(2, 0, 3, 1), index=np.s_[:, ::2, 1:, :]), [0]),
    *view_cases("view-tensor4d-tail", view(T4, index=np.s_[:, :, ::3, ::2]), [0, 1]),
    # float32 складывается во float32 тем же обходом.
    *view_cases("view-float32", view(CUBE.astype(np.float32), index=np.s_[:, :, 5:35]), [0, 2]),
]
