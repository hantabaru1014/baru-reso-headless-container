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
WORKDIR /src
COPY ["Headless/Headless.csproj", "Headless/"]
RUN dotnet restore "./Headless/Headless.csproj"
COPY --from=build-patcher /app/publish ./bin/prepatch
COPY ./Headless ./Headless
WORKDIR "/src/Headless"
RUN --mount=type=bind,source=Resonite/Headless,target=../Resonite/Headless,rw \
    dotnet ../bin/prepatch/EnginePrePatcher.dll ../Resonite/Headless && dotnet publish "./Headless.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends libassimp5 libfreeimage3 libfreetype6 libopus0 libbrotli1 zlib1g && rm -rf /var/lib/apt/lists/*
USER app
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
COPY --chown=app:app ./native-libs/${TARGETARCH}/* ./
CMD ["dotnet", "Headless.dll"]