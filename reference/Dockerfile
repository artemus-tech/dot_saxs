# Эталонный контейнер: чистые numpy и scipy из PyPI + Reference-LAPACK (netlib) для сверки через ctypes.
# Версии и базовый образ передаёт .github/workflows/env-check.yml; образ закрепляется по digest (DECISIONS.md, п. 3).
ARG PYTHON_IMAGE=python:3.13-slim-trixie@sha256:e48f59cdba69a3cbd9ed2cf0f348d90d45a8d350d98134cf1070bc106ee9ed90
FROM ${PYTHON_IMAGE}

ARG NUMPY_VERSION
ARG SCIPY_VERSION
ARG LAPACK_VERSION
ARG LAPACK_COMMIT
ARG THREADPOOLCTL_VERSION=3.6.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends gfortran cmake make git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# Только официальные колёса из PyPI, без сборки из исходников.
RUN pip install --no-cache-dir --only-binary=:all: \
      "numpy==${NUMPY_VERSION}" "scipy==${SCIPY_VERSION}" "threadpoolctl==${THREADPOOLCTL_VERSION}"

# Reference-LAPACK вместе со своим BLAS: -O2 без сжатия в FMA (DECISIONS.md, п. 8).
# Версию проверяем по коммиту тега: в v3.12.1 константы версии не обновлены, ilaver() возвращает 3.12.0.
RUN git clone -q --depth 1 --branch "v${LAPACK_VERSION}" https://github.com/Reference-LAPACK/lapack /tmp/lapack \
 && test "$(git -C /tmp/lapack rev-parse HEAD)" = "${LAPACK_COMMIT}" \
 && mkdir -p /opt/netlib && git -C /tmp/lapack rev-parse HEAD > /opt/netlib/COMMIT \
 && cmake -S /tmp/lapack -B /tmp/lapack-build -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_Fortran_FLAGS_RELEASE="-O2 -ffp-contract=off" -DBUILD_SHARED_LIBS=ON \
      -DBUILD_TESTING=OFF -DCBLAS=OFF -DLAPACKE=OFF \
      -DCMAKE_INSTALL_PREFIX=/opt/netlib -DCMAKE_INSTALL_LIBDIR=lib \
 && cmake --build /tmp/lapack-build -j"$(nproc)" \
 && cmake --install /tmp/lapack-build \
 && echo /opt/netlib/lib > /etc/ld.so.conf.d/netlib.conf && ldconfig \
 && rm -rf /tmp/lapack /tmp/lapack-build

ENV NUMPY_VERSION=${NUMPY_VERSION} \
    SCIPY_VERSION=${SCIPY_VERSION} \
    LAPACK_VERSION=${LAPACK_VERSION} \
    LAPACK_COMMIT=${LAPACK_COMMIT} \
    NETLIB_LAPACK=/opt/netlib/lib/liblapack.so \
    NETLIB_BLAS=/opt/netlib/lib/libblas.so

COPY check_env.py /opt/reference/check_env.py
CMD ["python", "-I", "/opt/reference/check_env.py"]
