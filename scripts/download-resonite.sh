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
chmod 777 "$PWD/Resonite"

# DepotDownloader用のbetaオプション
DEPOT_BETA="headless"
DEPOT_BETA_PASSWORD="-betapassword $HEADLESS_PASSWORD"
if [ "${USE_PRERELEASE}" = "true" ]; then
  DEPOT_BETA="prerelease"
  DEPOT_BETA_PASSWORD=""
fi

# SteamCMD用の設定
IMAGE_NAME="steamcmd/steamcmd:latest"
ENTRYPOINT="steamcmd"
INSTALL_DIR="/data"
APP_BRANCH="+app_update 2519830 -beta headless -betapassword $HEADLESS_PASSWORD"

if [ "$(uname -m)" = "arm64" ] || [ "$(uname -m)" = "aarch64" ]; then
  IMAGE_NAME="ghcr.io/sonroyaalmerol/steamcmd-arm64:latest"
  ENTRYPOINT="./steamcmd.sh"
  INSTALL_DIR="/home/steam/Steam/steamapps/common/Resonite"
fi

if [ "${USE_PRERELEASE}" = "true" ]; then
  APP_BRANCH="+app_update 2519830 -beta prerelease"
fi

# DepotDownloaderでダウンロードを試みる関数
download_with_depot_downloader() {
  TEMP_DIR=$(mktemp -d)
  trap 'rm -rf "$TEMP_DIR"' RETURN

  if [ "$(arch)" == "aarch64" ]; then
    wget https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-linux-arm64.zip -O "$TEMP_DIR/DepotDownloader.zip"
  else
    wget https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-linux-x64.zip -O "$TEMP_DIR/DepotDownloader.zip"
  fi
  unzip "$TEMP_DIR/DepotDownloader.zip" -d "$TEMP_DIR"
  chmod +x "$TEMP_DIR/DepotDownloader"

  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  FILELIST="${SCRIPT_DIR}/../depot-dl-list.txt"
  "$TEMP_DIR/DepotDownloader" -app 2519830 -beta $DEPOT_BETA $DEPOT_BETA_PASSWORD -username $STEAM_USERNAME -password $STEAM_PASSWORD -dir ./Resonite -os linux -filelist "$FILELIST"
}

# SteamCMDでダウンロードする関数
download_with_steamcmd() {
  echo "SteamCMDでダウンロードを試みます..."
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
}

# まずDepotDownloaderを試し、失敗したらSteamCMDにフォールバック
DOWNLOAD_SUCCESS=false

if download_with_depot_downloader; then
  if [ -d "$PWD/Resonite" ] && [ -n "$(ls -A "$PWD/Resonite")" ]; then
    echo "DepotDownloaderでのダウンロードが成功しました"
    DOWNLOAD_SUCCESS=true
  fi
fi

if [ "$DOWNLOAD_SUCCESS" = "false" ]; then
  echo "DepotDownloaderでのダウンロードに失敗しました。SteamCMDにフォールバックします..."
  rm -rf "$PWD/Resonite"/*
  if download_with_steamcmd; then
    if [ -d "$PWD/Resonite" ] && [ -n "$(ls -A "$PWD/Resonite")" ]; then
      echo "SteamCMDでのダウンロードが成功しました"
      DOWNLOAD_SUCCESS=true
    fi
  fi
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
