# baru-reso-headless-container

自分用のカスタムヘッドレス。  
まだ作成中で中身をわからずに他人が使える状態じゃないですが、Dockerfile, GithubAction, EnginePrePatcher辺りとかarm64で動くカスタムヘッドレスの作成の参考にどうぞ

ビルドしたdocker imageはResoniteのアセンブリを含んでるので公開できないです。CIでビルドしたかったらforkしてください。

## GithubActionの設定
以下をSecretsで設定。Workflow permissionsを "Read and write permissions" に設定する。
- STEAM_USERNAME
- STEAM_PASSWORD
- HEADLESS_PASSWORD

## リリースフロー

新しいバージョンをリリースする際は以下の手順で行います：

### 1. バージョンのインクリメント
GitHub Actionsから `Bump Version` ワークフローを実行します。
- **Actions** → **Bump Version** → **Run workflow**
- バージョンタイプ（major/minor/patch）を選択して実行
- 自動的にバージョンをインクリメントしたPRが作成されます
- PRのレビュアーには実行者が自動的にアサインされます

### 2. PRのレビュー＆マージ
作成されたPRをレビューして `main` ブランチにマージします。

### 3. リリースの作成
GitHubのWeb UIから新しいReleaseを作成します。
- **Releases** → **Draft a new release**
- タグ名: `v{バージョン番号}` （例: `v0.3.7`）
- タイトル、説明を記入して **Publish release**

### 4. 自動デプロイ
リリースが作成されると、自動的に以下が実行されます：
- `release` ブランチがリリースタグのコミットに更新されます
- Docker imageのビルドとプッシュが自動実行されます
- ビルドされたimageには `latest` タグと `{RESONITE_VERSION}-v{APP_VERSION}` タグが付与されます
