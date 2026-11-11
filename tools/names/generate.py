#!/usr/bin/env python3
"""Генератор таблицы имён names.csv: публичное имя NumPy/SciPy ↔ имя в C# (SPEC.md п. 2, DECISIONS.md п. 10–11).

Запуск — там, где установлены закреплённые numpy и scipy (эталонный контейнер):
    python -I tools/names/generate.py <корень репозитория> <выходной каталог>

Пишет names.csv (вся таблица) и tokens.csv (как разбита на смысловые части каждая слитная часть имени).
Публичные модули — из тестов самих numpy (PUBLIC_MODULES, PUBLIC_ALIASED_MODULES) и scipy (PUBLIC_MODULES).
Статус «перенесено» — если имя стоит в [Original("…")] в src/**/*.cs; правки руками в names.csv не нужны.
"""
import ast
import builtins
import csv
import functools
import importlib
import inspect
import math
import os
import re
import sys
import types
import warnings
from collections import Counter, defaultdict

warnings.simplefilter("ignore")

import numpy as np  # noqa: E402
import scipy  # noqa: E402
from scipy._lib._public_api import PUBLIC_MODULES as SCIPY_PUBLIC  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- публичные модули

def numpy_module_lists():
    path = os.path.join(os.path.dirname(np.__file__), "tests", "test_public_api.py")
    found = {}
    for node in ast.parse(open(path, encoding="utf-8").read()).body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                name = getattr(target, "id", None)
                if name in ("PUBLIC_MODULES", "PUBLIC_ALIASED_MODULES"):
                    vals = [n.value for n in ast.walk(node.value) if isinstance(n, ast.Constant) and isinstance(n.value, str)]
                    found[name] = [v if v.startswith("numpy.") else "numpy." + v for v in vals if v != "numpy."]
    return ["numpy"] + found["PUBLIC_MODULES"] + found["PUBLIC_ALIASED_MODULES"]


MODULES = numpy_module_lists() + ["scipy"] + list(SCIPY_PUBLIC)
MODULE_ORDER = {m: i for i, m in enumerate(MODULES)}

# ---------------------------------------------------------------- исключения (DECISIONS.md п. 11)

EXCLUDED_MODULES = {
    "numpy.f2py": "генератор обёрток Фортрана для Python",
    "numpy.ctypeslib": "мост к ctypes",
    "numpy.typing": "аннотации типов Python",
    "numpy.typing.mypy_plugin": "плагин mypy",
    "numpy.lib.mixins": "миксины протоколов операторов Python",
    "numpy.testing.overrides": "протокол __array_function__",
    "scipy.datasets": "загрузка данных из интернета",
    "scipy.linalg.cython_blas": "Cython-интерфейс",
    "scipy.linalg.cython_lapack": "Cython-интерфейс",
}
EXCLUDED_NAMES = {
    "scipy.LowLevelCallable": "вызов C-функций из Python",
    "numpy.memmap": "memmap: данные вне управляемого массива",
    "numpy.lib.format.open_memmap": "memmap: данные вне управляемого массива",
    "numpy.ndarray.ctypes": "ctypes",
}
EXCLUDED_WORDS = {"pickle": "pickle", "memmap": "memmap: данные вне управляемого массива", "dump": "pickle", "dumps": "pickle"}

# ---------------------------------------------------------------- словари для разбиения на смысловые части

def read_words(name):
    words = []
    for line in open(os.path.join(HERE, name), encoding="utf-8"):
        words += line.split("#")[0].split()
    return set(words)


JARGON = read_words("jargon.txt")
CODES = read_words("codes.txt")
OVERRIDES = {r["token"]: r["csharp"] for r in csv.DictReader(open(os.path.join(HERE, "overrides.csv"), encoding="utf-8"))}
TWO_JARGON = {"sp", "lu", "qr", "nd", "sf", "io", "py", "dd", "mm"}  # двухбуквенный жаргон: допустим в любой позиции
TWO_ENGLISH = {"is", "as", "to", "at"}  # английские двухбуквенные части: только в начале (isclose, asarray, atleast); to — и в середине
SPECIAL_TWO = {"ln"}  # единственная двухбуквенная часть в scipy.special: gammaln → GammaLn
CASE = {"nd": "ND", "dd": "DD", "mulx": "MulX", "uint": "UInt", "io": "IO"}
OBJECT_MEMBERS = {"ToString", "Equals", "GetHashCode", "GetType", "Finalize", "MemberwiseClone", "ReferenceEquals"}
STRONG = 50  # слово из прозы считается надёжной частью, если встречается не реже
COMMON = 30  # слово из прозы, которое встречается не реже, — обычное английское слово и целиком не делится
NOT_A_LETTER_SUFFIX = set("sdery")  # одиночная буква в конце не отделяется: это окончание слова (strided, validate)


def prose(doc):
    """Проза докстринга без примеров кода, «See Also», текста в кавычках-апострофах и вызовов."""
    doc = re.sub(r"See Also\n-+\n.*?(\n\n[A-Z][a-z]+\n-+\n|\Z)", r"\1", doc, flags=re.S)
    doc = "\n".join(line for line in doc.splitlines() if not line.strip().startswith((">>>", "...")))
    doc = re.sub(r"`[^`]*`", " ", doc)
    doc = re.sub(r"\b[A-Za-z_][\w.]*\(", " ", doc)
    return re.sub(r"\b(np|numpy|scipy|sp)\.[\w.]+", " ", doc)

# ---------------------------------------------------------------- обход публичных имён

FUNCTION_TYPES = (types.FunctionType, types.BuiltinFunctionType, types.MethodType, types.BuiltinMethodType, functools.partial)
FUNCTION_TYPE_NAMES = {"_ArrayFunctionDispatcher", "cython_function_or_method", "fused_cython_function", "method_descriptor",
                       "fortran", "_Function", "_lru_cache_wrapper"}
UFUNC_TYPE_NAMES = {"MultiUFunc", "_MaskedUnaryOperation", "_MaskedBinaryOperation", "_DomainedBinaryOperation", "_extrema_operation"}
BUILTIN_TYPES = {v for v in vars(builtins).values() if isinstance(v, type)}


def kind_of(obj):
    if isinstance(obj, types.ModuleType):
        return "module"
    if isinstance(obj, np.ufunc) or type(obj).__name__ in UFUNC_TYPE_NAMES:
        return "ufunc"
    if isinstance(obj, type):
        if issubclass(obj, Warning):
            return "warning"
        if issubclass(obj, BaseException):
            return "exception"
        return "class"
    if isinstance(obj, FUNCTION_TYPES) or type(obj).__name__ in FUNCTION_TYPE_NAMES:
        return "function"
    if obj is None or isinstance(obj, (bool, int, float, complex, str, bytes, tuple, frozenset, dict, list, np.generic, np.ndarray)):
        return "constant"
    return "object"


def member_kind(raw):
    if isinstance(raw, (property, types.GetSetDescriptorType, types.MemberDescriptorType)):
        return "property"
    if isinstance(raw, staticmethod):
        return "staticmethod"
    if isinstance(raw, (classmethod, types.ClassMethodDescriptorType)):
        return "classmethod"
    if callable(raw) or isinstance(raw, (types.MethodDescriptorType, types.WrapperDescriptorType)):
        return "method"
    return "attribute"


def public_names(module):
    names = None if module.__name__ in ("scipy.linalg.lapack", "scipy.linalg.blas") else getattr(module, "__all__", None)
    names = names or [n for n in dir(module) if not n.startswith("_")]
    return sorted({n for n in names if not n.startswith("_")})


def load(mn):
    try:
        return importlib.import_module(mn)
    except ImportError:
        parent, _, name = mn.rpartition(".")
        return getattr(load(parent), name)


def collect():
    """Все публичные имена: модули, их члены и члены публичных классов."""
    rows, docs = {}, []
    for mn in MODULES:
        module = load(mn)
        rows.setdefault(mn, dict(kind="module", obj=module, container=mn.rpartition(".")[0]))
        docs.append(module.__doc__ or "")
        for name in public_names(module):
            obj = getattr(module, name, None)
            kind = kind_of(obj)
            if type(obj).__name__ == "PytestTester":
                continue  # запуск тестов pytest
            path = f"{mn}.{name}"
            if kind == "module" and path not in MODULE_ORDER:
                continue  # непубличный подмодуль или ссылка на чужой модуль
            rows.setdefault(path, dict(kind=kind, obj=obj, container=mn))
            docs.append(getattr(obj, "__doc__", "") or "" if kind != "constant" else "")
    return rows, docs


# Вызов объекта как функции, f(x), — у интерполяторов SciPy (DECISIONS.md п. 28): в C# это методы
# GetInterpolatedValueAt (за краем точек NaN) и GetExtrapolatedValueAt (продолжает кривую, как f(x) по умолчанию).
CALL_MODULES = ("scipy.interpolate.",)
CALL_NAME = "GetInterpolatedValueAt"
CALL_NOTE = ("за краем точек — NaN, как f(x, extrapolate=False); продолжить кривую за край, как f(x) в SciPy по умолчанию, — "
             "obj.GetExtrapolatedValueAt(x) (DECISIONS.md п. 28)")


def add_members(rows, canonical_classes):
    """Члены публичных классов: свои и унаследованные от непубличных предков (не от встроенных типов Python);
    у интерполяторов ещё вызов __call__."""
    owner_paths = {id(cls): path for path, cls in canonical_classes.items()}
    for path, cls in canonical_classes.items():
        for name in dir(cls):
            if name.startswith("_") and not (name == "__call__" and path.startswith(CALL_MODULES)):
                continue
            owner = next((k for k in cls.__mro__ if name in k.__dict__), None)
            if owner is None or owner in BUILTIN_TYPES:
                continue
            if owner is not cls and id(owner) in owner_paths:
                continue  # член публичного предка — строка у предка
            raw = owner.__dict__[name]
            obj = raw.__func__ if isinstance(raw, (staticmethod, classmethod)) else raw.fget if isinstance(raw, property) else raw
            rows.setdefault(f"{path}.{name}", dict(kind=member_kind(raw), obj=obj, container=path))

# ---------------------------------------------------------------- псевдонимы

def resolve_aliases(rows):
    """Один и тот же объект под несколькими публичными именами. Каноническое — самое короткое; при равной длине —
    совпадающее с __module__ и __name__, затем просто с __name__ (scipy.special.roots_legendre, а не p_roots)."""
    by_id = defaultdict(list)
    for path, row in rows.items():
        if row["kind"] in ("module", "function", "ufunc", "class", "exception", "warning", "object"):
            by_id[id(row["obj"])].append(path)
    for paths in by_id.values():
        obj = rows[paths[0]]["obj"]
        mod, name = getattr(obj, "__module__", None), getattr(obj, "__name__", None)
        own = f"{mod}.{name}" if isinstance(mod, str) and isinstance(name, str) else None
        canon = min(paths, key=lambda p: (p.count("."), p != own, p.rpartition(".")[2] != name,
                                          MODULE_ORDER.get(p.rpartition(".")[0], 10**6), p))
        for p in paths:
            rows[p]["canonical"] = canon

# ---------------------------------------------------------------- имена в C#

class Namer:
    """Деление слитных частей имён на смысловые части (SPEC.md п. 2): argmax → ArgMax, linspace → LinSpace."""

    def __init__(self, words):
        self.words = words
        self.tokens = {}  # часть имени → (csharp, как получено, где встретилась)
        self.conflicts = []  # одна часть имени в разных модулях получила разное написание

    def remember(self, token, text, how, where):
        old = self.tokens.setdefault(token, (text, how, where))
        if old[0] != text:
            self.conflicts.append(f"{token}: {old[0]} ({old[2]}) и {text} ({where})")

    def freq(self, w):
        return self.words.get(w, 0)

    def known(self, w):
        return w in JARGON or w in CODES or (len(w) >= 3 and self.freq(w) >= 3) or w in TWO_JARGON or w in TWO_ENGLISH

    def strict_known(self, w):
        """Допустимая часть имени scipy.special: известное слово не короче трёх букв или ln; коды — только целиком."""
        return w not in CODES and (w in SPECIAL_TWO or (len(w) >= 3 and self.known(w)))

    def strong(self, w):
        return w in JARGON or w in TWO_JARGON or w in SPECIAL_TWO or self.freq(w) >= STRONG

    def allowed(self, w, i):
        return len(w) >= 3 or w in TWO_JARGON or (w in TWO_ENGLISH and (i == 0 or w == "to"))

    def cost(self, head, rest_cost, strict):
        if self.strict_known(head) if strict else self.known(head):
            return (rest_cost[0], rest_cost[1] + 1, rest_cost[2] - math.log(1 + self.freq(head) + (1000 if head in JARGON else 0)))
        return (rest_cost[0] + len(head), rest_cost[1] + 1, rest_cost[2])

    @functools.lru_cache(maxsize=None)
    def split(self, s, strict=False):
        """Лучшее разбиение строки: минимум неизвестных букв, затем частей, затем редких слов.
        strict — правила scipy.special: годятся только части из strict_known."""
        if not s:
            return (0, 0, 0.0), ()
        best = None
        for i in range(1, len(s) + 1):
            rest_cost, tail = self.split(s[i:], strict)
            c = self.cost(s[:i], rest_cost, strict)
            if best is None or c < best[0]:
                best = (c, (s[:i],) + tail)
        return best

    def split_in_parts(self, s, strict=False):
        """Лучшее разбиение минимум на две части."""
        best = None
        for i in range(1, len(s)):
            rest_cost, tail = self.split(s[i:], strict)
            c = self.cost(s[:i], rest_cost, strict)
            if best is None or c < best[0]:
                best = (c, (s[:i],) + tail)
        return best

    def good(self, words, need_strong):
        return all(self.known(w) and self.allowed(w, i) and (self.strong(w) or not need_strong) for i, w in enumerate(words))

    def edge_letter(self, words):
        """Неизвестна ровно одна буква — с краю (hstack → H + Stack), и это не окончание слова; остальное надёжно."""
        unknown = [i for i, w in enumerate(words) if not self.known(w)]
        if len(words) < 2 or len(unknown) != 1:
            return False
        i = unknown[0]
        if len(words[i]) != 1 or i not in (0, len(words) - 1) or (i > 0 and words[i] in NOT_A_LETTER_SUFFIX):
            return False
        rest = words[:i] + words[i + 1:]
        return all(self.strong(w) and (w in JARGON or w in TWO_JARGON or len(w) >= 4) for w in rest)

    def words_of(self, token):
        """Части слитного слова и как они получены."""
        known = self.known(token)
        if known and (self.freq(token) >= COMMON or token in CODES or token in JARGON or len(token) <= 3):
            return (token,), "whole"  # обычное слово, код или неделимая часть из jargon.txt
        cand = self.split_in_parts(token) if known else self.split(token)
        if cand and len(cand[1]) > 1:
            (unknown, _, _), words = cand
            if unknown == 0 and self.good(words, need_strong=known):
                return words, "split"
            if self.edge_letter(words):
                return words, "split-letter"
        return (token,), ("whole" if known else "unknown-long" if len(token) >= 7 else "unknown")

    def render(self, token, where):
        """Слитная часть имени из строчных букв → PascalCase; вложенные слитные части делятся дальше (flatnonzero)."""
        if token in OVERRIDES:
            return OVERRIDES[token], "override"
        if token in CODES:
            return token.capitalize(), "code"
        words, how = self.words_of(token)
        if len(words) == 1:
            text = CASE.get(token, token.capitalize())
        else:
            text = "".join(OVERRIDES.get(w) or CASE.get(w) or (self.render(w, where)[0] if self.known(w) else w.upper() if len(w) == 1 else w.capitalize())
                           for w in words)
        self.remember(token, text, how, where)
        return text, how

    def special(self, part, where):
        """Часть имени из scipy.special (DECISIONS.md п. 11). Слова делятся, как везде (logsumexp → LogSumExp), но строже:
        каждая часть — известное слово не короче трёх букв (кроме ln), буква с краю не отделяется. Коды Cephes, части
        с цифрами и части, которые не делятся на известные слова, — одним словом: ndtri → Ndtri, hyp2f1 → Hyp2f1."""
        if part in OVERRIDES:
            text, how = OVERRIDES[part], "override"
        elif part in CODES:
            text, how = part.capitalize(), "code"
        elif not part.isalpha():
            text, how = part[:1].upper() + part[1:], "digits"
        else:
            known = self.known(part) or part in SPECIAL_TWO
            words, how = (part,), ("whole" if known else "unknown")
            if not (known and (self.freq(part) >= COMMON or part in JARGON or len(part) <= 3)):
                cand = self.split_in_parts(part, strict=True) if known else self.split(part, strict=True)
                if cand and len(cand[1]) > 1 and cand[0][0] == 0 and all(self.strong(w) or not known for w in cand[1]):
                    words, how = cand[1], "split"
            # слитная часть внутри делится по тем же правилам: betaincinv → betainc + inv → BetaInc + Inv
            text = CASE.get(part, part.capitalize()) if len(words) == 1 else "".join(self.special(w, where)[0] for w in words)
        self.remember(part, text, how, where)
        return text, how

    def pascal(self, name, context, where):
        """Имя Python → имя в C# (SPEC.md п. 2). Хвостовое подчёркивание сохраняется: str_ → Str_."""
        trailing = "_" if name.endswith("_") and not name.endswith("__") else ""
        core = name.rstrip("_") if trailing else name
        if context == "original":
            return name, "original"
        if context == "code":
            return core[:1].upper() + core[1:] + trailing, "code"
        parts, hows = [], []
        for part in [p for p in core.split("_") if p]:
            if any(c.isupper() for c in part):
                parts.append(part[:1].upper() + part[1:]); hows.append("as-is")
                continue
            if context == "special":
                text, how = self.special(part, where)
                parts.append(text); hows.append(how)
                continue
            if part in OVERRIDES:
                parts.append(OVERRIDES[part]); hows.append("override"); self.remember(part, OVERRIDES[part], "override", where)
                continue
            for chunk in re.findall(r"[a-z]+|[0-9]+", part):
                if chunk.isdigit():
                    parts.append(chunk); continue
                if parts and parts[-1].isdigit() and chunk == "d":
                    parts.append("D"); continue  # 1d → 1D
                text, how = self.render(chunk, where)
                parts.append(text); hows.append(how)
        return "".join(parts) + trailing, ",".join(sorted(set(hows)))


def context_of(path, row):
    container = row["container"]
    if container == "scipy.special":
        return "special"
    if container in ("scipy.linalg.lapack", "scipy.linalg.blas") and row["kind"] != "class" and "_" not in path.rpartition(".")[2]:
        return "code"
    if container == "scipy.constants" and row["kind"] == "constant":
        return "original"
    return "default"


def assign_csharp(rows, namer):
    canon = {p: r for p, r in rows.items() if r.get("canonical", p) == p}
    short, how = {}, {}
    for path, row in canon.items():
        if path in ("numpy", "scipy"):
            short[path], how[path] = ("Np", "root") if path == "numpy" else ("SciPy", "root")
        elif path.endswith(".__call__"):
            short[path], how[path] = CALL_NAME, "decision"
        else:
            short[path], how[path] = namer.pascal(path.rpartition(".")[2], context_of(path, row), path)
    notes = defaultdict(list)
    siblings = defaultdict(list)
    for path, row in canon.items():
        if path not in ("numpy", "scipy"):
            siblings[canonical_container(rows, row["container"])].append(path)
    for container, members in siblings.items():
        container_short = short.get(container)
        by_name = defaultdict(list)
        for p in members:
            by_name[short[p]].append(p)
        for name, group in by_name.items():
            if len(group) < 2:
                continue
            is_class = lambda q: canon[q]["kind"] in ("class", "exception", "warning")
            group.sort(key=lambda q: (not is_class(q), not q.rpartition(".")[2][:1].isupper(), q))
            keeper, extra = group[0], 0
            for p in group[1:]:
                if canon[p]["kind"] == "module" and is_class(keeper):
                    short[p] += "Module"; notes[p].append("модуль совпал с классом → Module (DECISIONS.md п. 10)")
                    continue
                extra += 1
                short[p] += "_" * extra
                notes[p].append(("совпало с классом" if is_class(keeper) else f"совпало с {keeper}") + " → _ (DECISIONS.md п. 10)")
        for p in members:
            if short[p] == container_short:
                short[p] += "_"; notes[p].append("совпало с именем контейнера, CS0542 → _ (DECISIONS.md п. 10)")
            if short[p] in OBJECT_MEMBERS:
                short[p] += "_"; notes[p].append("совпало с членом object → _")

    @functools.lru_cache(maxsize=None)
    def full(path):
        container = canonical_container(rows, canon[path]["container"]) if path not in ("numpy", "scipy") else ""
        return (full(container) + "." if container else "") + short[path]

    for path, row in rows.items():
        c = row.get("canonical", path)
        row["csharp"] = full(c)
        row["how"] = how.get(c, "")
        row["notes"] = (notes.get(c, []) + (["разбиение на части: проверить"] if "unknown-long" in how.get(c, "") else [])) \
            if c == path else [f"= {c}"]


def canonical_container(rows, container):
    return rows[container].get("canonical", container) if container in rows else container

# ---------------------------------------------------------------- место определения, статус

def repo_relative(path):
    path = os.path.realpath(path)
    for pkg in (np, scipy):
        base = os.path.dirname(os.path.realpath(pkg.__file__))
        if path.startswith(base + os.sep):
            return pkg.__name__ + "/" + os.path.relpath(path, base).replace(os.sep, "/")
    return ""


GENERATED = {"numpy/__config__.py", "numpy/version.py", "scipy/__config__.py", "scipy/version.py"}  # создаются при сборке


def installed(file):
    pkg = np if file.startswith("numpy/") else scipy
    return os.path.join(os.path.dirname(os.path.dirname(os.path.realpath(pkg.__file__))), file)


def location(row, path):
    """Файл и строка определения; строка обязана содержать имя (иначе — поиск def/class/присваивания или пусто)."""
    file, line = raw_location(row, path)
    if not file or file in GENERATED:
        return "", ""
    short = path.rpartition(".")[2]
    lines = open(installed(file), encoding="utf-8").read().splitlines()
    if 0 < line <= len(lines) and short.lower() in lines[line - 1].lower():
        return file, line
    pattern = re.compile(rf"^\s*(?:async\s+)?(?:def|class)\s+{re.escape(short)}\b|^\s*{re.escape(short)}\s*=")
    for i, text in enumerate(lines, 1):
        if pattern.search(text):
            return file, i
    if row["kind"] == "module" and "." in path:
        parent = repo_relative(load(path.rpartition(".")[0]).__file__)
        for i, text in enumerate(open(installed(parent), encoding="utf-8").read().splitlines(), 1):
            if re.search(rf"\b{re.escape(short)}\b", text):
                return parent, i
    return "", ""


def raw_location(row, path):
    obj = row["obj"]
    if row["kind"] == "module":
        file = repo_relative(getattr(obj, "__file__", "") or "")
        if not file.endswith(".py"):
            return "", ""
        short = path.rpartition(".")[2].lower()
        for i, line in enumerate(open(obj.__file__, encoding="utf-8").read().splitlines()[:80], 1):
            if short in line.lower():
                return file, i
        return file, 1
    target = getattr(obj, "_implementation", obj)
    try:
        target = inspect.unwrap(target)
    except Exception:
        pass
    if not isinstance(target, (types.FunctionType, type)):
        return "", ""
    try:
        file, (lines, start) = inspect.getsourcefile(target), inspect.getsourcelines(target)
    except (TypeError, OSError):
        return "", ""
    name = target.__name__
    for i, line in enumerate(lines[:80]):
        if re.match(rf"\s*(async\s+)?(def|class)\s+{re.escape(name)}\b", line):
            return repo_relative(file), start + i
    return repo_relative(file), start


def excluded(path):
    for module, why in EXCLUDED_MODULES.items():
        if path == module or path.startswith(module + "."):
            return why
    if path in EXCLUDED_NAMES:
        return EXCLUDED_NAMES[path]
    tail = path.rpartition(".")[2]
    for word, why in EXCLUDED_WORDS.items():
        if word in tail.split("_") or (word in ("pickle", "memmap") and word in tail):
            return why
    return None


# ---------------------------------------------------------------- как это делается у нас (DECISIONS.md п. 22–26)

HOWS = ["функция числа", "уже есть в .NET", "LINQ", "метод", "метод объекта", "свойство объекта", "создание", "форма",
        "функция", "класс", "объект", "константа", "вид чисел", "исключение", "предупреждение", "модуль", "формат файла",
        "внутри", "не переносится", "решить"]
MEMBER_KINDS = {"method", "property", "attribute", "classmethod", "staticmethod"}


def read_placement():
    """Ручные решения placement.csv: точное имя или «префикс.*» — всё, что внутри; этап — если указан."""
    exact, prefix = {}, []
    for r in csv.DictReader(open(os.path.join(HERE, "placement.csv"), encoding="utf-8")):
        if r["how"] not in HOWS:
            sys.exit(f"placement.csv: неизвестный способ «{r['how']}» у {r['python']}")
        if r["stage"] not in ("", *map(str, range(1, 9))):
            sys.exit(f"placement.csv: этап «{r['stage']}» у {r['python']} — нужен номер от 1 до 8 или пусто")
        item = (r["how"], r["call"], r["note"], int(r["stage"]) if r["stage"] else None)
        if r["python"].endswith(".*"):
            prefix.append((r["python"][:-2], item))
        else:
            exact[r["python"]] = item
    prefix.sort(key=lambda t: -len(t[0]))
    return exact, prefix


PLACEMENT, PLACEMENT_PREFIX = read_placement()
P24, P25 = "DECISIONS.md п. 24", "DECISIONS.md п. 25"
REDUCTIONS = {"max", "min", "mean", "median", "std", "var", "sum", "prod", "ptp", "percentile", "quantile", "nanmax", "nanmin",
              "nanmean", "nanmedian", "nanstd", "nanvar", "nansum", "nanprod", "nanpercentile", "nanquantile"}
# Поэлементные функции NumPy, которые в C# пишутся обычным выражением внутри Select: результат тот же до бита.
LINQ_UFUNCS = {"add": "x + y", "subtract": "x - y", "multiply": "x * y", "divide": "x / y", "true_divide": "x / y",
               "negative": "-x", "positive": "+x", "square": "x * x", "greater": "x > y",
               "greater_equal": "x >= y", "less": "x < y", "less_equal": "x <= y", "equal": "x == y", "not_equal": "x != y",
               "logical_and": "x && y", "logical_or": "x || y", "logical_xor": "x ^ y", "logical_not": "!x",
               "bitwise_and": "x & y", "bitwise_or": "x | y", "bitwise_xor": "x ^ y", "invert": "~x", "bitwise_not": "~x",
               "bitwise_invert": "~x"}
# Не LINQ, хотя похоже: reciprocal (у целых 1 / 0 — исключение в C#), сдвиги (на 64 и больше у NumPy 0 или −1, в C# —
# сдвиг на y % 64), absolute (Math.Abs(long.MinValue) — исключение). Это функции чисел, примечания — в placement.csv.
# Поэлементные функции NumPy, у которых в .NET есть точный двойник (точные по IEEE 754).
DOTNET_UFUNCS = {"isnan": "double.IsNaN", "isinf": "double.IsInfinity", "isfinite": "double.IsFinite",
                 "signbit": "double.IsNegative", "fabs": "Math.Abs", "floor": "Math.Floor",
                 "ceil": "Math.Ceiling", "trunc": "Math.Truncate", "rint": "Math.Round", "sqrt": "Math.Sqrt",
                 "copysign": "Math.CopySign", "conjugate": "Complex.Conjugate", "conj": "Complex.Conjugate"}
FFT_2D = {"fft2", "ifft2", "rfft2", "irfft2", "hfft2", "ihfft2"}
FFT_ND = {"fftn", "ifftn", "rfftn", "irfftn", "hfftn", "ihfftn", "dctn", "idctn", "dstn", "idstn"}
ALONG_LINE_SUBPACKAGES = {"stats", "signal", "integrate", "special"}  # функции с axis здесь — функции вдоль линии (Vector)


def has_axis(obj):
    try:
        params = inspect.signature(obj).parameters
    except (TypeError, ValueError):
        return False
    return "axis" in params or "axes" in params


def is_distribution(obj):
    return any(k.__name__ in ("rv_generic", "multi_rv_generic", "multi_rv_frozen") for k in type(obj).__mro__)


def distribution_placement(obj, cs):
    """Распределение: пример — первый из Pdf, Pmf (у дискретных), Cdf, Ppf, Rvs, который есть у объекта."""
    methods = [m.capitalize() for m in ("pdf", "pmf", "cdf", "ppf", "rvs") if callable(getattr(obj, m, None))] or ["Rvs"]
    arg = "(…)" if methods[0] == "Rvs" else "(x)"
    return "объект", f"{cs}.{methods[0]}{arg}", f"распределение: {', '.join(methods)}…"


def ufunc_placement(path, obj, cs):
    tail = path.rpartition(".")[2]
    if path.startswith("numpy.") and tail in LINQ_UFUNCS:
        expr = LINQ_UFUNCS[tail]
        if "y" in expr:
            return "LINQ", f"v.Select((x, i) => {expr.replace('y', 'w[i]')})", f"операторов у массивов нет ({P24})"
        return "LINQ", f"v.Select(x => {expr})", f"операторов у массивов нет ({P24})"
    if path.startswith("numpy.") and tail in DOTNET_UFUNCS:
        return "уже есть в .NET", f"v.Select({DOTNET_UFUNCS[tail]})", "результат тот же, что у NumPy"
    if getattr(obj, "nin", 1) == 1:
        return "функция числа", f"v.Select({cs})", ""
    return "функция числа", f"{cs}(…)", "к массиву — через Select"


def placement(path, row, rows, placed):
    """Как это делается у нас: (способ, вызов, примечание, этап или None — тогда этап по правилам stage())."""
    cs, kind = row["csharp"], row["kind"]
    name = cs.rpartition(".")[2].rstrip("_") or cs
    tail = path.rpartition(".")[2]

    def fill(item):
        how, call, note, st = item
        return how, call.replace("{name}", name).replace("{cs}", cs), note.replace("{name}", name).replace("{cs}", cs), st

    if path in PLACEMENT:
        return fill(PLACEMENT[path])
    for prefix, item in PLACEMENT_PREFIX:
        if path.startswith(prefix + "."):
            return fill(item)
    if kind in MEMBER_KINDS and tail == "__call__":
        owner = placed.get(row["container"])
        if owner is not None and owner[0] in ("не переносится", "решить"):
            return owner[0], "", owner[2], owner[3] or None
        return "метод объекта", f"obj.{CALL_NAME}(x)", CALL_NOTE, (owner[3] or None) if owner else None
    if kind in MEMBER_KINDS:
        return member_placement(row, rows, placed, kind, name)
    return (*automatic_placement(path, row, cs, kind, name, tail), None)


def member_placement(row, rows, placed, kind, name):
    """Член класса — как класс и на том же этапе."""
    owner = placed.get(row["container"])
    if owner is None:
        return "решить", "", "", None
    st = owner[3] or None
    if owner[0] in ("класс", "объект", "формат файла"):
        target = rows[row["container"]]["csharp"] if kind in ("classmethod", "staticmethod") or owner[0] == "объект" else "obj"
        if kind in ("property", "attribute"):
            return "свойство объекта", f"{target}.{name}", "", st
        return "метод объекта", f"{target}.{name}(…)", "", st
    return owner[0], "", owner[2], st


def automatic_placement(path, row, cs, kind, name, tail):
    """Правила для всего, чего нет в placement.csv: (способ, вызов, примечание)."""
    if kind == "module":
        return "модуль", cs, ""
    if kind == "exception":
        return "исключение", f"catch ({name} e)", ""
    if kind == "warning":
        return "предупреждение", "", ""
    if path.startswith(("scipy.linalg.blas.", "scipy.linalg.lapack.")):
        return "внутри", "", f"наружу — методы линейной алгебры ({P25})"
    if kind == "ufunc":
        return ufunc_placement(path, row["obj"], cs)
    if kind == "class":
        if path.startswith("scipy.io."):
            return "формат файла", f"new {name}(path)", f"по сборке на формат ({P24})"
        return "класс", f"new {name}(…)", ""
    if kind == "constant":
        return "константа", cs, ""
    if kind == "object":
        if is_distribution(row["obj"]):
            return distribution_placement(row["obj"], cs)
        return "решить", "", ""
    parts = path.split(".")
    sub = parts[1] if len(parts) > 2 else ""
    if parts[0] == "numpy" and len(parts) == 2 and tail in REDUCTIONS:
        return "метод", f"a.{name}()", f"у всех наших типов, результат как в NumPy ({P24})"
    if parts[0] == "numpy" and sub == "random" and tail != "default_rng":
        return "решить", "", "старый интерфейс случайных чисел; NumPy советует Generator"
    if parts[0] == "numpy" and sub == "random":
        return "создание", f"{cs}(42)", ""
    if sub == "fft":
        if tail in FFT_2D:
            return "метод", f"m.{name}()", ""
        if tail in FFT_ND:
            return "метод", f"a.{name}()", "у всех наших типов"
        if tail in ("fftfreq", "rfftfreq"):
            return "создание", f"{cs}(n, d)", ""
        if has_axis(row["obj"]):
            return "метод", f"v.{name}()", f"для матрицы — m.GetRows().Select(r => r.{name}())"
        return "функция", f"{cs}(…)", ""
    if parts[0] == "numpy" and sub == "linalg":
        return "метод", f"m.{name}(…)", f"линейная алгебра — методы Matrix ({P25})"
    if parts[0] == "scipy" and sub == "linalg":
        return "метод", f"m.{name}(…)", f"линейная алгебра — методы Matrix ({P25})"
    if parts[0] == "scipy" and sub == "io":
        return "формат файла", f"{cs}(path)", f"по сборке на формат ({P24})"
    if parts[0] == "scipy" and sub == "ndimage":
        if tail.endswith("1d"):
            return "метод", f"v.{name}(…)", ""
        if tail in ("generate_binary_structure", "iterate_structure"):
            return "создание", f"{cs}(…)", ""
        return "метод", f"m.{name}(…)", "и у CubeMatrix"
    if parts[0] == "scipy" and sub in ALONG_LINE_SUBPACKAGES and has_axis(row["obj"]):
        return "метод", f"v.{name}(…)", f"функция вдоль линии; для матрицы — m.GetRows().Select(r => r.{name}(…)) ({P24})"
    return "функция", f"{cs}(…)", ""


# Этапы плана (DECISIONS.md п. 25). Этап 2 — ядро и типы: работа с формой, создание массивов, функции чисел, свои
# Sum, Mean, Min, Max, Std, Var, Median, Sort, запись и чтение .npy, .npz и текста. Этап 3 — BLAS, наружу LinAlgMultiply.
STAGE2_HOWS = {"форма", "создание", "вид чисел", "LINQ", "уже есть в .NET", "функция числа"}
STAGE2_METHODS = {"sum", "prod", "mean", "median", "min", "max", "amin", "amax", "std", "var", "sort", "copy"}
DOT_PRODUCTS = {"dot", "vdot", "inner", "outer", "matmul", "matvec", "vecmat", "vecdot"}
NUMPY_STAGE5 = {"fft", "random", "polynomial", "ma", "strings", "char", "rec", "testing", "version"}


def stage(path, file, how):
    """Этап плана (DECISIONS.md п. 25), если в placement.csv он не указан и имя не член класса."""
    if path == "scipy" or path.startswith("scipy."):
        return 3 if path.startswith("scipy.linalg.blas") else 4 if path.startswith("scipy.linalg") else 6
    parts = path.split(".")
    sub, tail = (parts[1] if len(parts) > 1 else ""), parts[-1]
    if sub in NUMPY_STAGE5:
        return 5
    if how in STAGE2_HOWS or (how == "метод" and tail in STAGE2_METHODS):
        return 2
    if how == "метод" and tail in DOT_PRODUCTS:
        return 3
    if sub == "linalg":
        return 4
    if sub in ("lib", "emath") or str(file).startswith("numpy/lib/"):
        return 5
    return 2


def place_all(rows, file_of):
    """Способ и этап для каждой строки: сначала классы и функции, потом их члены (этап — как у класса),
    псевдонимы — как у канонического имени."""
    placed = {}
    canon = [p for p in sorted(rows) if rows[p].get("canonical", p) == p]
    for members in (False, True):
        for p in canon:
            if (rows[p]["kind"] in MEMBER_KINDS) == members:
                if excluded(p):
                    placed[p] = ("не переносится", "", "", "")
                    continue
                how, call, note, st = placement(p, rows[p], rows, placed)
                if how == "не переносится":
                    st = ""
                elif st is None:
                    st = stage(p, file_of(p), how)
                placed[p] = (how, call, note, st)
    for p in rows:
        placed.setdefault(p, placed.get(rows[p].get("canonical"), ("решить", "", "", "")))
    return placed


def ported_names(repo):
    names = set()
    for base, _, files in os.walk(os.path.join(repo, "src")):
        for f in files:
            if f.endswith(".cs"):
                names |= set(re.findall(r'\[Original\(\s*"([^"]+)"', open(os.path.join(base, f), encoding="utf-8").read()))
    return names

# ---------------------------------------------------------------- запуск

def main(repo, out):
    rows, docs = collect()
    resolve_aliases(rows)
    classes = {p: r["obj"] for p, r in rows.items() if r.get("canonical", p) == p and r["kind"] in ("class", "exception", "warning")}
    add_members(rows, classes)
    words = Counter(w.lower() for d in docs for w in re.findall(r"\b[A-Za-z]{2,}\b(?!')", prose(d))
                    if w.islower() or (w[0].isupper() and w[1:].islower()))
    namer = Namer(words)
    assign_csharp(rows, namer)
    if namer.conflicts:
        sys.exit("Одна и та же часть имени записана по-разному:\n" + "\n".join(namer.conflicts))
    ported = ported_names(repo)
    where = {p: location(r, p) for p, r in rows.items() if r.get("canonical", p) == p and r["obj"] is not None}
    placed = place_all(rows, lambda p: where.get(p, ("", ""))[0])
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "names.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["python", "kind", "csharp", "status", "stage", "how", "call", "file", "line", "note"])
        for path in sorted(rows):
            row = rows[path]
            why = excluded(path)
            alias = row.get("canonical", path) != path
            how, call, how_note, st = placed[path]
            dropped = how == "не переносится"
            status = ("псевдоним" if alias else "не переносится" if dropped or why else "перенесено" if path in ported
                      else "запланировано")
            file, line = ("", "") if alias else where.get(path, ("", ""))
            generated = not alias and row["obj"] is not None and raw_location(row, path)[0] in GENERATED
            notes = ([why] if why else []) + row["notes"] + ([how_note] if how_note and not alias else []) + \
                (["определено в файле, который создаётся при сборке пакета"] if generated else [])
            w.writerow([path, row["kind"], row["csharp"], status, "" if dropped or why else st, how, call,
                        file, line, "; ".join(notes)])
    with open(os.path.join(out, "tokens.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["token", "csharp", "how", "example"])
        for token in sorted(namer.tokens):
            w.writerow([token, *namer.tokens[token]])
    print(f"numpy {np.__version__}, scipy {scipy.__version__}: имён {len(rows)}, частей {len(namer.tokens)}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
