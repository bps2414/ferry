# Ferry self-hosted: servidor web + 7-Zip num container Linux (amd64 e arm64).
# docker build -t ferry .   ·   docker compose up -d (ver docker-compose.yml)

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY core/ core/
COPY web/ web/
COPY app/fonts/ app/fonts/
RUN dotnet publish web -c Release -a "$TARGETARCH" --self-contained false -p:UseAppHost=false -o /out

# aspnet:10.0 é Ubuntu 24.04: 7-Zip do próprio Ubuntu, com o codec RAR (7zip-rar, multiverse)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends 7zip 7zip-rar && rm -rf /var/lib/apt/lists/*
COPY --from=build /out /app
ENV FERRY_DATA=/data FERRY_GAMES=/games FERRY_PORT=8021 LC_ALL=C.UTF-8
RUN mkdir -p /data /games
VOLUME ["/data", "/games"]
EXPOSE 8021
WORKDIR /app
ENTRYPOINT ["dotnet", "ferry.dll"]
