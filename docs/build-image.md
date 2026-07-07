# イメージのビルド

headless イメージには Resonite のアセンブリが含まれるため、公開レジストリで配布できません。代わりに、このリポジトリが発行する **builder image** を使って手元で headless イメージをビルドします。

builder image (`ghcr.io/hantabaru1014/baru-reso-headless-container/builder`) は Resonite のアセンブリを含まないため public に配布されています。リポジトリのソース一式・DepotDownloader・docker CLI が焼き込まれており、`docker run` するだけで Resonite の取得から headless イメージのビルドまでを一括で行います。

いずれの場合も、ビルドに使う Steam アカウント (username / password) と Resonite headless ブランチのベータアクセスコードが必要です。Steam アカウントは、パスワードログインができて2段階認証 (Steam Guard) をオフにした新規の専用アカウントを用意してください。ベータアクセスコードはダウンロード時に指定されるため、アカウント自体で headless ブランチを有効化する必要はありません。

## builder image でビルドする

Docker があれば .NET SDK 等のツールチェインは不要です。ビルドはマウントした docker.sock 経由でホストの Docker デーモン上で走るため、`/var/run/docker.sock` のマウントが必須です。

```sh
docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e STEAM_USERNAME=<steam_username> \
  -e STEAM_PASSWORD=<steam_password> \
  -e HEADLESS_PASSWORD=<headless_beta_code> \
  ghcr.io/hantabaru1014/baru-reso-headless-container/builder
```

上記は headless ブランチの最新をビルドし、`ghcr.io/hantabaru1014/baru-reso-headless-container:<RESONITE_VERSION>-<APP_VERSION>` というタグのイメージをホストの Docker に作成します。`--output-image` で出力イメージ名を差し替えれば、自分のイメージ名でビルドできます。

### env (secrets のみ)

| 変数 | 必須 | 意味 |
|---|---|---|
| `STEAM_USERNAME` | ✓ | Steam アカウントのユーザー名 |
| `STEAM_PASSWORD` | ✓ | Steam アカウントのパスワード |
| `HEADLESS_PASSWORD` | `--branch headless` 時 | Resonite headless ブランチのベータアクセスコード |

### 引数 (すべて省略可)

| 引数 | default | 意味 |
|---|---|---|
| `--branch <name>` | `headless` | Resonite の branch (`headless` / `prerelease` / その他) |
| `--manifest <id>` | (branch 最新) | Steam depot manifest ID。省略時は branch の最新をダウンロード |
| `--game-version <v>` | (自動) | Resonite バージョン。省略時は `Build.version` から読む |
| `--output-image <name>` | `ghcr.io/hantabaru1014/baru-reso-headless-container` | 出力イメージ名 (タグ抜き) |
| `--build-id <id>` | (なし) | built image の label `brhc.build-id` に焼く |
| `--app-id <id>` | `2519830` | Steam AppID |
| `--depot-id <id>` | (なし) | 指定時 DepotDownloader の `-depot` に渡す |
| `--help` | | usage を表示 |

`--help` で同じ内容の usage を確認できます。

### builder image の入手

上記の GHCR イメージをそのまま pull できます (public)。手元でビルドしたい場合は `make build.builder` でビルドできます。

> [!NOTE]
> builder image は汎用ツールです。例えば管理アプリ [baru-reso-headless-controller](https://github.com/hantabaru1014/baru-reso-headless-controller) はこの builder image を使って headless イメージのビルドを自動化しています。

## (補足) ツールチェインを使ってローカルでビルドする

builder image を使わず、リポジトリを clone して直接ビルドすることもできます。開発者向けの補足手順です。.NET SDK 10.0 と Docker が必要です。

```sh
# Resonite 本体のダウンロード (要 STEAM_USERNAME, STEAM_PASSWORD, HEADLESS_PASSWORD)
make download.resonite

# イメージのビルド
make build.docker
```

`make download.resonite` は SteamCMD (Docker) を使用します。SteamCMD が使えない環境では DepotDownloader を使う `make download.resonite-depot` も利用できます。Resonite の prerelease ブランチを使う場合は `make download.resonite-pre` / `make download.resonite-pre-depot` を使ってください。
