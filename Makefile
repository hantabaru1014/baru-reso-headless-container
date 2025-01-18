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

./DepotDownloader:
	./scripts/download-depot-downloader.sh

.PHONY: install.resonite
install.resonite: ./DepotDownloader
	./scripts/download-resonite.sh
