FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/DistributedGitStorage.StorageNode/DistributedGitStorage.StorageNode.csproj src/DistributedGitStorage.StorageNode/
RUN dotnet restore src/DistributedGitStorage.StorageNode/DistributedGitStorage.StorageNode.csproj

COPY src/DistributedGitStorage.StorageNode/ src/DistributedGitStorage.StorageNode/
RUN dotnet publish src/DistributedGitStorage.StorageNode/DistributedGitStorage.StorageNode.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends git curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__RootPath=/var/lib/git/repositories
EXPOSE 8080

ENTRYPOINT ["dotnet", "DistributedGitStorage.StorageNode.dll"]
