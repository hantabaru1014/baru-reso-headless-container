ワールド開始
```
grpcurl -plaintext -proto proto/headless/v1/headless.proto -d '{
  "parameters": {
    "name": "test",
    "access_level": "ACCESS_LEVEL_PRIVATE",
    "load_world_preset_name": "Grid"
  }
}' localhost:5014 headless.v1.HeadlessControlService/StartWorld
```

セッション一覧
```
grpcurl -plaintext -proto proto/headless/v1/headless.proto localhost:5014 headless.v1.HeadlessControlService/ListSessions
```
