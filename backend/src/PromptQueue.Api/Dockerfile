FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PromptQueue.Domain/PromptQueue.Domain.csproj src/PromptQueue.Domain/
COPY src/PromptQueue.Infrastructure/PromptQueue.Infrastructure.csproj src/PromptQueue.Infrastructure/
COPY src/PromptQueue.Api/PromptQueue.Api.csproj src/PromptQueue.Api/
RUN dotnet restore src/PromptQueue.Api/PromptQueue.Api.csproj
COPY . .
RUN dotnet publish src/PromptQueue.Api/PromptQueue.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PromptQueue.Api.dll"]
