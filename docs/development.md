# 開発ガイド

## 必要なもの

- .NET SDK 10.0
- Docker
- Go（buf の実行に使用。[mise](https://mise.jdx.dev/) を使う場合は `mise install` で入ります）
- Resonite の headless ブランチにアクセスできる Steam アカウント

## セットアップ

環境変数 `STEAM_USERNAME`, `STEAM_PASSWORD`, `HEADLESS_PASSWORD` を設定して Resonite 本体をダウンロードします（mise を使う場合は `.env` に書けば読み込まれます）。

```sh
make download.resonite        # SteamCMD でダウンロード
make download.resonite-depot  # DepotDownloader でダウンロード
```

Resonite の prerelease ブランチを使う場合は `download.resonite-pre` / `download.resonite-pre-depot` を使います。

## ビルド・実行

Headless プロジェクトは `./Resonite/Headless` のアセンブリを参照します。ビルド前に EnginePrePatcher でアセンブリにパッチを当てる必要があります。

```sh
# アセンブリのパッチ (./Resonite/Headless に対して実行)
make prepatch

# ローカル実行
dotnet run --project Headless

# Docker image のビルド
make build.docker
```

Docker で開発する場合は `docker-compose.dev.yml` が使えます。

### EnginePrePatcher

Resonite のアセンブリをビルド前に書き換えるツールです。パッチは `EnginePrePatcher/Patches/` にあり、不要なコネクタの除去や internal メンバーの公開などを行います。Dockerfile 内のビルドでも自動的に実行されます。

## gRPC API

API 定義は `proto/headless/v1/headless.proto` にあり、[buf](https://buf.build/) で管理しています。

```sh
make build.proto  # Headless/Protos に C# コードを生成
make lint         # proto と C# の lint / format
```

[evans](https://github.com/ktr0731/evans) で REPL から API を試せます。

```sh
make evans
```

## テスト

```sh
make test
```

ネイティブアーキテクチャの Docker image をビルドし、それに対して `Headless.Tests` のテスト（ユニットテスト + 統合テスト）を実行します。

> [!NOTE]
> QEMU エミュレーション下では Resonite が SIGSEGV でクラッシュするため、クロスアーキテクチャのテストはできません。CI では amd64 / arm64 それぞれのネイティブランナーでテストしています。

## リポジトリ構成

| パス | 内容 |
| --- | --- |
| `Headless/` | ヘッドレス本体（ASP.NET Core + FrooxEngine をホストする gRPC サーバー） |
| `Headless.Tests/` | ユニットテスト・統合テスト |
| `EnginePrePatcher/` | Resonite アセンブリをビルド前にパッチするツール |
| `proto/` | gRPC API 定義（buf で管理） |
| `scripts/` | Resonite 本体のダウンロードスクリプト |
| `native-libs/` | アーキテクチャ別のネイティブライブラリ |
