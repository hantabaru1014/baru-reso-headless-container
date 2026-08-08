# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build-patcher
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EnginePrePatcher/EnginePrePatcher.csproj", "EnginePrePatcher/"]
RUN dotnet restore "./EnginePrePatcher/EnginePrePatcher.csproj"
COPY ./EnginePrePatcher ./EnginePrePatcher
WORKDIR "/src/EnginePrePatcher"
RUN dotnet publish "./EnginePrePatcher.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# release tag 由来のアプリバージョン。builder image / CI が渡す
ARG APP_VERSION=0.0.0-dev
WORKDIR /src
COPY ["Headless/Headless.csproj", "Headless/"]
RUN dotnet restore "./Headless/Headless.csproj"
COPY --from=build-patcher /app/publish ./bin/prepatch
COPY ./Headless ./Headless
WORKDIR "/src/Headless"
RUN --mount=type=bind,source=Resonite/Headless,target=../Resonite/Headless,rw \
    dotnet ../bin/prepatch/EnginePrePatcher.dll ../Resonite/Headless && dotnet publish "./Headless.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG TARGETARCH
# libmsquic は Microsoft のリポジトリにしか無い。URL は base image の OS (Ubuntu 24.04) に合わせること
ADD https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb /tmp/packages-microsoft-prod.deb
RUN dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends libpng16-16t64 libmsquic \
    && rm -rf /var/lib/apt/lists/*
USER app
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
COPY --chown=app:app ./native-libs/${TARGETARCH}/* ./
CMD ["dotnet", "Headless.dll"]
