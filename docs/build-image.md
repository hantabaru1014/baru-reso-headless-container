# イメージのビルド

ビルドしたイメージには Resonite のアセンブリが含まれるため、公開レジストリで配布できません。以下のいずれかの方法で自分用のイメージをビルドしてください。

いずれの方法でも、Resonite の headless ブランチにアクセスできる Steam アカウントが必要です。

## ローカルでビルドする

.NET SDK 10.0 と Docker が必要です。

```sh
# Resonite 本体のダウンロード (要 STEAM_USERNAME, STEAM_PASSWORD, HEADLESS_PASSWORD)
make download.resonite

# イメージのビルド
make build.docker
```

`make download.resonite` は SteamCMD (Docker) を使用します。SteamCMD が使えない環境では DepotDownloader を使う `make download.resonite-depot` も利用できます。

Resonite の prerelease ブランチを使う場合は `make download.resonite-pre` / `make download.resonite-pre-depot` を使ってください。

## CI (GitHub Actions) でビルドする

CI でビルドする場合は、このリポジトリを fork して以下を設定します。

1. リポジトリの **Settings** → **Actions** → **General** → **Workflow permissions** を "Read and write permissions" に設定する
2. 以下の Secrets を設定する
   - `STEAM_USERNAME` — Steam アカウントのユーザー名
   - `STEAM_PASSWORD` — Steam アカウントのパスワード
   - `HEADLESS_PASSWORD` — Resonite headless ブランチのベータアクセスコード

Release を作成する（[リリースフロー](./release.md) 参照）と、GHCR にイメージがビルド・push されます。イメージには `latest` タグと `{RESONITE_VERSION}-v{APP_VERSION}` タグが付与されます。

Release を作成せずにビルドだけしたい場合は、**Actions** → **Build and Push Docker Image** → **Run workflow** から手動実行できます。Resonite の branch (headless / prerelease) とビルド対象の git branch も選択できます。
