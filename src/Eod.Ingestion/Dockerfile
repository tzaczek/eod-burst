# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Directory.Build.props", "."]
COPY ["src/Eod.Shared/Eod.Shared.csproj", "src/Eod.Shared/"]
COPY ["src/Eod.Ingestion/Eod.Ingestion.csproj", "src/Eod.Ingestion/"]

# Restore dependencies
RUN dotnet restore "src/Eod.Ingestion/Eod.Ingestion.csproj"

# Copy source code
COPY ["src/Eod.Shared/", "src/Eod.Shared/"]
COPY ["src/Eod.Ingestion/", "src/Eod.Ingestion/"]

# Build
WORKDIR "/src/src/Eod.Ingestion"
RUN dotnet build "Eod.Ingestion.csproj" -c Release -o /app/build

# Publish
RUN dotnet publish "Eod.Ingestion.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Environment
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Expose port
EXPOSE 8080

# Entry point
ENTRYPOINT ["dotnet", "Eod.Ingestion.dll"]
