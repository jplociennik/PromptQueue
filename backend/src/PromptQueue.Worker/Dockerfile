FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PromptQueue.Domain/PromptQueue.Domain.csproj src/PromptQueue.Domain/
COPY src/PromptQueue.Infrastructure/PromptQueue.Infrastructure.csproj src/PromptQueue.Infrastructure/
COPY src/PromptQueue.Worker/PromptQueue.Worker.csproj src/PromptQueue.Worker/
RUN dotnet restore src/PromptQueue.Worker/PromptQueue.Worker.csproj
COPY . .
RUN dotnet publish src/PromptQueue.Worker/PromptQueue.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PromptQueue.Worker.dll"]
