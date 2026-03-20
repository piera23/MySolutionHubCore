# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia i file di progetto e ripristina le dipendenze (layer cache-friendly)
COPY ["Api/Api.csproj",                         "Api/"]
COPY ["Application/Application.csproj",         "Application/"]
COPY ["Domain/Domain.csproj",                   "Domain/"]
COPY ["Infrastructure/Infrastructure.csproj",   "Infrastructure/"]
COPY ["MasterDb/MasterDb.csproj",               "MasterDb/"]
COPY ["Base/Base.csproj",                       "Base/"]
RUN dotnet restore "Api/Api.csproj"

# Copia il codice sorgente e pubblica
COPY . .
WORKDIR /src/Api
RUN dotnet publish "Api.csproj" -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Utente non-root per sicurezza
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000

ENTRYPOINT ["dotnet", "Api.dll"]
