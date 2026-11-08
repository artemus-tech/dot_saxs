# Эталонный контейнер: чистые numpy и scipy из PyPI (только колёса), сверка порта идёт только с ними (DECISIONS.md, п. 8).
# Версии и базовый образ передаёт .github/workflows/env-check.yml; образ закреплён по digest (DECISIONS.md, п. 3).
ARG PYTHON_IMAGE=python:3.13-slim-trixie@sha256:e48f59cdba69a3cbd9ed2cf0f348d90d45a8d350d98134cf1070bc106ee9ed90
FROM ${PYTHON_IMAGE}

ARG NUMPY_VERSION
ARG SCIPY_VERSION
ARG OPENBLAS_VERSION
ARG THREADPOOLCTL_VERSION=3.6.0

RUN pip install --no-cache-dir --only-binary=:all: \
      "numpy==${NUMPY_VERSION}" "scipy==${SCIPY_VERSION}" "threadpoolctl==${THREADPOOLCTL_VERSION}"

ENV NUMPY_VERSION=${NUMPY_VERSION} \
    SCIPY_VERSION=${SCIPY_VERSION} \
    OPENBLAS_VERSION=${OPENBLAS_VERSION}

COPY check_env.py /opt/reference/check_env.py
CMD ["python", "-I", "/opt/reference/check_env.py"]
