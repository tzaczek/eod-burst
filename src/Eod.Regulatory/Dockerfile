# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Directory.Build.props", "."]
COPY ["src/Eod.Shared/Eod.Shared.csproj", "src/Eod.Shared/"]
COPY ["src/Eod.Regulatory/Eod.Regulatory.csproj", "src/Eod.Regulatory/"]

# Restore dependencies
RUN dotnet restore "src/Eod.Regulatory/Eod.Regulatory.csproj"

# Copy source code
COPY ["src/Eod.Shared/", "src/Eod.Shared/"]
COPY ["src/Eod.Regulatory/", "src/Eod.Regulatory/"]

# Build
WORKDIR "/src/src/Eod.Regulatory"
RUN dotnet build "Eod.Regulatory.csproj" -c Release -o /app/build

# Publish
RUN dotnet publish "Eod.Regulatory.csproj" -c Release -o /app/publish /p:UseAppHost=false

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
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8082/health || exit 1

# Expose port
EXPOSE 8082

# Entry point
ENTRYPOINT ["dotnet", "Eod.Regulatory.dll"]
