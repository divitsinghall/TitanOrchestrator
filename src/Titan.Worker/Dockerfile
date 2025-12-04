FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files
COPY src/TitanOrchestrator.sln ./src/
COPY src/Titan.Master/Titan.Master.csproj ./src/Titan.Master/
COPY src/Titan.Worker/Titan.Worker.csproj ./src/Titan.Worker/
COPY src/Titan.Shared/Titan.Shared.csproj ./src/Titan.Shared/
COPY src/Titan.Tests/Titan.Tests.csproj ./src/Titan.Tests/

# Restore dependencies
RUN dotnet restore ./src/Titan.Worker/Titan.Worker.csproj

# Copy the rest of the source code
COPY src/ ./src/

# Build and publish
WORKDIR /app/src/Titan.Worker
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Titan.Worker.dll"]
