FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/NeSud.Server/NeSud.Server.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 Database=/data/server.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "NeSud.Server.dll"]
