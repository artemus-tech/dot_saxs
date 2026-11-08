"""Проверка эталонного контейнера: версии numpy и scipy, SIMD numpy, OpenBLAS и LAPACK внутри него.

Печатает отчёт в JSON и падает, если версии не те или numpy.linalg.solve даёт неверный ответ.
"""
import ctypes
import json
import os
import platform
import sys

import numpy as np
import scipy
import threadpoolctl


def lapack_inside(path):
    """Версия LAPACK, встроенного в OpenBLAS из колеса numpy (ilaver; у numpy — 64-битные целые)."""
    lib = ctypes.CDLL(path)
    for name in ("scipy_ilaver_64_", "scipy_ilaver_", "ilaver_"):
        if hasattr(lib, name):
            v = [ctypes.c_int64() for _ in range(3)]
            getattr(lib, name)(*map(ctypes.byref, v))
            return ".".join(str(x.value) for x in v)
    return None


simd = np.show_config(mode="dicts")["SIMD Extensions"]
blas = threadpoolctl.threadpool_info()
openblas = next(x for x in blas if x["internal_api"] == "openblas")
a = np.array([[4.0, 1, 0, 0], [1, 4, 1, 0], [0, 1, 4, 1], [0, 0, 1, 4]])
b = np.arange(1.0, 5.0)
residual = float(np.max(np.abs(a @ np.linalg.solve(a, b) - b)))

report = {
    "python": sys.version.split()[0],
    "machine": platform.machine(),
    "numpy": np.__version__,
    "scipy": scipy.__version__,
    "numpy SIMD baseline": simd["baseline"],
    "numpy SIMD found": simd["found"],
    "BLAS": [f'{x["internal_api"]} {x.get("version")} {x.get("architecture")} threads={x["num_threads"]}' for x in blas],
    "LAPACK внутри OpenBLAS numpy (ilaver)": lapack_inside(openblas["filepath"]),
    "solve 4x4, max |a·x - b|": residual,
    "env": {k: os.environ[k] for k in ("NPY_DISABLE_CPU_FEATURES", "OPENBLAS_CORETYPE", "OPENBLAS_NUM_THREADS")
            if k in os.environ},
}
print(json.dumps(report, ensure_ascii=False, indent=1))

assert report["numpy"] == os.environ["NUMPY_VERSION"], "версия numpy"
assert report["scipy"] == os.environ["SCIPY_VERSION"], "версия scipy"
assert openblas.get("version") == os.environ["OPENBLAS_VERSION"], \
    "OpenBLAS в колесе numpy сменился — обновите источник переноса BLAS и LAPACK (DECISIONS.md, п. 8)"
assert residual < 1e-14, "numpy.linalg.solve: неверный ответ"
