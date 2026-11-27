# -------------------------------
# STEP 1: Build stage (.NET 9 stable)
# -------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
ARG LEGAL_RELEASE_MODE=production

# Copy everything
COPY . .

# Restore dependencies
RUN dotnet restore HeartLog.sln
RUN dotnet restore tools/HeartLog.LegalVerifier/HeartLog.LegalVerifier.csproj

# The checkout must already contain the recorded submodule commit. The build
# never fetches a branch or embeds credentials in an image layer.
RUN dotnet run --project tools/HeartLog.LegalVerifier/HeartLog.LegalVerifier.csproj --no-restore -- legal-release.json shared/heartlog-legal "$LEGAL_RELEASE_MODE" docs/legal/snapshots

# Publish API
RUN dotnet publish HeartLog.Api/HeartLog.Api.csproj -c Release -o /app/publish -p:LegalReleaseMode="$LEGAL_RELEASE_MODE"


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
