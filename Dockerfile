FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ./Resonite/Headless/net8.0 ./Headless/net8.0
COPY ["Headless/Headless.csproj", "Headless/"]
RUN dotnet restore "./Headless/Headless.csproj"
COPY . .
WORKDIR "/src/Headless"
# RUN dotnet build "./Headless.csproj" -c $BUILD_CONFIGURATION -o /app/build

# FROM build AS publish
# ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Headless.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends libassimp5 libfreeimage3 libfreetype6 libopus0 libbrotli1 zlib1g && rm -rf /var/lib/apt/lists/*
USER app
WORKDIR /app
# COPY --from=publish /app/publish .
COPY --from=build /app/publish .
CMD ["dotnet", "Headless.dll"]