FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/ ./src/
RUN dotnet publish src/HitPan.API/HitPan.API.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /out .
ENTRYPOINT ["dotnet", "HitPan.API.dll"]
