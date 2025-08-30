# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY HeartLog.Api/HeartLog.Api.csproj HeartLog.Api/
COPY HeartLog.BLL/HeartLog.BLL.csproj HeartLog.BLL/
COPY HeartLog.DAL/HeartLog.DAL.csproj HeartLog.DAL/

# Restore dependencies
RUN dotnet restore HeartLog.Api/HeartLog.Api.csproj

# Copy the full source
COPY . .

# Publish the API project
WORKDIR /src/HeartLog.Api
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "HeartLog.Api.dll"]