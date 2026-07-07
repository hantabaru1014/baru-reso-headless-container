# リリースフロー

## 1. バージョンのインクリメント

GitHub Actions から `Bump Version` ワークフローを実行します。

- **Actions** → **Bump Version** → **Run workflow**
- バージョンタイプ (major / minor / patch) を選択して実行
- バージョンをインクリメントした PR が自動作成され、実行者がレビュアーにアサインされます

## 2. PR のレビュー＆マージ

作成された PR をレビューして `main` ブランチにマージします。

## 3. リリースの作成

GitHub の Web UI から新しい Release を作成します。

- **Releases** → **Draft a new release**
- タグ名: `v{バージョン番号}` （例: `v0.3.7`）
- タイトル、説明を記入して **Publish release**

> [!IMPORTANT]
> タグ名は `Headless/AppVersion` の中身と一致している必要があります（`v{AppVersion}`）。Release CI の `validate-version` ジョブがこれを検証し、不一致（＝手順 1・2 のバージョンインクリメントを飛ばした場合など）ならリリースのビルドは失敗します。

## 4. 自動デプロイ

リリースが作成されると、自動的に以下が実行されます。

- `release` ブランチがリリースタグのコミットに更新される
- Docker image のビルドとプッシュが実行される
- ビルドされた image には `latest` タグと `{RESONITE_VERSION}-v{APP_VERSION}` タグが付与される
