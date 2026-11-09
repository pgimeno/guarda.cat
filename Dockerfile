FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /source

COPY global.json ./
COPY src/GuardaCat.Contracts/GuardaCat.Contracts.csproj src/GuardaCat.Contracts/
COPY src/GuardaCat.Client/GuardaCat.Client.csproj src/GuardaCat.Client/
COPY src/GuardaCat.Server/GuardaCat.Server.csproj src/GuardaCat.Server/
RUN dotnet restore src/GuardaCat.Server/GuardaCat.Server.csproj

COPY src/ src/
RUN dotnet publish src/GuardaCat.Server/GuardaCat.Server.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app/publish ./
RUN mkdir -p App_Data && chown -R $APP_UID:$APP_UID /app
USER $APP_UID
ENTRYPOINT ["dotnet", "GuardaCat.Server.dll"]
CMD ["--web"]
