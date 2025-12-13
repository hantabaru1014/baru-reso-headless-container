#!/bin/bash

set -e

if [ -z "$STEAM_USERNAME" ] || [ -z "$STEAM_PASSWORD" ] || [ -z "$HEADLESS_PASSWORD" ]; then
  echo "エラー: 必要な環境変数が設定されていません. 以下の環境変数を設定してください."
  echo "- STEAM_USERNAME"
  echo "- STEAM_PASSWORD"
  echo "- HEADLESS_PASSWORD"
  exit 1
fi

mkdir -p "$PWD/Resonite"

IMAGE_NAME="steamcmd/steamcmd:latest"
ENTRYPOINT="steamcmd"
INSTALL_DIR="/data"
APP_BRANCH="+app_update 2519830 -beta headless -betapassword $HEADLESS_PASSWORD"

if [ "$(uname -m)" = "arm64" ] || [ "$(uname -m)" = "aarch64" ]; then
  IMAGE_NAME="ghcr.io/sonroyaalmerol/steamcmd-arm64:latest"
  ENTRYPOINT="./steamcmd.sh"
  # ARM64の場合は異なるインストールディレクトリを使用
  INSTALL_DIR="/home/steam/Steam/steamapps/common/Resonite"
fi

if [ "${USE_PRERELEASE}" = "true" ]; then
  APP_BRANCH="+app_update 2519830 -beta prerelease"
fi

chmod 777 "$PWD/Resonite"

if [ "${USE_DEPOT_DOWNLOADER}" = "true" ]; then
  if [ ! -e "./DepotDownloader" ]; then
    TEMP_DIR=$(mktemp -d)
    trap 'rm -rf "$TEMP_DIR"' EXIT
    if [ "$(arch)" == "aarch64" ]; then
      wget https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-linux-arm64.zip -O "$TEMP_DIR/DepotDownloader.zip"
    else
      wget https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-linux-x64.zip -O "$TEMP_DIR/DepotDownloader.zip"
    fi
    unzip "$TEMP_DIR/DepotDownloader.zip" -d "$TEMP_DIR"
    cp "$TEMP_DIR/DepotDownloader" ./
    chmod +x ./DepotDownloader
    trap - EXIT
    rm -rf "$TEMP_DIR"
  fi

  ./DepotDownloader -app 2519830 -beta headless -betapassword $HEADLESS_PASSWORD -username $STEAM_USERNAME -password $STEAM_PASSWORD -dir ./Resonite -os linux
else
  echo "アーキテクチャ: $(uname -m)"
  echo "使用するイメージ: $IMAGE_NAME"
  
  docker run \
    -v "$PWD/Resonite:$INSTALL_DIR" \
    --entrypoint "$ENTRYPOINT" \
    "$IMAGE_NAME" \
    +force_install_dir "$INSTALL_DIR" \
    +login "$STEAM_USERNAME" "$STEAM_PASSWORD" \
    "$APP_BRANCH" \
    +quit
fi

if ! sudo chown -R "$USER:$USER" Resonite; then
  echo "警告: 所有者の変更に失敗しました"
fi

if [ ! -d "$PWD/Resonite" ] || [ -z "$(ls -A "$PWD/Resonite")" ]; then
  echo "エラー: Resoniteのダウンロードに失敗した可能性があります"
  echo "Resoniteディレクトリが空かまたは存在しません"
  exit 1
else
  echo "Resoniteのダウンロードが完了しました"
fi

rm -rf ./native-libs/arm64
mkdir -p ./native-libs/arm64
cp ./Resonite/Headless/runtimes/linux-arm64/lib/**/* ./Resonite/Headless/runtimes/linux-arm64/native/* ./native-libs/arm64/

rm -rf ./native-libs/amd64
mkdir -p ./native-libs/amd64
cp ./Resonite/Headless/runtimes/linux-x64/lib/**/* ./Resonite/Headless/runtimes/linux-x64/native/* ./native-libs/amd64/

if [ "$(uname -m)" = "arm64" ] || [ "$(uname -m)" = "aarch64" ]; then
  cp ./native-libs/arm64/* ./Resonite/Headless/
else
  cp ./native-libs/amd64/* ./Resonite/Headless/
fi
