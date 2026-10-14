FROM node:24-bookworm-slim AS web
WORKDIR /source/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
COPY docs/features.json /source/docs/features.json
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/ src/
COPY --from=web /source/src/Tangle.Web/wwwroot/ src/Tangle.Web/wwwroot/
RUN dotnet restore src/Tangle.Host/Tangle.Host.csproj --locked-mode \
    && dotnet publish src/Tangle.Host/Tangle.Host.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    Tangle__Address=http://0.0.0.0:8080 \
    Tangle__DatabasePath=/data/tangle.db \
    Auth__KeyDirectory=/keys
RUN mkdir /data /keys && chown "$APP_UID" /data /keys
COPY --from=build /app/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tangle.Host.dll"]
