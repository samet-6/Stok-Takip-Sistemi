# Build context is the repo root (project references need all of src/).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project manifests first so `restore` is cached until they change.
COPY src/StokTakip.Domain/StokTakip.Domain.csproj src/StokTakip.Domain/
COPY src/StokTakip.Application/StokTakip.Application.csproj src/StokTakip.Application/
COPY src/StokTakip.Infrastructure/StokTakip.Infrastructure.csproj src/StokTakip.Infrastructure/
COPY src/StokTakip.Api/StokTakip.Api.csproj src/StokTakip.Api/
# Restore the API's own graph, NOT the solution: the solution also lists tests/, whose
# csproj files are deliberately not in this image. Restoring the .sln here broke the build
# the day the test projects were added to it.
RUN dotnet restore src/StokTakip.Api/StokTakip.Api.csproj

COPY src/ src/
RUN dotnet publish src/StokTakip.Api -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "StokTakip.Api.dll"]
