BUF_VERSION := v1.35.1
BIN_DIR := $(shell pwd)/bin

# tools
buf := go run github.com/bufbuild/buf/cmd/buf@$(BUF_VERSION)

.PHONY: install.tools
install.tools:
	mkdir -p $(BIN_DIR);
	@GOBIN=$(BIN_DIR) go install github.com/bufbuild/buf/cmd/buf@$(BUF_VERSION);

.PHONY: build.proto
build.proto:
	rm -r ./proto/**
	rm ./Headless/Protos/*
	$(buf) generate

.PHONY: build.docker
build.docker:
	docker build -t ghcr.io/hantabaru1014/baru-reso-headless-container .