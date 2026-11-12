FROM postgres:16-alpine
RUN apk add --no-cache --virtual .build-deps build-base git \
    && git clone --depth 1 --branch v0.8.0 https://github.com/pgvector/pgvector.git /tmp/pgvector \
    && cd /tmp/pgvector \
    && make clean \
    && make OPTFLAGS="" with_llvm=no \
    && make with_llvm=no install \
    && rm -rf /tmp/pgvector \
    && apk del .build-deps
