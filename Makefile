BUF_VERSION := v1.35.1
BIN_DIR := $(shell pwd)/bin

# tools
buf := go run github.com/bufbuild/buf/cmd/buf@$(BUF_VERSION)

.PHONY: lint
lint: lint.proto lint.headless

.PHONY: install.tools
install.tools:
	mkdir -p $(BIN_DIR);
	@GOBIN=$(BIN_DIR) go install github.com/bufbuild/buf/cmd/buf@$(BUF_VERSION);

.PHONY: build.proto
build.proto:
	$(buf) generate

.PHONY: lint.proto
lint.proto:
	$(buf) format -w
	$(buf) lint

.PHONY: lint.headless
lint.headless:
	cd Headless
	dotnet format

.PHONY: build.docker
build.docker:
	docker build -t ghcr.io/hantabaru1014/baru-reso-headless-container .

.PHONY: build.prepatcher
build.prepatcher:
	dotnet publish -c Release -o ./bin/prepatch ./EnginePrePatcher/EnginePrePatcher.csproj

.PHONY: prepatch
prepatch: build.prepatcher
	dotnet ./bin/prepatch/EnginePrePatcher.dll ./Resonite/Headless

.PHONY: download.resonite
download.resonite:
	./scripts/download-resonite.sh

.PHONY: download.resonite-pre
download.resonite-pre:
	USE_PRERELEASE=true ./scripts/download-resonite.sh

.PHONY: download.resonite-depot
download.resonite-depot:
	USE_DEPOT_DOWNLOADER=true ./scripts/download-resonite.sh

.PHONY: download.resonite-pre-depot
download.resonite-pre-depot:
	USE_PRERELEASE=true USE_DEPOT_DOWNLOADER=true ./scripts/download-resonite.sh

.PHONY: evans
evans:
	evans --proto proto/headless/v1/headless.proto --host localhost -p 5000 repl

# Integration tests
# Note: Cross-architecture testing via QEMU is not supported.
#       Resonite crashes with SIGSEGV under QEMU emulation.
#       Run tests on native architecture only. CI uses native runners for both amd64 and arm64.
TEST_IMAGE_TAG ?= ghcr.io/hantabaru1014/baru-reso-headless-container:test
NATIVE_ARCH := $(shell uname -m | sed 's/x86_64/amd64/' | sed 's/aarch64/arm64/')

.PHONY: test
test:
	docker build --platform linux/$(NATIVE_ARCH) -t $(TEST_IMAGE_TAG)-$(NATIVE_ARCH) .
	TEST_IMAGE_TAG=$(TEST_IMAGE_TAG)-$(NATIVE_ARCH) dotnet test ./Headless.Tests/Headless.Tests.csproj --logger "console;verbosity=detailed"
