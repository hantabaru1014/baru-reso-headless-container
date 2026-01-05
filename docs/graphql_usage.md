# FrooxEngine GraphQL API 利用ガイド

このドキュメントは、FrooxEngine（Resonite）のWorld内の状態を調査・操作するためのGraphQL APIの使い方を説明します。

## 基本概念

### Resonite/FrooxEngineのデータ構造

```
World (ワールド/セッション)
├── RootSlot (ルートスロット)
│   ├── Slot (子スロット)
│   │   ├── Component (コンポーネント)
│   │   │   └── SyncMember (プロパティ)
│   │   │       ├── SyncField (値型: 数値、文字列、bool等)
│   │   │       └── SyncRef (参照型: 他のSlot/Componentへの参照)
│   │   └── Slot (孫スロット)
│   │       └── ...
│   └── ...
└── Users (ユーザー一覧)
```

**用語説明:**

| 用語 | 説明 |
|------|------|
| **World** | 一つのセッション/ワールド。SessionIdで識別される |
| **Slot** | 3D空間上のオブジェクト。位置・回転・スケールを持ち、階層構造を形成 |
| **Component** | Slotにアタッチされる機能単位（MeshRenderer, Collider, ValueField等） |
| **SyncMember** | Componentのプロパティ。SyncFieldとSyncRefの2種類がある |
| **SyncField** | 値を保持するプロパティ（int, float, string, bool, float3等） |
| **SyncRef** | 他のSlot/Componentへの参照を保持するプロパティ |
| **RefID** | オブジェクトの一意識別子（例: `ID8A2B3C4D`） |

## エンドポイント

```
http://localhost:5050/graphql
```

GraphQL Playgroundも同じURLでアクセス可能です。

**注意:** GraphQLはgRPC（ポート5014）とは別ポートで動作します。

## 設定

環境変数で設定:
```bash
# GraphQLを有効化（デフォルト: true）
GraphQL__Enabled=true

# GraphQLのポート番号（デフォルト: 5050）
GraphQL__Port=5050

# GraphQLのパス（デフォルト: /graphql）
GraphQL__Path=/graphql
```

## Query（読み取り操作）

### 1. ワールド一覧の取得

```graphql
query {
  worlds {
    sessionId
    name
    userCount
    maxUsers
    accessLevel
  }
}
```

### 2. 特定ワールドの詳細取得

```graphql
query GetWorld($sessionId: String!) {
  world(sessionId: $sessionId) {
    sessionId
    name
    description
    userCount
    rootSlot {
      refId
      name
    }
    users {
      userId
      userName
      isHost
      isPresent
    }
  }
}
```

### 3. Slot階層の探索

**RootSlotから子を取得:**
```graphql
query ExploreSlots($sessionId: String!) {
  world(sessionId: $sessionId) {
    rootSlot {
      name
      children {
        refId
        name
        active
        childCount
        componentCount
      }
    }
  }
}
```

**特定のSlotを深掘り:**
```graphql
query GetSlotDetails($sessionId: String!, $refId: String!) {
  slot(sessionId: $sessionId, refId: $refId) {
    refId
    name
    active
    tag
    position { x y z }
    rotation { x y z w }
    scale { x y z }
    parent {
      refId
      name
    }
    children {
      refId
      name
    }
    components {
      refId
      typeName
      enabled
    }
  }
}
```

### 4. Slotを名前で検索

```graphql
query FindSlotsByName($sessionId: String!, $name: String!) {
  world(sessionId: $sessionId) {
    findSlotByName(name: $name, searchChildren: true) {
      refId
      name
      parent {
        name
      }
    }
  }
}
```

### 5. Componentのプロパティ取得

**全プロパティを列挙:**
```graphql
query GetComponentProperties($sessionId: String!, $refId: String!) {
  component(sessionId: $sessionId, refId: $refId) {
    refId
    typeName
    typeFullName
    enabled
    syncMemberCount
    syncMembers {
      name
      typeName
      isLinked
      ... on SyncFieldType {
        valueType
        value
        valueAsString
        valueAsFloat
        valueAsInt
        valueAsBool
      }
      ... on SyncRefType {
        targetRefId
        targetTypeName
        targetAsSlot {
          refId
          name
        }
        targetAsComponent {
          refId
          typeName
        }
      }
    }
  }
}
```

**特定のプロパティを取得:**
```graphql
query GetSpecificProperty($sessionId: String!, $refId: String!) {
  component(sessionId: $sessionId, refId: $refId) {
    syncMemberByName(name: "Value") {
      name
      ... on SyncFieldType {
        valueType
        value
      }
    }
  }
}
```

### 6. Slotから特定タイプのComponentを検索

```graphql
query FindComponents($sessionId: String!, $slotRefId: String!) {
  slot(sessionId: $sessionId, refId: $slotRefId) {
    componentByType(typeName: "ValueField`1") {
      refId
      typeName
      syncMembers {
        name
        ... on SyncFieldType {
          value
        }
      }
    }
  }
}
```

**複数のComponentを取得:**
```graphql
query FindMultipleComponents($sessionId: String!, $slotRefId: String!) {
  slot(sessionId: $sessionId, refId: $slotRefId) {
    componentsByType(typeName: "Sync`1") {
      refId
      typeName
    }
  }
}
```

### 7. アタッチ可能なComponentタイプ一覧

```graphql
query ListComponentTypes {
  componentTypes(filter: null) {
    name
    fullName
    category
    isGeneric
  }
}
```

**フィルター付きで検索:**
```graphql
query SearchComponents {
  componentTypes(filter: "Light") {
    name
    fullName
    category
  }
}
```

**結果例:**
```json
{
  "data": {
    "componentTypes": [
      { "name": "Light", "fullName": "FrooxEngine.Light", "category": "Core" },
      { "name": "LightSource", "fullName": "FrooxEngine.LightSource", "category": "Core" }
    ]
  }
}
```

`fullName` を `attachComponent` の `componentTypeName` に使用します。

## Mutation（変更操作）

### 1. Slotの操作

**子Slotを追加:**
```graphql
mutation AddSlot($sessionId: String!, $parentRefId: String!, $name: String!) {
  addChildSlot(sessionId: $sessionId, parentSlotRefId: $parentRefId, name: $name) {
    refId
    name
  }
}
```

**Slotを削除:**
```graphql
mutation DeleteSlot($sessionId: String!, $slotRefId: String!) {
  deleteSlot(sessionId: $sessionId, slotRefId: $slotRefId, preserveChildren: false) {
    success
    deletedRefId
    error
  }
}
```

**Slotの有効/無効を切り替え:**
```graphql
mutation ToggleSlot($sessionId: String!, $slotRefId: String!, $active: Boolean!) {
  setSlotActive(sessionId: $sessionId, slotRefId: $slotRefId, active: $active) {
    refId
    active
  }
}
```

**Slotの名前を変更:**
```graphql
mutation RenameSlot($sessionId: String!, $slotRefId: String!, $name: String!) {
  setSlotName(sessionId: $sessionId, slotRefId: $slotRefId, name: $name) {
    refId
    name
  }
}
```

**Slotの位置を変更:**
```graphql
mutation MoveSlot($sessionId: String!, $slotRefId: String!) {
  setSlotPosition(
    sessionId: $sessionId
    slotRefId: $slotRefId
    x: 1.0
    y: 2.0
    z: 3.0
    global: false
  ) {
    refId
    position { x y z }
  }
}
```

### 2. Componentの操作

**Componentをアタッチ:**
```graphql
mutation AttachComponent($sessionId: String!, $slotRefId: String!, $typeName: String!) {
  attachComponent(
    sessionId: $sessionId
    slotRefId: $slotRefId
    componentTypeName: $typeName
  ) {
    refId
    typeName
    syncMembers {
      name
      typeName
    }
  }
}
```

例えば `FrooxEngine.ValueField`1` や `FrooxEngine.MeshRenderer` などを指定。

**Componentを削除:**
```graphql
mutation RemoveComponent($sessionId: String!, $componentRefId: String!) {
  removeComponent(sessionId: $sessionId, componentRefId: $componentRefId) {
    success
    removedRefId
    removedTypeName
    error
  }
}
```

**Componentの有効/無効を切り替え:**
```graphql
mutation ToggleComponent($sessionId: String!, $componentRefId: String!, $enabled: Boolean!) {
  setComponentEnabled(
    sessionId: $sessionId
    componentRefId: $componentRefId
    enabled: $enabled
  ) {
    refId
    enabled
  }
}
```

### 3. プロパティの変更

**SyncField（値型）の変更:**
```graphql
mutation SetFieldValue($sessionId: String!, $componentRefId: String!) {
  setSyncFieldValue(
    sessionId: $sessionId
    componentRefId: $componentRefId
    memberName: "Value"
    value: "42"
  ) {
    success
    previousValue
    newValue
    error
  }
}
```

**値の形式:**
- 数値: `"42"`, `"3.14"`
- 文字列: `"hello"`
- bool: `"true"`, `"false"`
- float3: `{"x":1,"y":2,"z":3}` (JSON形式)

**SyncRef（参照型）の変更:**
```graphql
mutation SetRefTarget($sessionId: String!, $componentRefId: String!) {
  setSyncRefTarget(
    sessionId: $sessionId
    componentRefId: $componentRefId
    memberName: "Target"
    targetRefId: "ID12345678"
  ) {
    success
    previousRefId
    newRefId
    error
  }
}
```

参照を解除する場合は `targetRefId: null` を指定。

## よくあるユースケース

### ユースケース1: ワールド内の全オブジェクトを探索

```graphql
# Step 1: ワールドのRootSlotを取得
query {
  world(sessionId: "S-xxx") {
    rootSlot {
      refId
      children {
        refId
        name
        childCount
      }
    }
  }
}

# Step 2: 興味のあるSlotの子を再帰的に取得
query {
  slot(sessionId: "S-xxx", refId: "取得したRefId") {
    children {
      refId
      name
      childCount
      componentCount
    }
  }
}
```

### ユースケース2: 特定のComponentタイプを持つSlotを探す

```graphql
# まずSlotを取得し、componentsをチェック
query {
  slot(sessionId: "S-xxx", refId: "xxx") {
    name
    components {
      typeName
      refId
    }
    children {
      name
      refId
      components {
        typeName
        refId
      }
    }
  }
}
```

### ユースケース3: オブジェクトの配置を変更

```graphql
# 位置を変更
mutation {
  setSlotPosition(
    sessionId: "S-xxx"
    slotRefId: "xxx"
    x: 0
    y: 1.5
    z: 0
    global: true
  ) {
    globalPosition { x y z }
  }
}

# 回転を変更
mutation {
  setSlotRotation(
    sessionId: "S-xxx"
    slotRefId: "xxx"
    x: 0
    y: 0.707
    z: 0
    w: 0.707
    global: false
  ) {
    rotation { x y z w }
  }
}

# スケールを変更
mutation {
  setSlotScale(
    sessionId: "S-xxx"
    slotRefId: "xxx"
    x: 2.0
    y: 2.0
    z: 2.0
  ) {
    scale { x y z }
  }
}
```

### ユースケース4: 新しいオブジェクトを作成

```graphql
# Step 1: 親Slotの下に新しいSlotを作成
mutation {
  addChildSlot(
    sessionId: "S-xxx"
    parentSlotRefId: "parentRefId"
    name: "MyNewObject"
  ) {
    refId
    name
  }
}

# Step 2: 必要なComponentをアタッチ
mutation {
  attachComponent(
    sessionId: "S-xxx"
    slotRefId: "新しく作成したSlotのRefId"
    componentTypeName: "FrooxEngine.MeshRenderer"
  ) {
    refId
    typeName
  }
}

# Step 3: Componentのプロパティを設定
mutation {
  setSyncFieldValue(
    sessionId: "S-xxx"
    componentRefId: "xxx"
    memberName: "Enabled"
    value: "true"
  ) {
    success
  }
}
```

### ユースケース5: 参照関係を辿る

```graphql
# SyncRefの参照先を取得
query {
  component(sessionId: "S-xxx", refId: "xxx") {
    syncMembers {
      name
      ... on SyncRefType {
        targetRefId
        targetAsSlot {
          refId
          name
          position { x y z }
        }
        targetAsComponent {
          refId
          typeName
        }
      }
    }
  }
}
```

## 型情報

### スカラー型

| 型名 | 説明 | 例 |
|------|------|-----|
| `String` | 文字列 | `"hello"` |
| `Int` | 整数 | `42` |
| `Float` | 浮動小数点 | `3.14` |
| `Boolean` | 真偽値 | `true`, `false` |

### オブジェクト型

#### SlotType
| フィールド | 型 | 説明 |
|-----------|-----|------|
| `refId` | String! | RefID |
| `name` | String! | Slot名 |
| `active` | Boolean! | 有効/無効 |
| `position` | Float3Type! | ローカル位置 |
| `globalPosition` | Float3Type! | グローバル位置 |
| `rotation` | FloatQType! | ローカル回転 |
| `scale` | Float3Type! | ローカルスケール |
| `parent` | SlotType | 親Slot |
| `children` | [SlotType!]! | 子Slot一覧 |
| `childCount` | Int! | 子Slot数 |
| `components` | [ComponentType!]! | 全Component一覧 |
| `componentCount` | Int! | Component数 |
| `componentByType(typeName)` | ComponentType | 型名で検索（最初の1件） |
| `componentsByType(typeName)` | [ComponentType!]! | 型名で検索（全件） |
| `syncMembers` | [ISyncMemberType!]! | 全SyncMember一覧 |
| `syncMemberByName(name)` | ISyncMemberType | 名前でSyncMember検索 |

#### ComponentType
| フィールド | 型 | 説明 |
|-----------|-----|------|
| `refId` | String! | RefID |
| `typeName` | String! | 型名（短縮形） |
| `typeFullName` | String! | 完全修飾型名 |
| `enabled` | Boolean! | 有効/無効 |
| `slot` | SlotType! | 所属Slot |
| `syncMemberCount` | Int! | SyncMember数 |
| `syncMembers` | [ISyncMemberType!]! | 全SyncMember一覧 |
| `syncMemberByName(name)` | ISyncMemberType | 名前でSyncMember検索 |
| `syncMemberByIndex(index)` | ISyncMemberType | インデックスでSyncMember検索 |

#### ComponentTypeInfo
| フィールド | 型 | 説明 |
|-----------|-----|------|
| `name` | String! | 型名（短縮形） |
| `fullName` | String! | 完全修飾型名 |
| `category` | String | カテゴリ（Core, UIX, ProtoFlux, Avatar, IK, LogiX） |
| `isGeneric` | Boolean! | ジェネリック型かどうか |
| `genericDefinition` | String | ジェネリック定義名（ジェネリック型の場合のみ） |

#### Float3Type（位置・スケール）
```graphql
{
  x: Float!
  y: Float!
  z: Float!
}
```

#### FloatQType（回転・クォータニオン）
```graphql
{
  x: Float!
  y: Float!
  z: Float!
  w: Float!
}
```

### SyncMemberの型判別

```graphql
syncMembers {
  __typename  # "SyncFieldType" or "SyncRefType" or "GenericSyncMemberType"
  name
  index
  typeName
  isLinked
  ... on SyncFieldType {
    # SyncFieldの場合のみ
    valueType
    value
    valueAsString
    valueAsFloat
    valueAsInt
    valueAsBool
  }
  ... on SyncRefType {
    # SyncRefの場合のみ
    targetRefId
    targetTypeName
    targetAsSlot { ... }
    targetAsComponent { ... }
  }
}
```

## 注意事項

1. **RefID形式**: RefIDは `ID` で始まる16進数文字列（例: `ID8A2B3C4D`）
2. **SessionId形式**: SessionIdは `S-` で始まる文字列
3. **スレッド安全**: 全ての操作はワールドスレッド上で実行されるため安全
4. **存在確認**: Slot/Componentが存在しない場合は `null` が返る
5. **値の形式**: `setSyncFieldValue`の`value`は文字列で渡す。複合型はJSON形式
6. **型名**: `attachComponent`の型名は完全修飾名（例: `FrooxEngine.MeshRenderer`）

## トラブルシューティング

### "Session not found" エラー
- `sessionId`が正しいか確認
- ワールドが起動しているか確認
- `worlds`クエリで利用可能なセッション一覧を取得

### "Slot/Component not found" エラー
- `refId`が正しいか確認
- 対象オブジェクトが削除されていないか確認
- 正しい`sessionId`を使用しているか確認

### プロパティの変更が反映されない
- `memberName`が正確か確認（大文字小文字を区別）
- `value`の形式が正しいか確認
- Mutationの`success`フィールドを確認

### "Component type not found" エラー
- 型名が完全修飾名か確認（例: `FrooxEngine.MeshRenderer`）
- ジェネリック型の場合は `` `1 `` などの修飾子を含める
