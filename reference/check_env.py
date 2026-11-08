"""Проверка эталонного контейнера: версии, SIMD numpy, OpenBLAS, Reference-LAPACK через ctypes.

Печатает отчёт в JSON и падает, если версии не те или netlib dgesv расходится с numpy.linalg.solve.
"""
import ctypes
import json
import os
import platform
import subprocess
import sys

import numpy as np
import scipy
import threadpoolctl


def netlib_version(lib):
    v = [ctypes.c_int() for _ in range(3)]
    lib.ilaver_(*map(ctypes.byref, v))
    return ".".join(str(x.value) for x in v)


def netlib_dgesv(lib, a, b):
    n, nrhs = b.shape
    a = np.array(a, dtype=np.float64, order="F")
    b = np.array(b, dtype=np.float64, order="F")
    ipiv, info = np.zeros(n, dtype=np.int32), ctypes.c_int()

    def i(v):
        return ctypes.byref(ctypes.c_int(v))

    def p(arr):
        return arr.ctypes.data_as(ctypes.c_void_p)

    lib.dgesv_(i(n), i(nrhs), p(a), i(n), p(ipiv), p(b), i(n), ctypes.byref(info))
    return b, info.value


simd = np.show_config(mode="dicts")["SIMD Extensions"]
lapack = ctypes.CDLL(os.environ["NETLIB_LAPACK"])
a = np.array([[4.0, 1, 0, 0], [1, 4, 1, 0], [0, 1, 4, 1], [0, 0, 1, 4]])
b = np.arange(1.0, 5.0).reshape(4, 1)
x_netlib, info = netlib_dgesv(lapack, a, b)
diff = float(np.max(np.abs(x_netlib - np.linalg.solve(a, b))))

report = {
    "python": sys.version.split()[0],
    "machine": platform.machine(),
    "numpy": np.__version__,
    "scipy": scipy.__version__,
    "numpy SIMD baseline": simd["baseline"],
    "numpy SIMD found": simd["found"],
    "BLAS": [f'{x["internal_api"]} {x.get("version")} {x.get("architecture")} threads={x["num_threads"]}'
             for x in threadpoolctl.threadpool_info()],
    "netlib LAPACK, коммит": open("/opt/netlib/COMMIT").read().strip(),
    "netlib LAPACK, ilaver": netlib_version(lapack),  # в теге v3.12.1 возвращает 3.12.0 — константы не обновлены
    "gfortran": subprocess.run(["gfortran", "--version"], capture_output=True, text=True).stdout.splitlines()[0],
    "dgesv info": info,
    "dgesv netlib vs numpy, max |diff|": diff,
    "env": {k: os.environ[k] for k in ("NPY_DISABLE_CPU_FEATURES", "OPENBLAS_CORETYPE", "OPENBLAS_NUM_THREADS")
            if k in os.environ},
}
print(json.dumps(report, ensure_ascii=False, indent=1))

assert report["numpy"] == os.environ["NUMPY_VERSION"], "версия numpy"
assert report["scipy"] == os.environ["SCIPY_VERSION"], "версия scipy"
assert report["netlib LAPACK, коммит"] == os.environ["LAPACK_COMMIT"], "коммит Reference-LAPACK"
assert info == 0 and diff < 1e-14, "netlib dgesv расходится с numpy.linalg.solve"
