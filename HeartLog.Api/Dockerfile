# Build stage
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY *.sln .
COPY HeartLog.Api/*.csproj ./HeartLog.Api/
COPY HeartLog.BLL/*.csproj ./HeartLog.BLL/
COPY HeartLog.DAL/*.csproj ./HeartLog.DLL/

RUN dotnet restore

COPY . .
WORKDIR /src/HeartLog.Api
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HeartLog.Api.dll"]
