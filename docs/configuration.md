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
| `PublicIp` | コンテナのグローバル IP / ホスト名。現時点では QUIC のセッション URL の announce にのみ使われます（下記参照） | - |

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

`WorldStartupParameters` は公式ヘッドレスの設定とおおむね互換のパラメータ（`loadWorldUrl` / `loadWorldPresetName`, `maxUsers`, `accessLevel`, `customSessionId`, `defaultUserRoles`, `saveOnExit`, `autoSaveIntervalSeconds` など）に加え、独自の拡張パラメータとして `joinAllowedUserIds`（招待メッセージを送らずに join のアクセス許可だけを与えるユーザー ID のリスト）を持ちます。全フィールドは proto 定義を参照してください。

### ポートの固定

セッションが待ち受けるポートを固定する場合は `forcePorts` を指定します。指定しなかったプロトコルはランダムなポートを使います。

```json
"forcePorts": [
  { "protocol": "NETWORK_PROTOCOL_LNL", "port": 12345 },
  { "protocol": "NETWORK_PROTOCOL_QUIC", "port": 12346 }
]
```

`protocol` は `NETWORK_PROTOCOL_LNL` / `NETWORK_PROTOCOL_QUIC` / `NETWORK_PROTOCOL_TCP` のいずれかです。同じプロトコルを複数指定した場合は最後のものが使われ、範囲外 (1 - 65535 以外) のポートは無視されます。単一ポートのみを指定する `forcePort` は非推奨で、`forcePorts` が空のときに限り LNL のポートとして扱われます。

LNL と QUIC は UDP、TCP は TCP を使うので、コンテナのポート公開もそれに合わせてください (例: `-p 12345:12345/udp`)。

### 公式ヘッドレスの Config.json を使う

環境変数 `StartupConfig` が設定されていない場合、コンテナのカレントディレクトリの `Config/Config.json` を公式ヘッドレスの設定ファイルとして読み込み、対応するフィールド（`universeID`, `tickRate`, `maxConcurrentAssetTransfers`, `usernameOverride`, `startWorlds`, `allowedUrlHosts`, `autoSpawnItems`）を変換して利用します。既存の公式ヘッドレスからの移行時はマウントするだけで動きます。

なお、ログイン情報 (`loginCredential` / `loginPassword`) は Config.json からは読み込まれません。環境変数 `HeadlessUserCredential` / `HeadlessUserPassword` で指定してください。

## QUIC

イメージには `libmsquic` が含まれているため QUIC は有効ですが、外部から接続させるには追加で 2 つの設定が必要です。

1. 環境変数 `PublicIp` にグローバル IP (またはホスト名) を設定する

    QUIC のリスナーは公開アドレスが分かっている場合のみセッション URL (`quic://<ip>:<port>`) を announce します。未設定だと LAN 内のアドレスしか announce されず、外部から QUIC で接続できません。

    ```yaml
    environment:
      - PublicIp=203.0.113.10
    ```

    この値は Resonite エンジンが読む `Config.json` (AppConfig) の `quicConfig.publicIP` として渡されます。`AppPath` 直下に `Config.json` がある場合はそれを土台にして `quicConfig` だけ上書きします。

2. `forcePorts` で `NETWORK_PROTOCOL_QUIC` のポートを固定し、その UDP ポートを公開する

    固定しないとセッションごとにポートが変わるため、ポートフォワードできません。
