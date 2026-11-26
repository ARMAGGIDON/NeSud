FROM node:22-alpine AS web
WORKDIR /web
COPY src/NeSud.Server/package.json src/NeSud.Server/prepare-web.mjs ./
RUN npm install --ignore-scripts --no-audit --no-fund && npm run prepare-web
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
COPY --from=web /web/wwwroot/vendor/ src/NeSud.Server/wwwroot/vendor/
RUN dotnet publish src/NeSud.Server/NeSud.Server.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 Database=/data/server.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "NeSud.Server.dll"]
