FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/DistributedGitStorage.Data/DistributedGitStorage.Data.csproj src/DistributedGitStorage.Data/
COPY src/DistributedGitStorage.Services/DistributedGitStorage.Services.csproj src/DistributedGitStorage.Services/
COPY src/DistributedGitStorage.Web/DistributedGitStorage.Web.csproj src/DistributedGitStorage.Web/
RUN dotnet restore src/DistributedGitStorage.Web/DistributedGitStorage.Web.csproj

COPY src/DistributedGitStorage.Data/ src/DistributedGitStorage.Data/
COPY src/DistributedGitStorage.Services/ src/DistributedGitStorage.Services/
COPY src/DistributedGitStorage.Web/ src/DistributedGitStorage.Web/
RUN dotnet publish src/DistributedGitStorage.Web/DistributedGitStorage.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DistributedGitStorage.Web.dll"]
