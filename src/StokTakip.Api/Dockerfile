# Build context is the repo root (project references need all of src/).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + project manifests first so `restore` is cached until they change.
COPY StokTakip.sln ./
COPY src/StokTakip.Domain/StokTakip.Domain.csproj src/StokTakip.Domain/
COPY src/StokTakip.Application/StokTakip.Application.csproj src/StokTakip.Application/
COPY src/StokTakip.Infrastructure/StokTakip.Infrastructure.csproj src/StokTakip.Infrastructure/
COPY src/StokTakip.Api/StokTakip.Api.csproj src/StokTakip.Api/
RUN dotnet restore StokTakip.sln

COPY src/ src/
RUN dotnet publish src/StokTakip.Api -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "StokTakip.Api.dll"]
