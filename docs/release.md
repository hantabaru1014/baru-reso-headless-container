# リリースフロー

## リリースの作成

GitHub の Web UI から新しい Release を作成します。

- **Releases** → **Draft a new release**
- タグ名: `v{バージョン番号}` （例: `v0.9.0`）。このタグがそのままアプリバージョンになります
- タイトル、説明を記入して **Publish release**

## 自動デプロイ

リリースが作成されると、リリースタグのコミットから builder image が自動でビルドされます。

- アプリバージョンとしてタグの `{バージョン番号}` が image に焼き込まれる
- `ghcr.io/hantabaru1014/baru-reso-headless-container/builder:{バージョン番号}` と `:latest` タグでプッシュされる

headless image 自体は builder image を使って各環境でビルドします。詳細は [イメージのビルド](./build-image.md) を参照してください。

## Resonite バージョンアップ時のビルドテスト

Resonite 本体の更新後も headless image がビルドできるかは、**Image Build Test** ワークフロー (`image-build-test.yml`) を手動実行して確認できます。`main` ブランチのソースで image をビルドし、integration test まで実行します（image はどこにも push されません）。
