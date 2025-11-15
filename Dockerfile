# -------------------------------
# STEP 1: Build stage (.NET 9 stable)
# -------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything
COPY . .

# Restore dependencies
RUN dotnet restore HeartLog.sln

# Publish API
RUN dotnet publish HeartLog.Api/HeartLog.Api.csproj -c Release -o /app/publish


# -------------------------------
# STEP 2: Runtime stage (with CA certificates)
# -------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install CA certificates for Neon (TLS)
RUN apt-get update && apt-get install -y ca-certificates && update-ca-certificates

# Copy published app
COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "HeartLog.Api.dll"]
