# baru-reso-headless-container

An unofficial custom headless client for [Resonite](https://resonite.com/), packaged as a container image and controllable from outside via a gRPC API.

It replaces the interactive console of the official headless client with a [gRPC API](./proto/headless/v1/headless.proto), so external tools such as web dashboards and bots can manage sessions.

## Features

Most of what the official interactive console offers is available as RPCs: starting / stopping / saving sessions, updating session parameters, kick / ban / role management, friend requests and messaging, dynamic impulses, and more. Startup settings are injected as JSON through an environment variable, and the official headless `Config.json` also works as-is. Images are built for both amd64 and arm64.

On top of that, it has several features the official headless client doesn't have:

- **Host event streaming** — session started / ended, user joined / left, world saved, and session parameter changes are delivered as a server-streamed event feed. Events carry monotonically increasing ULID ids and are buffered on the host, so a controller can reconnect and resume from the last id it saw without missing events, even across container restarts.
- **World export** — download the world of a running session via the API as a `.resonitepackage` or BRSON stream.
- **Save as** — save a running world as a new record (including worlds started from a preset) and get the resulting record URL back.
- **Runtime host settings** — change tick rate, max concurrent asset transfers, auto-spawn items, and allow / deny URL host access (HTTP / WebSocket / OSC) while running, without editing the config and restarting.
- **Startup config snapshot** — fetch the current host state as a `StartupConfig`, so a controller can persist it and restore the same state after a restart.
- **ResoniteLink over gRPC** — ResoniteLink connections are bridged through a gRPC bidirectional stream, so tools can connect without the per-world WebSocket server (and extra open ports) of the official `enableResoniteLink`.
- **Cloud queries** — search users, fetch world info, get account info including storage usage, list contacts, and read contact message history.
- **Per-user join grants** — grant a specific user permission to join a session without sending them an invite message (like an invite, but access-only), via the API or the `joinAllowedUserIds` startup parameter.

## Getting Started

> [!IMPORTANT]
> Prebuilt images are not distributed because they contain Resonite assemblies. Build the image locally, or fork this repository to build with GitHub Actions. See [Building the image](./docs/build-image.md) for instructions.

Run your built image using `docker-compose.sample.yml` as a reference:

```yaml
services:
  app:
    image: <YOUR_IMAGE_HERE>
    ports:
      - "5000:5000"
    environment:
      - HeadlessUserCredential=<YOUR_HEADLESS_CREDENTIAL_HERE>
      - HeadlessUserPassword=<YOUR_HEADLESS_PASSWORD_HERE>
```

Once started, the gRPC API listens on port 5000. To open worlds automatically on startup, set the `StartupConfig` environment variable. See the [Configuration reference](./docs/configuration.md) for details.

## Documentation

- [Building the image](./docs/build-image.md) — build locally, or build via CI on your fork
- [Configuration reference](./docs/configuration.md) — environment variables and StartupConfig
- [Development guide](./docs/development.md) — dev environment setup, tests, and trying out the gRPC API
- [Release flow](./docs/release.md) — versioning and release process
