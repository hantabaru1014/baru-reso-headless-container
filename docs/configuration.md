# 設定リファレンス

設定はすべて環境変数で行います。

## 環境変数

| 環境変数 | 説明 | デフォルト |
| --- | --- | --- |
| `HeadlessUserCredential` | ヘッドレスアカウントのユーザー名またはメールアドレス | - |
| `HeadlessUserPassword` | ヘッドレスアカウントのパスワード | - |
| `StartupConfig` | 起動設定の JSON（下記参照） | - |
| `RpcHostUrl` | gRPC サーバーのリッスン URL | `http://0.0.0.0:5000` |
| `DataDirectoryPath` | Resonite の Data / Cache ディレクトリを配置するパス | カレントディレクトリ |
| `BackgroundWorkers` | FrooxEngine のバックグラウンドワーカー数 | エンジン既定値 |
| `PriorityWorkers` | FrooxEngine の優先ワーカー数 | エンジン既定値 |
| `ShutdownTimeoutSeconds` | シャットダウン時にワールドの保存等を待つ最大秒数 | `180` |

## StartupConfig

起動時に開くワールドなどの設定を JSON で指定します。スキーマは [headless.proto](../proto/headless/v1/headless.proto) の `StartupConfig` メッセージです（フィールド名は camelCase の JSON 表現）。

```json
{
  "startWorlds": [
    {
      "name": "My World",
      "loadWorldPresetName": "GridSpace",
      "maxUsers": 16,
      "accessLevel": "ACCESS_LEVEL_CONTACTS"
    }
  ]
}
```

主なフィールド:

| フィールド | 説明 |
| --- | --- |
| `universeId` | 接続するユニバース ID |
| `tickRate` | ティックレート |
| `maxConcurrentAssetTransfers` | アセット同時転送数 |
| `usernameOverride` | セッションホストとして表示するユーザー名 |
| `startWorlds` | 起動時に開くワールドのリスト (`WorldStartupParameters`) |
| `allowedUrlHosts` | HTTP / WebSocket / OSC でのアクセスを許可するホスト |
| `autoSpawnItems` | 各ワールド起動時に自動スポーンするアイテムの URL |

`WorldStartupParameters` は公式ヘッドレスの設定とおおむね互換のパラメータ（`loadWorldUrl` / `loadWorldPresetName`, `maxUsers`, `accessLevel`, `customSessionId`, `defaultUserRoles`, `saveOnExit`, `autoSaveIntervalSeconds` など）に加え、独自の拡張パラメータを持ちます。全フィールドは proto 定義を参照してください。

### 公式ヘッドレスの Config.json を使う

環境変数 `StartupConfig` が設定されていない場合、コンテナのカレントディレクトリの `Config/Config.json` を公式ヘッドレスの設定ファイルとして読み込み、対応するフィールド（`universeID`, `tickRate`, `maxConcurrentAssetTransfers`, `usernameOverride`, `startWorlds`, `allowedUrlHosts`, `autoSpawnItems`）を変換して利用します。既存の公式ヘッドレスからの移行時はマウントするだけで動きます。

なお、ログイン情報 (`loginCredential` / `loginPassword`) は Config.json からは読み込まれません。環境変数 `HeadlessUserCredential` / `HeadlessUserPassword` で指定してください。
