# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Web/Web.csproj",                         "Web/"]
COPY ["Application/Application.csproj",         "Application/"]
COPY ["Domain/Domain.csproj",                   "Domain/"]
COPY ["Infrastructure/Infrastructure.csproj",   "Infrastructure/"]
COPY ["MasterDb/MasterDb.csproj",               "MasterDb/"]
COPY ["Base/Base.csproj",                       "Base/"]
RUN dotnet restore "Web/Web.csproj"

COPY . .
WORKDIR /src/Web
RUN dotnet publish "Web.csproj" -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5001

ENTRYPOINT ["dotnet", "Web.dll"]
