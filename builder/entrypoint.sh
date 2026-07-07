#!/bin/bash
#
# builder image の entrypoint。
#
# baru-reso-headless-container の headless image をどこからでもビルドできる汎用ツール。
# DepotDownloader で Resonite を取得 → native-libs を整形 → inner Dockerfile を
# docker build する。ビルドはマウントされた docker.sock 経由でホストデーモン上で走る。
#
# 使い方:
#   docker run --rm \
#     -v /var/run/docker.sock:/var/run/docker.sock \
#     -e STEAM_USERNAME=... -e STEAM_PASSWORD=... -e HEADLESS_PASSWORD=... \
#     <builder-image> [OPTIONS]
#
# env (secrets のみ):
#   STEAM_USERNAME    (必須)
#   STEAM_PASSWORD    (必須)
#   HEADLESS_PASSWORD (--branch headless のとき必須)
#
# 引数 (毎回変わるパラメータ。すべて省略可):
#   --branch <name>        Resonite branch。default headless
#   --manifest <id>        Steam depot manifest ID。省略時は branch の最新
#   --game-version <v>     Resonite バージョン。省略時は Build.version から読む
#   --output-image <name>  出力イメージ名 (タグ抜き)
#   --build-id <id>        built image の label brhc.build-id に焼く
#   --app-id <id>          Steam AppID。default 2519830
#   --depot-id <id>        指定時 DepotDownloader の -depot に渡す
#   --help                 usage を表示
#
# 注意: 認証情報を echo しない (set -x 禁止)。

set -euo pipefail

SRC_DIR=/src
RESONITE_DIR="${SRC_DIR}/Resonite"

DEFAULT_OUTPUT_IMAGE="ghcr.io/hantabaru1014/baru-reso-headless-container"
DEFAULT_APP_ID="2519830"

usage() {
  cat <<'EOF'
Usage: docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e STEAM_USERNAME=... -e STEAM_PASSWORD=... -e HEADLESS_PASSWORD=... \
  ghcr.io/hantabaru1014/baru-reso-headless-container/builder [OPTIONS]

Builds a baru-reso-headless-container headless image. Downloads Resonite with
DepotDownloader, then builds the image on the host Docker daemon via the mounted
docker.sock. The built image is tagged <output-image>:<[prerelease-]gameVersion>-<appVersion>.

Environment (secrets only):
  STEAM_USERNAME     (required)
  STEAM_PASSWORD     (required)
  HEADLESS_PASSWORD  (required when --branch headless)

Options (all optional):
  --branch <name>        Resonite branch: headless (default) / prerelease / other
  --manifest <id>        Steam depot manifest ID. If omitted, downloads the branch's latest
  --game-version <v>     Resonite version. If omitted, read from Resonite/Headless/Build.version
  --output-image <name>  Output image name without tag
                         (default: ghcr.io/hantabaru1014/baru-reso-headless-container)
  --build-id <id>        Stamped onto the built image as label brhc.build-id
  --app-id <id>          Steam AppID (default: 2519830)
  --depot-id <id>        Passed to DepotDownloader as -depot
  --help                 Show this help and exit

Example (build the latest headless image locally):
  docker run --rm -v /var/run/docker.sock:/var/run/docker.sock \
    -e STEAM_USERNAME=me -e STEAM_PASSWORD=secret -e HEADLESS_PASSWORD=beta \
    ghcr.io/hantabaru1014/baru-reso-headless-container/builder
EOF
}

# --- 引数 parse ---
BRANCH="headless"
MANIFEST_ID=""
GAME_VERSION=""
OUTPUT_IMAGE="${DEFAULT_OUTPUT_IMAGE}"
BUILD_ID=""
APP_ID="${DEFAULT_APP_ID}"
DEPOT_ID=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --branch) BRANCH="$2"; shift 2 ;;
    --manifest) MANIFEST_ID="$2"; shift 2 ;;
    --game-version) GAME_VERSION="$2"; shift 2 ;;
    --output-image) OUTPUT_IMAGE="$2"; shift 2 ;;
    --build-id) BUILD_ID="$2"; shift 2 ;;
    --app-id) APP_ID="$2"; shift 2 ;;
    --depot-id) DEPOT_ID="$2"; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *)
      echo "error: unknown argument: $1" >&2
      echo >&2
      usage >&2
      exit 1
      ;;
  esac
done

# --- env (secrets) バリデーション ---
missing=()
[ -n "${STEAM_USERNAME:-}" ] || missing+=("STEAM_USERNAME")
[ -n "${STEAM_PASSWORD:-}" ] || missing+=("STEAM_PASSWORD")
if [ "${BRANCH}" = "headless" ] && [ -z "${HEADLESS_PASSWORD:-}" ]; then
  missing+=("HEADLESS_PASSWORD (required for --branch headless)")
fi

if [ "${#missing[@]}" -gt 0 ]; then
  echo "error: missing required environment variables:" >&2
  for m in "${missing[@]}"; do
    echo "  - ${m}" >&2
  done
  exit 1
fi

# --- DepotDownloader で Resonite を取得 ---
# DepotDownloader は manifest から削除されたファイルを prune しないため、毎回クリーンな
# ディレクトリに DL する。誰かが手動で volume をマウントしても古い manifest / 別 branch の
# 残骸ファイルが built image に混入しないよう、防御的に空にしてから DL する。
rm -rf "${RESONITE_DIR:?}"
mkdir -p "${RESONITE_DIR}"

dd_args=(
  -app "${APP_ID}"
  -username "${STEAM_USERNAME}"
  -password "${STEAM_PASSWORD}"
  -dir "${RESONITE_DIR}"
  -os linux
  -filelist "${SRC_DIR}/depot-dl-list.txt"
)

# --manifest 省略時は -manifest を渡さず branch の最新をダウンロードする。
if [ -n "${MANIFEST_ID}" ]; then
  dd_args+=(-manifest "${MANIFEST_ID}")
fi

if [ -n "${DEPOT_ID}" ]; then
  dd_args+=(-depot "${DEPOT_ID}")
fi

case "${BRANCH}" in
  prerelease)
    dd_args+=(-beta prerelease)
    ;;
  headless)
    dd_args+=(-beta headless -betapassword "${HEADLESS_PASSWORD}")
    ;;
  *)
    # その他の branch は -beta 指定なし (default depot)
    ;;
esac

echo "==> Downloading Resonite (branch=${BRANCH}, manifest=${MANIFEST_ID:-latest})"
DepotDownloader "${dd_args[@]}"

# --- ダウンロード結果の検証 ---
if [ ! -d "${RESONITE_DIR}/Headless" ] || [ -z "$(ls -A "${RESONITE_DIR}/Headless" 2>/dev/null)" ]; then
  echo "error: DepotDownloader did not populate ${RESONITE_DIR}/Headless" >&2
  exit 1
fi

# --- game version の解決 (--game-version > Build.version) ---
if [ -z "${GAME_VERSION}" ]; then
  build_version_file="${RESONITE_DIR}/Headless/Build.version"
  if [ ! -f "${build_version_file}" ]; then
    echo "error: --game-version is empty and ${build_version_file} was not found" >&2
    exit 1
  fi
  GAME_VERSION="$(tr -d '[:space:]' < "${build_version_file}")"
fi

# --- native-libs の整形 (scripts/download-resonite.sh 後半と同等) ---
# inner Dockerfile が ./native-libs/${TARGETARCH}/* を要求するため、runtime 配下の
# lib/native をフラットにコピーする。runtime ディレクトリが丸ごと無い arch は skip する
# (headless の linux ビルドは arm64 runtime を欠くことがある)。
shopt -s globstar nullglob

prepare_native_libs() {
  local arch="$1" runtime="$2"
  local runtime_root="${RESONITE_DIR}/Headless/runtimes/${runtime}"
  local dst="${SRC_DIR}/native-libs/${arch}"

  if [ ! -d "${runtime_root}" ]; then
    echo "==> native-libs: skipping ${arch} (${runtime} runtime not present)"
    return 0
  fi

  rm -rf "${dst}"
  mkdir -p "${dst}"

  local f
  for f in "${runtime_root}"/lib/**/* "${runtime_root}"/native/*; do
    # glob 展開にはディレクトリも含まれる。set -e 下でループ末尾要素が非ファイルだと
    # 関数の exit status が 1 になり script が死ぬため、if 文で明示的に分岐する。
    if [ -f "${f}" ]; then
      cp "${f}" "${dst}/"
    fi
  done
}

prepare_native_libs amd64 linux-x64
prepare_native_libs arm64 linux-arm64

# Resonite 配布の runtimes/linux-x64/native/libbrolib.so は aarch64 バイナリで amd64 では
# dlopen 対象にもならない死蔵ファイル (Brotli.LibPathBootStrapper は IsArm のときだけ
# libbrolib.so を選び、それ以外では brolib_x64.so を使う)。
rm -f "${SRC_DIR}/native-libs/amd64/libbrolib.so"

shopt -u globstar nullglob

# --- タグ計算 ---
APP_VERSION="$(tr -d '[:space:]' < "${SRC_DIR}/Headless/AppVersion")"

TAG_PREFIX=""
if [ "${BRANCH}" = "prerelease" ]; then
  TAG_PREFIX="prerelease-"
fi
TAG="${TAG_PREFIX}${GAME_VERSION}-${APP_VERSION}"

# --- inner build ---
build_args=(
  build
  -t "${OUTPUT_IMAGE}:${TAG}"
  --label "brhc.image-tag=${TAG}"
  --label "brhc.resonite-version=${GAME_VERSION}"
  --label "brhc.app-version=${APP_VERSION}"
)
if [ -n "${BUILD_ID}" ]; then
  build_args+=(--label "brhc.build-id=${BUILD_ID}")
fi
build_args+=("${SRC_DIR}")

echo "==> Building image ${OUTPUT_IMAGE}:${TAG}"
docker "${build_args[@]}"

# --- 結果出力 (人間向け。呼び出し側は label 経由で結果を読む) ---
printf 'BUILD_RESULT: {"image_tag":"%s","resonite_version":"%s","app_version":"%s"}\n' \
  "${TAG}" "${GAME_VERSION}" "${APP_VERSION}"
