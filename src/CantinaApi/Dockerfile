FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore on its own layer so packages stay cached until the project file changes.
COPY src/CantinaApi/CantinaApi.csproj src/CantinaApi/
RUN dotnet restore src/CantinaApi/CantinaApi.csproj

COPY src/CantinaApi/ src/CantinaApi/
RUN dotnet publish src/CantinaApi/CantinaApi.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The aspnet image ships a non-root app user whose id is in APP_UID.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CantinaApi.dll"]
