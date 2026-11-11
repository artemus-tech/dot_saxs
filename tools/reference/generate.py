"""Генератор эталонов (tools/reference/README.md, SPEC.md п. 8).

Запуск — в эталонном контейнере с закреплёнными numpy и scipy, в пустой каталог:
    python -I tools/reference/generate.py <корень репозитория> <каталог>
Результат — tests/references: по каталогу на функцию (cases.json и файлы .npy), npy-format (файлы для проверки
чтения .npy) и environment.json. CI пересчитывает эталоны и сравнивает с коммитом.
"""
import os
import sys

# Параметры эталона (DECISIONS.md п. 4): numpy и OpenBLAS выбирают ядра по процессору; без этих параметров эталоны
# зависели бы от машины. Задаются до загрузки numpy, поэтому без них генератор перезапускает сам себя.
PARAMS = {"NPY_DISABLE_CPU_FEATURES": "X86_V4 AVX512_ICL AVX512_SPR", "OPENBLAS_CORETYPE": "Haswell",
          "OPENBLAS_NUM_THREADS": "1"}
if any(os.environ.get(k) != v for k, v in PARAMS.items()):
    os.execve(sys.executable, [sys.executable, "-I", os.path.abspath(__file__), *sys.argv[1:]], {**os.environ, **PARAMS})

import csv  # noqa: E402
import hashlib  # noqa: E402
import importlib  # noqa: E402
import importlib.util  # noqa: E402
import json  # noqa: E402
import platform  # noqa: E402
import struct  # noqa: E402
import warnings  # noqa: E402

import numpy as np  # noqa: E402
import scipy  # noqa: E402
import scipy.linalg  # noqa: E402,F401  — загружает OpenBLAS из scipy, чтобы его версия попала в environment.json
import threadpoolctl  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)  # cases/*.py берут описание кейсов из refcase.py
import fixtures  # noqa: E402
import refcase  # noqa: E402,F401


def bits(x):
    """Число Python float — его биты в шестнадцатеричном виде: точно, вместе со знаком нуля и NaN."""
    return struct.pack("<d", x)[::-1].hex()


def resolve(python):
    """numpy.linalg.solve → функция."""
    parts = python.split(".")
    for i in range(len(parts), 0, -1):
        try:
            obj = importlib.import_module(".".join(parts[:i]))
        except ImportError:
            continue
        for part in parts[i:]:
            obj = getattr(obj, part)
        return obj
    raise ValueError(f"нет такого имени: {python}")


def portable_names(repo):
    """Имена из names.csv, для которых можно делать эталоны: переносимые и не псевдонимы."""
    rows = csv.DictReader(open(os.path.join(repo, "names.csv"), encoding="utf-8"))
    return {r["python"]: r["status"] for r in rows}


def load_cases():
    cases = []
    folder = os.path.join(HERE, "cases")
    for file in sorted(f for f in os.listdir(folder) if f.endswith(".py")):
        spec = importlib.util.spec_from_file_location(f"cases_{file[:-3]}", os.path.join(folder, file))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        cases += module.CASES
    return cases


class Writer:
    """Пишет значения кейса: массивы — в .npy рядом с cases.json, остальное — в JSON с явным видом. Одинаковый массив
    в кейсах одной функции пишется один раз: кейсы ссылаются на тот же файл."""

    def __init__(self, folder, case, written):
        self.folder, self.case, self.written = folder, case, written

    def npy(self, array, path):
        fortran = array.flags.f_contiguous and not array.flags.c_contiguous
        key = (array.dtype.str, array.shape, fortran, hashlib.sha256(np.ascontiguousarray(array).tobytes()).hexdigest())
        if key not in self.written:
            file = ".".join([self.case.name, *path]) + ".npy"
            np.save(os.path.join(self.folder, file), array, allow_pickle=False)
            self.written[key] = file
        return self.written[key]

    def value(self, x, path):
        if isinstance(x, refcase.View):
            base, v = x.base, x.array
            offset = (v.__array_interface__["data"][0] - base.__array_interface__["data"][0]) // base.itemsize
            return {"npy": self.npy(base, path), "view": {"offset": offset, "shape": list(v.shape),
                                                          "strides": [st // base.itemsize for st in v.strides]}}
        if isinstance(x, np.ndarray):
            return {"npy": self.npy(x, path)}
        if isinstance(x, np.generic):
            return {"npy": self.npy(np.asarray(x), path), "scalar": True}
        if isinstance(x, bool):
            return {"bool": x}
        if isinstance(x, int):
            return {"int": x}
        if isinstance(x, float):
            return {"float": bits(x)}
        if isinstance(x, complex):
            return {"complex": [bits(x.real), bits(x.imag)]}
        if isinstance(x, str):
            return {"str": x}
        if x is None:
            return {"none": True}
        if isinstance(x, tuple):
            out = {"tuple": [self.value(v, [*path, str(i)]) for i, v in enumerate(x)]}
            if hasattr(type(x), "_fields"):
                out["fields"] = list(type(x)._fields)
            return out
        if isinstance(x, list):
            return {"list": [self.value(v, [*path, str(i)]) for i, v in enumerate(x)]}
        if isinstance(x, np.dtype) or (isinstance(x, type) and issubclass(x, np.generic)):
            return {"dtype": np.dtype(x).str}
        raise TypeError(f"{self.case.python}/{self.case.name}: значение {type(x).__name__} пока не поддерживается")


def unwrap(x):
    """Функции передаётся сам вид, а не исходный массив."""
    return x.array if isinstance(x, refcase.View) else x


def run(case, folder, written):
    """Вызов функции: входы пишутся до вызова (функция может их изменить), потом результат или ошибка."""
    w = Writer(folder, case, written)
    entry = {"name": case.name,
             "args": [{"name": a.name, "value": w.value(a.value, [a.name])} for a in case.args],
             "kwargs": {k: w.value(v, [k]) for k, v in case.kwargs.items()},
             "match": case.match}
    func = resolve(case.python)
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        try:
            result = func(*(unwrap(a.value) for a in case.args), **{k: unwrap(v) for k, v in case.kwargs.items()})
            entry["result"] = w.value(result, ["result"])
        except Exception as e:  # noqa: BLE001 — ошибка и есть эталон негативного теста
            entry["error"] = {"type": type(e).__name__, "module": type(e).__module__, "message": str(e)}
    entry["warnings"] = [{"type": c.category.__name__, "message": str(c.message)} for c in caught]
    return entry


def environment():
    simd = np.show_config(mode="dicts")["SIMD Extensions"]
    disabled = set(PARAMS["NPY_DISABLE_CPU_FEATURES"].split()) & set(simd["found"])
    if disabled:
        sys.exit(f"numpy не отключил {sorted(disabled)} — эталоны зависели бы от процессора (DECISIONS.md п. 4)")
    blas = [{"library": os.path.basename(os.path.dirname(x["filepath"])), "version": x["version"],
             "architecture": x["architecture"], "threads": x["num_threads"]}
            for x in threadpoolctl.threadpool_info() if x["internal_api"] == "openblas"]
    return {"python": platform.python_version(), "machine": platform.machine(), "numpy": np.__version__,
            "scipy": scipy.__version__, "numpy_simd": {"baseline": simd["baseline"], "found": simd["found"]},
            "openblas": sorted(blas, key=lambda x: x["library"]), "params": PARAMS}


def write_json(path, data):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")


def flat_values(array):
    """Элементы в порядке C для fixtures.json: целые — числом, bool — true/false, дробные — битами, комплексные — парой."""
    native = np.ascontiguousarray(array, dtype=array.dtype.newbyteorder("=")).reshape(-1)
    kind, size = native.dtype.kind, native.dtype.itemsize
    if kind == "b":
        return [bool(x) for x in native]
    if kind in "iu":
        return [int(x) for x in native]
    part = size // 2 if kind == "c" else size
    raw = [format(int(x), f"0{2 * part}x") for x in native.view(f"u{part}")]
    return raw if kind == "f" else [raw[i:i + 2] for i in range(0, len(raw), 2)]


def write_fixtures(out):
    """Файлы .npy разных видов и версий заголовка с ожидаемым содержимым: проверка чтения .npy в C#."""
    folder = os.path.join(out, "npy-format")
    os.makedirs(folder)
    listing = []
    for name, array, version in fixtures.FIXTURES:
        file = f"{name}.npy"
        with open(os.path.join(folder, file), "wb") as f:
            np.lib.format.write_array(f, array, version=version, allow_pickle=False)
        with open(os.path.join(folder, file), "rb") as f:
            major, minor = np.lib.format.read_magic(f)
        listing.append({"file": file, "descr": np.lib.format.dtype_to_descr(array.dtype), "shape": list(array.shape),
                        "fortran_order": bool(array.flags.f_contiguous and not array.flags.c_contiguous),
                        "version": [major, minor], "values": flat_values(array)})
    write_json(os.path.join(folder, "fixtures.json"), {"fixtures": listing})
    return len(listing)


def main(repo, out):
    if os.path.exists(out) and os.listdir(out):
        sys.exit(f"{out} не пуст: эталоны пишутся в пустой каталог")
    os.makedirs(out, exist_ok=True)
    names = portable_names(repo)
    by_function = {}
    for case in load_cases():
        status = names.get(case.python)
        if status not in ("запланировано", "перенесено"):
            sys.exit(f"{case.python}: в names.csv {'нет такого имени' if status is None else 'статус «' + status + '»'}"
                     " — эталоны только для переносимых имён без псевдонимов")
        by_function.setdefault(case.python, []).append(case)
    total = 0
    for python, cases in sorted(by_function.items()):
        if len({c.name for c in cases}) != len(cases):
            sys.exit(f"{python}: имена кейсов повторяются")
        folder = os.path.join(out, python)
        os.makedirs(folder)
        written = {}
        write_json(os.path.join(folder, "cases.json"), {"python": python, "cases": [run(c, folder, written) for c in cases]})
        total += len(cases)
    count = write_fixtures(out)
    write_json(os.path.join(out, "environment.json"), environment())
    print(f"numpy {np.__version__}, scipy {scipy.__version__}: функций {len(by_function)}, кейсов {total}, "
          f"файлов для проверки .npy {count}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
