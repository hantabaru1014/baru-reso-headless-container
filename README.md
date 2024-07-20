# baru-reso-headless-container

自分用のカスタムヘッドレス。  
まだ作成中で中身をわからずに他人が使える状態じゃないですが、Dockerfile, GithubAction, EnginePrePatcher辺りとかarm64で動くカスタムヘッドレスの作成の参考にどうぞ

ビルドしたdocker imageはResoniteのアセンブリを含んでるので公開できないです。CIでビルドしたかったらforkしてください。

## GithubActionの設定
以下をSecretsで設定。Workflow permissionsを "Read and write permissions" に設定する。
- STEAM_USERNAME
- STEAM_PASSWORD
- HEADLESS_PASSWORD
