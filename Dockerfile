FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PlayerQueueService.sln ./
COPY PlayerQueueService.Api/PlayerQueueService.Api.csproj PlayerQueueService.Api/
COPY PlayerQueueService.Api.Tests/PlayerQueueService.Api.Tests.csproj PlayerQueueService.Api.Tests/
RUN dotnet restore PlayerQueueService.Api/PlayerQueueService.Api.csproj

COPY . .
RUN dotnet publish PlayerQueueService.Api/PlayerQueueService.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "PlayerQueueService.Api.dll"]
