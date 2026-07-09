# syntax=docker/dockerfile:1

# builder image: headless image をどこからでもビルドできる汎用ツール。
# Resonite バイナリを含まないので public 配布できる。
# 実行 IF は builder/entrypoint.sh 冒頭のコメントおよび docs/build-image.md を参照。

# DepotDownloader (self-contained .NET バイナリ) をビルド時に取得して焼き込む。
# 実行時に GitHub Releases へ依存しないようにするため。
FROM debian:12-slim AS depotdownloader
ARG TARGETARCH
ARG DEPOT_DOWNLOADER_VERSION=3.4.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget unzip \
    && rm -rf /var/lib/apt/lists/*
RUN case "${TARGETARCH}" in \
      amd64) DD_ARCH=x64 ;; \
      arm64) DD_ARCH=arm64 ;; \
      *) echo "unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && wget -q -O /tmp/depotdownloader.zip "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_${DEPOT_DOWNLOADER_VERSION}/DepotDownloader-linux-${DD_ARCH}.zip" \
    && unzip -o /tmp/depotdownloader.zip -d /opt/depotdownloader \
    && chmod +x /opt/depotdownloader/DepotDownloader \
    && rm /tmp/depotdownloader.zip

FROM debian:12-slim
# ca-certificates: DepotDownloader / docker の TLS
# libicu72: DepotDownloader (.NET self-contained) の実行に必要
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libicu72 \
    && rm -rf /var/lib/apt/lists/*

# inner build (headless-container の Dockerfile) は BuildKit 必須。
# ビルド自体はマウントされた docker.sock 経由でホストデーモン上で走る。
COPY --from=docker:28-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker/buildx-bin:0.35.0 /buildx /usr/libexec/docker/cli-plugins/docker-buildx
COPY --from=depotdownloader /opt/depotdownloader /opt/depotdownloader
RUN ln -s /opt/depotdownloader/DepotDownloader /usr/local/bin/DepotDownloader

ENV DOCKER_BUILDKIT=1

# repo ソースを /src に焼き込む (git 不要)。ビルドコンテキストは repo root、
# `docker build -f builder/Dockerfile .` を想定。/src に含まれるものは
# builder/Dockerfile.dockerignore で制御する。
COPY . /src
WORKDIR /src

# CI (build-builder-image.yml) が release tag 由来のバージョンを渡す。
# entrypoint が ENV 経由で読み、inner build の APP_VERSION build-arg に引き継ぐ。
ARG APP_VERSION=0.0.0-dev
ENV APP_VERSION="${APP_VERSION}"
LABEL brhc.app-version="${APP_VERSION}"

RUN chmod +x /src/builder/entrypoint.sh
ENTRYPOINT ["/src/builder/entrypoint.sh"]
