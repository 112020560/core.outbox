FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Src/SmartCore.Outbox/SmartCore.Outbox.csproj Src/SmartCore.Outbox/
COPY Src/OutboxWorker/OutboxWorker.csproj Src/OutboxWorker/
RUN dotnet restore Src/OutboxWorker/OutboxWorker.csproj

COPY Src/SmartCore.Outbox/ Src/SmartCore.Outbox/
COPY Src/OutboxWorker/ Src/OutboxWorker/
RUN dotnet publish Src/OutboxWorker/OutboxWorker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OutboxWorker.dll"]
