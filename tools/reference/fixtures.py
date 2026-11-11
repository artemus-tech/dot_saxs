"""Файлы .npy для проверки чтения .npy в C#: все поддерживаемые виды чисел, порядок байт, порядок Fortran,
0-мерный и пустые массивы, три версии заголовка. (имя, массив, версия заголовка)."""
import numpy as np

SPECIAL = [0.0, -0.0, 1.0, -1.5, np.inf, -np.inf, np.nan, -np.nan]


def floats(dtype):
    info = np.finfo(dtype)
    tiny_subnormal = np.nextafter(dtype(0), dtype(1))
    return np.array([*SPECIAL, info.max, -info.max, info.tiny, tiny_subnormal, info.eps, 0.1], dtype=dtype)


def ints(dtype):
    info = np.iinfo(dtype)
    return np.array([0, 1, info.max, info.min, info.max - 1, info.min + 1, 42], dtype=dtype)


def complexes(dtype):
    parts = floats(np.float64 if dtype == np.complex128 else np.float32)
    out = np.empty(len(parts), dtype=dtype)
    out.real, out.imag = parts, parts[::-1]
    return out


FIXTURES = [
    ("float64", floats(np.float64), (1, 0)),
    ("float32", floats(np.float32), (1, 0)),
    ("float16", floats(np.float16), (1, 0)),
    *[(np.dtype(t).name, ints(t), (1, 0)) for t in
      (np.int8, np.int16, np.int32, np.int64, np.uint8, np.uint16, np.uint32, np.uint64)],
    ("bool", np.array([True, False, False, True]), (1, 0)),
    ("complex128", complexes(np.complex128), (1, 0)),
    ("complex64", complexes(np.complex64), (1, 0)),
    ("float64-big-endian", floats(np.float64).astype(">f8"), (1, 0)),
    ("int32-big-endian", ints(np.int32).astype(">i4"), (1, 0)),
    ("matrix-fortran-order", np.asfortranarray(np.arange(12.0).reshape(3, 4)), (1, 0)),
    ("matrix-c-order", np.arange(12, dtype=np.int64).reshape(3, 4), (1, 0)),
    ("cube", np.arange(24.0).reshape(2, 3, 4) / 7, (1, 0)),
    ("scalar", np.array(3.5), (1, 0)),
    ("empty", np.zeros(0), (1, 0)),
    ("empty-matrix", np.zeros((2, 0)), (1, 0)),
    ("long", np.arange(4099.0) * 0.25, (1, 0)),
    ("header-version-2", np.arange(5.0), (2, 0)),
    ("header-version-3", np.arange(5.0), (3, 0)),
]
