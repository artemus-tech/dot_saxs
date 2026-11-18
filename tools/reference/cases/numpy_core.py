"""Первые эталоны NumPy — по одному примеру на каждый уровень совпадения (SPEC.md п. 8):
сложение и сумма — побитово, экспонента — в ULP. Сумма показывает, почему у нас свой Sum (DECISIONS.md п. 24):
десять раз по 0.1 у NumPy дают 1.0, а сложение подряд — 0.9999999999999999."""
import numpy as np
from refcase import BITWISE, Case, arg, rng, special, ulp

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
