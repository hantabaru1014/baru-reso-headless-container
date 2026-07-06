# baru-reso-headless-container

An unofficial custom headless client for [Resonite](https://resonite.com/), packaged as a container image and controllable from outside via a gRPC API.

It replaces the interactive console of the official headless client with a gRPC API, so external tools such as web dashboards and bots can manage sessions.

## Features

- Control everything via the [gRPC API](./proto/headless/v1/headless.proto): start / stop / save sessions, manage users (kick / ban / role changes), handle friend requests and messages, and more
- Subscribe to session events and logs via server streaming
- Inject startup settings (worlds to open on boot, etc.) as JSON through an environment variable — the official headless `Config.json` also works as-is
- Multi-architecture support: amd64 / arm64

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
