# syntax=docker/dockerfile:1.7
# Dockerfile unico com tres alvos: api, worker e migrator (mesma base de codigo, imagens enxutas).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaura dependencias primeiro (camada em cache enquanto os .csproj nao mudam).
COPY Directory.Build.props Orders.slnx ./
COPY src/Orders.Domain/*.csproj src/Orders.Domain/
COPY src/Orders.Contracts/*.csproj src/Orders.Contracts/
COPY src/Orders.Application/*.csproj src/Orders.Application/
COPY src/Orders.Infrastructure/*.csproj src/Orders.Infrastructure/
COPY src/Orders.Api/*.csproj src/Orders.Api/
COPY src/Orders.Worker/*.csproj src/Orders.Worker/
COPY src/Orders.Migrator/*.csproj src/Orders.Migrator/
RUN for p in Api Worker Migrator; do dotnet restore src/Orders.$p/Orders.$p.csproj; done

COPY .editorconfig ./
COPY src/ src/
RUN dotnet publish src/Orders.Api/Orders.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/Orders.Worker/Orders.Worker.csproj -c Release -o /out/worker --no-restore \
 && dotnet publish src/Orders.Migrator/Orders.Migrator.csproj -c Release -o /out/migrator --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
# curl e usado apenas pelos healthchecks do Docker Compose.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
USER $APP_UID
WORKDIR /app

FROM runtime-base AS api
COPY --from=build /out/api ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.Api.dll"]

FROM runtime-base AS worker
COPY --from=build /out/worker ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
USER $APP_UID
WORKDIR /app
COPY --from=build /out/migrator ./
ENTRYPOINT ["dotnet", "Orders.Migrator.dll"]
