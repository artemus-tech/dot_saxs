#!/usr/bin/env bash
# Исходники оригиналов на закреплённых коммитах из sources.json:
# numpy и scipy — по тегу с подмодулями, OpenBLAS — по коммиту (тега нет: снимок ветки develop).
# Использование: fetch.sh <каталог>. Падает, если тег указывает не на закреплённый коммит.
set -euo pipefail
dest=${1:?укажите каталог}
root=$(cd "$(dirname "$0")/../.." && pwd)
pin() { jq -r ".$1.$2" "$root/sources.json"; }
mkdir -p "$dest"

for name in numpy scipy; do
  tag=$(pin "$name" tag) commit=$(pin "$name" commit)
  rm -rf "${dest:?}/$name"
  git -c advice.detachedHead=false clone -q --depth 1 --branch "$tag" --recurse-submodules --shallow-submodules \
    "$(pin "$name" repo)" "$dest/$name" 2> "$dest/clone-$name.log" || { tail -n 20 "$dest/clone-$name.log"; exit 1; }
  head=$(git -C "$dest/$name" rev-parse HEAD)
  echo "$name $tag → $head, подмодулей: $(git -C "$dest/$name" submodule status --recursive | wc -l)"
  [ "$head" = "$commit" ] || { echo "ожидался коммит $commit"; exit 1; }
done

commit=$(pin openblas commit)
rm -rf "${dest:?}/openblas" && mkdir -p "$dest/openblas"
git -C "$dest/openblas" init -q
git -C "$dest/openblas" remote add origin "$(pin openblas repo)"
git -C "$dest/openblas" fetch -q --depth 1 origin "$commit" 2> "$dest/clone-openblas.log" \
  || { tail -n 20 "$dest/clone-openblas.log"; exit 1; }
git -C "$dest/openblas" -c advice.detachedHead=false checkout -q FETCH_HEAD
head=$(git -C "$dest/openblas" rev-parse HEAD)
echo "openblas $(pin openblas version) → $head, LAPACK на Фортране: $(find "$dest/openblas/lapack-netlib/SRC" -name '*.f' | wc -l)," \
     "своих реализаций LAPACK: $(find "$dest/openblas/interface/lapack" -name '*.c' | wc -l)"
[ "$head" = "$commit" ] || { echo "ожидался коммит $commit"; exit 1; }
