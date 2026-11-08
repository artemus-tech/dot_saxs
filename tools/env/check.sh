#!/usr/bin/env bash
# Проверка окружения по п. 14 SPEC.md (DECISIONS.md, п. 2): запускается в GitHub Actions на ubuntu-24.04, x86-64.
# Разделы выполняются независимо; в конце — итог по разделам и код выхода 1, если хоть один упал.
set -uo pipefail
: "${NUMPY_VERSION:?}" "${SCIPY_VERSION:?}" "${LAPACK_VERSION:?}" "${NUMPY_COMMIT:?}" "${SCIPY_COMMIT:?}" "${LAPACK_COMMIT:?}"
: "${PYTHON_IMAGE:?}" "${POSTGRES_IMAGE:?}" "${POSTGRES_URL:?}"

root=$(cd "$(dirname "$0")/../.." && pwd)
work=${RUNNER_TEMP:-/tmp}/env-check
rm -rf "$work" && mkdir -p "$work"
sdk=$(jq -r .sdk.version "$root/global.json")
results=()

section() {  # section "Название" функция; вызов не внутри if, иначе bash игнорирует set -e в функции
  local name=$1 t0=$SECONDS rc
  echo; echo "=== $name"
  "$2"; rc=$?
  if [ "$rc" -eq 0 ]; then results+=("OK    $name ($((SECONDS - t0)) с)"); else results+=("FAIL  $name ($((SECONDS - t0)) с, код $rc)"); fi
}

runner() {
  uname -srm &&
  lscpu | grep -E '^(Model name|CPU\(s\)|Thread\(s\) per core):' &&
  echo "флаги: $(grep -o -w -E 'avx2|fma|avx512f|avx512bw|avx512vl' /proc/cpuinfo | sort -u | tr '\n' ' ')" &&
  free -g | awk '/^Mem/ {print "память: " $2 " ГБ"}' &&
  docker version --format 'docker: клиент {{.Client.Version}}, сервер {{.Server.Version}}'
}

dotnet_smoke() (  # пустое решение + один тест, предупреждения = ошибки
  set -e
  mkdir -p "$work/smoke" && cp "$root/global.json" "$work/smoke/" && cd "$work/smoke"
  v=$(dotnet --version)
  echo "SDK: $v (global.json: $sdk)"
  [ "$v" = "$sdk" ] || { echo "ожидался SDK $sdk"; exit 1; }
  dotnet --list-runtimes | grep -E '^Microsoft\.NETCore\.App 10\.'
  dotnet --info | awk '/^Host:/ {f = 1; next} f && /Version:/ {print "хост (рантайм по умолчанию): " $2; exit}'
  dotnet new sln -n Smoke > /dev/null
  dotnet new xunit -n Smoke.Tests -o Smoke.Tests > /dev/null
  dotnet sln add Smoke.Tests/Smoke.Tests.csproj > /dev/null
  dotnet build -c Release -warnaserror -p:TreatWarningsAsErrors=true -nologo -v q
  dotnet test -c Release --no-build -nologo -v q
)

sources() (  # исходники на зафиксированных тегах, с подмодулями; тег должен указывать на закреплённый коммит
  set -e
  for spec in "numpy https://github.com/numpy/numpy v$NUMPY_VERSION $NUMPY_COMMIT" \
              "scipy https://github.com/scipy/scipy v$SCIPY_VERSION $SCIPY_COMMIT" \
              "lapack https://github.com/Reference-LAPACK/lapack v$LAPACK_VERSION $LAPACK_COMMIT"; do
    read -r name url tag commit <<< "$spec"
    git -c advice.detachedHead=false clone -q --depth 1 --branch "$tag" --recurse-submodules --shallow-submodules \
      "$url" "$work/src/$name" 2> "$work/clone-$name.log" || { tail -n 20 "$work/clone-$name.log"; exit 1; }
    head=$(git -C "$work/src/$name" rev-parse HEAD)
    echo "$name $tag → $head, подмодулей: $(git -C "$work/src/$name" submodule status --recursive | wc -l)"
    [ "$head" = "$commit" ] || { echo "ожидался коммит $commit"; exit 1; }
  done
)

postgres() { psql "$POSTGRES_URL" -tAc 'select version()'; }

reference() (  # эталонный контейнер: чистые numpy/scipy + Reference-LAPACK
  set -e
  docker pull -q "$PYTHON_IMAGE" > /dev/null
  docker build --progress=plain -t dot-saxs-reference \
    --build-arg PYTHON_IMAGE="$PYTHON_IMAGE" --build-arg NUMPY_VERSION="$NUMPY_VERSION" \
    --build-arg SCIPY_VERSION="$SCIPY_VERSION" --build-arg LAPACK_VERSION="$LAPACK_VERSION" \
    --build-arg LAPACK_COMMIT="$LAPACK_COMMIT" \
    "$root/reference" > "$work/docker-build.log" 2>&1 || { tail -n 60 "$work/docker-build.log"; exit 1; }
  echo "--- параметры по умолчанию"
  docker run --rm dot-saxs-reference
  echo "--- параметры эталона (DECISIONS.md, п. 4)"
  docker run --rm -e NPY_DISABLE_CPU_FEATURES="X86_V4 AVX512_ICL AVX512_SPR" \
    -e OPENBLAS_CORETYPE=Haswell -e OPENBLAS_NUM_THREADS=1 dot-saxs-reference
)

images() {  # образы закреплены по digest: скачан именно он, и Postgres запущен из него
  local img ref d c running="" rc=0
  for img in "$PYTHON_IMAGE" "$POSTGRES_IMAGE"; do
    ref="${img%%[:@]*}@${img##*@}"  # имя:тег@sha256:… → имя@sha256:…
    d=$(docker image inspect --format '{{join .RepoDigests " "}}' "$ref" 2> /dev/null) || d=""
    if [[ $img == *@sha256:* && " $d " == *" $ref "* ]]; then echo "закреплён и скачан: $img"
    else echo "не закреплён или не скачан: $img (найдено: ${d:-ничего})"; rc=1; fi
  done
  for c in $(docker ps -q); do  # docker ps показывает тег без digest, поэтому сверяем по ID образа
    running+=" $(docker image inspect --format '{{join .RepoDigests " "}}' "$(docker inspect --format '{{.Image}}' "$c")")"
  done
  ref="${POSTGRES_IMAGE%%[:@]*}@${POSTGRES_IMAGE##*@}"
  if [[ " $running " == *" $ref "* ]]; then echo "контейнер Postgres запущен из $ref"
  else echo "контейнер Postgres запущен не из закреплённого образа:$running"; rc=1; fi
  return "$rc"
}

section "Раннер" runner
section ".NET: пустое решение и один тест" dotnet_smoke
section "Исходники на тегах" sources
section "PostgreSQL" postgres
section "Эталонный контейнер" reference
section "Digest образов" images

echo; echo "=== Итог"
printf '%s\n' "${results[@]}"
fails=$(printf '%s\n' "${results[@]}" | grep -c '^FAIL')
[ "$fails" -eq 0 ]
