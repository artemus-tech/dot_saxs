"""Первые эталоны линейной алгебры — пример совпадения по допуску (SPEC.md п. 8): numpy.linalg.solve."""
import numpy as np
from refcase import Case, arg, rng, tolerance

TOL = tolerance(rtol=1e-12, atol=1e-14)
A4 = np.array([[4.0, 1, 0, 0], [1, 4, 1, 0], [0, 1, 4, 1], [0, 0, 1, 4]])

CASES = [
    Case("numpy.linalg.solve", "tridiagonal-4", arg("a", A4), arg("b", np.arange(1.0, 5.0)), match=TOL),
    Case("numpy.linalg.solve", "random-10", arg("a", rng("solve/random-10/a").standard_normal((10, 10)) + 10 * np.eye(10)),
         arg("b", rng("solve/random-10/b").standard_normal(10)), match=TOL),
    Case("numpy.linalg.solve", "random-10-three-columns",
         arg("a", rng("solve/random-10/a").standard_normal((10, 10)) + 10 * np.eye(10)),
         arg("b", rng("solve/three-columns/b").standard_normal((10, 3))), match=TOL),
    Case("numpy.linalg.solve", "singular", arg("a", np.zeros((2, 2))), arg("b", np.ones(2)), match=TOL),
    Case("numpy.linalg.solve", "not-square", arg("a", np.ones((2, 3))), arg("b", np.ones(2)), match=TOL),
]
