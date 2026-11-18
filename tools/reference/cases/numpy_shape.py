"""Работа с формой (этап 2, DECISIONS.md п. 22, 24, 34): AppendRow, InsertColumn, RemoveRow… — как np.append,
np.insert, np.delete; RotateCounterClockWise и RotateClockWise — np.rot90; FromBlocks — np.block; Tri, Vandermonde,
Np.MeshGrid, GetWindows (sliding_window_view) и CrossProduct (np.cross)."""
import numpy as np
from refcase import BITWISE, Case, arg, rng

M = rng("shape/matrix").standard_normal((3, 4))
ROW, COLUMN = rng("shape/row").standard_normal(4), rng("shape/column").standard_normal(3)
X = rng("shape/x").standard_normal(6)
A, B = np.arange(4.0).reshape(2, 2), np.arange(6.0).reshape(2, 3) + 10
C, D = np.arange(2.0).reshape(1, 2) + 20, np.arange(3.0).reshape(1, 3) + 30

CASES = [
    # AppendRow, PrependRow, InsertRow, RemoveRow и то же со столбцами.
    Case("numpy.append", "row", arg("arr", M), arg("values", ROW[None, :]), axis=0, match=BITWISE),
    Case("numpy.append", "column", arg("arr", M), arg("values", COLUMN[:, None]), axis=1, match=BITWISE),
    Case("numpy.insert", "row-first", arg("arr", M), arg("obj", 0), arg("values", ROW), axis=0, match=BITWISE),
    Case("numpy.insert", "row-middle", arg("arr", M), arg("obj", 2), arg("values", ROW), axis=0, match=BITWISE),
    Case("numpy.insert", "column-middle", arg("arr", M), arg("obj", 1), arg("values", COLUMN), axis=1, match=BITWISE),
    Case("numpy.insert", "column-last", arg("arr", M), arg("obj", 4), arg("values", COLUMN), axis=1, match=BITWISE),
    Case("numpy.delete", "row", arg("arr", M), arg("obj", 1), axis=0, match=BITWISE),
    Case("numpy.delete", "column", arg("arr", M), arg("obj", 3), axis=1, match=BITWISE),

    # RotateCounterClockWise — np.rot90(m), RotateClockWise — np.rot90(m, -1), дважды — np.rot90(m, 2).
    Case("numpy.rot90", "counterclockwise", arg("m", M), match=BITWISE),
    Case("numpy.rot90", "clockwise", arg("m", M), k=-1, match=BITWISE),
    Case("numpy.rot90", "half", arg("m", M), k=2, match=BITWISE),
    Case("numpy.rot90", "empty", arg("m", np.zeros((0, 3))), match=BITWISE),

    # FromBlocks — np.block: строки блоков, у блоков строки одно число строк.
    Case("numpy.block", "two-by-two", arg("arrays", [[A, B], [C, D]]), match=BITWISE),
    Case("numpy.block", "column-of-blocks", arg("arrays", [[A], [C]]), match=BITWISE),
    Case("numpy.block", "rows-differ", arg("arrays", [[A, C]]), match=BITWISE, raises="ValueError"),
    Case("numpy.block", "columns-differ", arg("arrays", [[A], [B]]), match=BITWISE, raises="ValueError"),
    Case("numpy.block", "empty-row", arg("arrays", [[A], []]), match=BITWISE, raises="ValueError"),
    Case("numpy.block", "empty", arg("arrays", []), match=BITWISE, raises="ValueError"),

    # Tri — np.tri; отрицательный размер у NumPy — пустая матрица, а не ошибка.
    Case("numpy.tri", "square", arg("N", 4), match=BITWISE),
    Case("numpy.tri", "wide-above", arg("N", 3), arg("M", 5), arg("k", 1), match=BITWISE),
    Case("numpy.tri", "tall-below", arg("N", 4), arg("M", 2), arg("k", -1), match=BITWISE),
    Case("numpy.tri", "negative", arg("N", -1), match=BITWISE),

    # Vandermonde — np.vander: степени умножением подряд.
    Case("numpy.vander", "square", arg("x", X), match=BITWISE),
    Case("numpy.vander", "columns", arg("x", X), arg("N", 8), match=BITWISE),
    Case("numpy.vander", "increasing", arg("x", X), arg("N", 4), increasing=True, match=BITWISE),
    Case("numpy.vander", "no-columns", arg("x", X), arg("N", 0), match=BITWISE),
    Case("numpy.vander", "overflow", arg("x", np.array([1e200, 2.0])), arg("N", 3), match=BITWISE),
    Case("numpy.vander", "negative", arg("x", X), arg("N", -1), match=BITWISE, raises="ValueError"),

    # Np.MeshGrid — np.meshgrid(x, y): строки — y, столбцы — x.
    Case("numpy.meshgrid", "grid", arg("x", X[:4]), arg("y", COLUMN), match=BITWISE),

    # GetWindows — sliding_window_view.
    Case("numpy.lib.stride_tricks.sliding_window_view", "three", arg("x", X), arg("window_shape", 3), match=BITWISE),
    Case("numpy.lib.stride_tricks.sliding_window_view", "whole", arg("x", X), arg("window_shape", 6), match=BITWISE),
    Case("numpy.lib.stride_tricks.sliding_window_view", "zero", arg("x", X), arg("window_shape", 0), match=BITWISE),
    Case("numpy.lib.stride_tricks.sliding_window_view", "too-long", arg("x", X), arg("window_shape", 7),
         match=BITWISE, raises="ValueError"),
    Case("numpy.lib.stride_tricks.sliding_window_view", "negative", arg("x", X), arg("window_shape", -1),
         match=BITWISE, raises="ValueError"),

    # CrossProduct — np.cross векторов из трёх чисел.
    Case("numpy.cross", "random", arg("a", X[:3]), arg("b", X[3:]), match=BITWISE),
    Case("numpy.cross", "overflow", arg("a", np.array([1e200, 1e200, 0.0])), arg("b", np.array([0.0, 1e200, 1e200])),
         match=BITWISE),
    Case("numpy.cross", "two-numbers", arg("a", np.array([1.0, 2.0])), arg("b", np.array([3.0, 4.0])), match=BITWISE,
         raises="ValueError"),
]
