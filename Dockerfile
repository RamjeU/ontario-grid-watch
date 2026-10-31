# Multi-stage build: the React UI and the .NET API are built in separate SDK images, and only the
# published output goes into the small ASP.NET runtime image, which runs as a non-root user.

# 1. Front end
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# vite.config.ts writes the build into ../src/GridWatch.Api/wwwroot
RUN npm run build

# 2. API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GridWatch.slnx global.json ./
COPY src/GridWatch.Core/GridWatch.Core.csproj src/GridWatch.Core/
COPY src/GridWatch.Infrastructure/GridWatch.Infrastructure.csproj src/GridWatch.Infrastructure/
COPY src/GridWatch.Api/GridWatch.Api.csproj src/GridWatch.Api/
RUN dotnet restore src/GridWatch.Api/GridWatch.Api.csproj
COPY src/ src/
COPY --from=web /src/src/GridWatch.Api/wwwroot src/GridWatch.Api/wwwroot
RUN dotnet publish src/GridWatch.Api/GridWatch.Api.csproj -c Release -o /app --no-restore

# 3. Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Writable folder for the SQLite file when no SQL Server is configured.
RUN mkdir -p /data && chown "$APP_UID" /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__GridWatch="Data Source=/data/gridwatch.db"
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "GridWatch.Api.dll"]
