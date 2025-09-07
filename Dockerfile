# -------------------------------
# STEP 1: Build stage with .NET 9.0
# -------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-preview AS build
WORKDIR /src

# Copy everything
COPY . .

# Restore packages
RUN dotnet restore HeartLog.sln

# Publish only the API project
RUN dotnet publish HeartLog.Api/HeartLog.Api.csproj -c Release -o /app/publish

# -------------------------------
# STEP 2: Runtime stage
# -------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-preview AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "HeartLog.Api.dll"]
