# Stage 1: Build React frontend
FROM node:20-alpine AS frontend-build
WORKDIR /app
COPY src/Eod.TestRunner/ClientApp/package*.json ./
RUN npm install
COPY src/Eod.TestRunner/ClientApp/ ./
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Eod.TestRunner/Eod.TestRunner.csproj", "Eod.TestRunner/"]
COPY ["src/Eod.Shared/Eod.Shared.csproj", "Eod.Shared/"]
COPY ["Directory.Build.props", "."]
RUN dotnet restore "Eod.TestRunner/Eod.TestRunner.csproj"
COPY src/Eod.TestRunner/ Eod.TestRunner/
COPY src/Eod.Shared/ Eod.Shared/
WORKDIR "/src/Eod.TestRunner"
RUN dotnet build "Eod.TestRunner.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Eod.TestRunner.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8083

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
COPY --from=frontend-build /wwwroot ./wwwroot
ENTRYPOINT ["dotnet", "Eod.TestRunner.dll"]
