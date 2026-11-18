"""Создание массивов и сравнение (этап 2): Create, Identity, FromDiagonal, GetDiagonal, Range, LinSpace и
VectorComparer. Create — один метод с делегатом вместо zeros, ones, full, empty и fromfunction (DECISIONS.md п. 22),
поэтому эталоны — у zeros, ones и full: у fromfunction аргумент — функция Python, её в эталон не записать."""
import numpy as np
from refcase import BITWISE, Case, arg, rng, view

M = rng("diagonal/matrix").standard_normal((3, 5))
R = rng("creation/random")
START = float(R.uniform(-20, 20))
STOP, STEP = START + float(R.uniform(100, 1000)), float(R.uniform(0.05, 0.5))

CASES = [
    # Create(count, i => …) и Create(rows, columns, (row, column) => …).
    Case("numpy.zeros", "vector", arg("shape", 5), match=BITWISE),
    Case("numpy.ones", "matrix", arg("shape", (2, 3)), match=BITWISE),
    Case("numpy.full", "vector", arg("shape", 4), arg("fill_value", 0.1), match=BITWISE),
    Case("numpy.zeros", "negative", arg("shape", -1), match=BITWISE, raises="ValueError"),
    Case("numpy.zeros", "negative-columns", arg("shape", (2, -3)), match=BITWISE, raises="ValueError"),

    # Identity(n).
    Case("numpy.identity", "four", arg("n", 4), match=BITWISE),
    Case("numpy.identity", "empty", arg("n", 0), match=BITWISE),
    Case("numpy.identity", "negative", arg("n", -1), match=BITWISE, raises="ValueError"),

    # FromDiagonal(v, offset): сторона матрицы — длина вектора плюс |offset|.
    Case("numpy.diag", "main", arg("v", np.array([1.0, -2.0, 0.5])), match=BITWISE),
    Case("numpy.diag", "above", arg("v", np.array([1.0, -2.0, 0.5])), k=2, match=BITWISE),
    Case("numpy.diag", "below", arg("v", np.array([1.0, -2.0, 0.5])), k=-1, match=BITWISE),
    Case("numpy.diag", "empty", arg("v", np.zeros(0)), match=BITWISE),

    # GetDiagonal(offset): вид без копирования; за краем матрицы — пустой вектор.
    Case("numpy.diagonal", "main", arg("a", M), match=BITWISE),
    Case("numpy.diagonal", "above", arg("a", M), offset=2, match=BITWISE),
    Case("numpy.diagonal", "below", arg("a", M), offset=-1, match=BITWISE),
    Case("numpy.diagonal", "outside", arg("a", M), offset=7, match=BITWISE),
    Case("numpy.diagonal", "outside-below", arg("a", M), offset=-3, match=BITWISE),
    Case("numpy.diagonal", "transposed", arg("a", view(M, transpose=(1, 0))), offset=1, match=BITWISE),

    # Range(start, stop, step): второе число — start + step, дальше start + i·(второе − start), как у NumPy.
    Case("numpy.arange", "halves", arg("start", 0.0), arg("stop", 10.0), arg("step", 0.5), match=BITWISE),
    Case("numpy.arange", "random", arg("start", START), arg("stop", STOP), arg("step", STEP), match=BITWISE),
    # 1e6 + 0.1 − 1e6 = 0.10000000009313226, а не 0.1: шаг берётся из первых двух чисел.
    Case("numpy.arange", "large-start", arg("start", 1e6), arg("stop", 1e6 + 10), arg("step", 0.1), match=BITWISE),
    Case("numpy.arange", "negative-step", arg("start", 5.0), arg("stop", -5.0), arg("step", -0.3), match=BITWISE),
    Case("numpy.arange", "empty", arg("start", 5.0), arg("stop", 1.0), arg("step", 1.0), match=BITWISE),
    Case("numpy.arange", "infinite-step", arg("start", 0.0), arg("stop", 1.0), arg("step", float("inf")),
         match=BITWISE),
    Case("numpy.arange", "infinite-step-back", arg("start", 0.0), arg("stop", -1.0), arg("step", float("inf")),
         match=BITWISE),
    Case("numpy.arange", "zero-step", arg("start", 0.0), arg("stop", 10.0), arg("step", 0.0), match=BITWISE,
         raises="ZeroDivisionError"),
    Case("numpy.arange", "nan", arg("start", 0.0), arg("stop", float("nan")), arg("step", 1.0), match=BITWISE,
         raises="ValueError"),
    Case("numpy.arange", "infinite", arg("start", 0.0), arg("stop", float("inf")), arg("step", 1.0), match=BITWISE,
         raises="ValueError"),

    # LinSpace(start, stop, count, endpoint): i·шаг + start, последнее — ровно stop.
    Case("numpy.linspace", "unit", arg("start", 0.0), arg("stop", 1.0), num=5, match=BITWISE),
    Case("numpy.linspace", "random", arg("start", START), arg("stop", STOP), num=1000, match=BITWISE),
    Case("numpy.linspace", "no-endpoint", arg("start", 0.0), arg("stop", 1.0), num=10, endpoint=False,
         match=BITWISE),
    Case("numpy.linspace", "reversed", arg("start", 1.0), arg("stop", -1.0), num=7, match=BITWISE),
    Case("numpy.linspace", "one", arg("start", 2.0), arg("stop", 3.0), num=1, match=BITWISE),
    Case("numpy.linspace", "zero", arg("start", 2.0), arg("stop", 3.0), num=0, match=BITWISE),
    # Шаг округлился до нуля (субнормальные числа): NumPy делит номер на число шагов и умножает на разность.
    Case("numpy.linspace", "denormal", arg("start", 0.0), arg("stop", 1.5e-323), num=10, match=BITWISE),
    Case("numpy.linspace", "nan", arg("start", 0.0), arg("stop", float("nan")), num=3, match=BITWISE),
    Case("numpy.linspace", "overflow-subtract", arg("start", -1e308), arg("stop", 1e308), num=5, match=BITWISE),
    Case("numpy.linspace", "infinite-stop", arg("start", 0.0), arg("stop", float("inf")), num=3, match=BITWISE),
    Case("numpy.linspace", "overflow-multiply", arg("start", 0.0), arg("stop", 1.7976931348623157e308), num=4,
         match=BITWISE),
    Case("numpy.linspace", "negative", arg("start", 0.0), arg("stop", 1.0), num=-1, match=BITWISE,
         raises="ValueError"),

    # VectorComparer.ByValues и MatrixComparer.ByValues: NaN равен NaN, 0 равен −0, размеры разные — не равны.
    Case("numpy.array_equal", "nan-and-zeros", arg("a1", np.array([1.0, np.nan, -0.0, np.inf])),
         arg("a2", np.array([1.0, np.nan, 0.0, np.inf])), equal_nan=True, match=BITWISE),
    Case("numpy.array_equal", "different", arg("a1", np.array([1.0, 2.0, 3.0])), arg("a2", np.array([1.0, 2.0, 4.0])),
         equal_nan=True, match=BITWISE),
    Case("numpy.array_equal", "lengths", arg("a1", np.array([1.0, 2.0, 3.0])), arg("a2", np.array([1.0, 2.0])),
         equal_nan=True, match=BITWISE),
    Case("numpy.array_equal", "nan-and-number", arg("a1", np.array([np.nan])), arg("a2", np.array([1.0])),
         equal_nan=True, match=BITWISE),
    # У комплексного числа NaN — если NaN хоть одна часть (np.isnan): два таких числа равны.
    Case("numpy.array_equal", "complex-nan", arg("a1", np.array([complex(np.nan, 1.0), 2 + 3j])),
         arg("a2", np.array([complex(2.0, np.nan), 2 + 3j])), equal_nan=True, match=BITWISE),
    Case("numpy.array_equal", "matrix", arg("a1", M), arg("a2", M.copy()), equal_nan=True, match=BITWISE),
    Case("numpy.array_equal", "matrix-shapes", arg("a1", M[:, :4]), arg("a2", M[:, :4].reshape(4, 3)),
         equal_nan=True, match=BITWISE),
]
