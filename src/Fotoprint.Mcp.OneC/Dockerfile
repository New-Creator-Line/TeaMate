FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY src/Fotoprint.Core/Fotoprint.Core.csproj src/Fotoprint.Core/
COPY src/Fotoprint.Mcp.OneC/Fotoprint.Mcp.OneC.csproj src/Fotoprint.Mcp.OneC/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet restore src/Fotoprint.Mcp.OneC/Fotoprint.Mcp.OneC.csproj

COPY src/Fotoprint.Core/ src/Fotoprint.Core/
COPY src/Fotoprint.Mcp.OneC/ src/Fotoprint.Mcp.OneC/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet publish src/Fotoprint.Mcp.OneC/Fotoprint.Mcp.OneC.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8082
ENTRYPOINT ["dotnet", "Fotoprint.Mcp.OneC.dll"]

