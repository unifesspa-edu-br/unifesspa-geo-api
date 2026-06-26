# ---- Restore + Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json Unifesspa.Geo.slnx .editorconfig ./

COPY src/shared/ src/shared/
COPY src/geo/ src/geo/

RUN dotnet restore src/geo/Unifesspa.Geo.API/Unifesspa.Geo.API.csproj --locked-mode

COPY src/shared/ src/shared/
COPY src/geo/ src/geo/

RUN dotnet publish src/geo/Unifesspa.Geo.API/Unifesspa.Geo.API.csproj \
    -c Release \
    -o /app/publish

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -r appuser && useradd -r -g appuser -s /sbin/nologin appuser

COPY --from=build /app/publish .

USER appuser

EXPOSE 8080

ENTRYPOINT ["dotnet", "Unifesspa.Geo.API.dll"]
