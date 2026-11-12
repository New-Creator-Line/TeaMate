FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY src/Fotoprint.Core/Fotoprint.Core.csproj src/Fotoprint.Core/
COPY src/Fotoprint.Mcp.KnowledgeBase/Fotoprint.Mcp.KnowledgeBase.csproj src/Fotoprint.Mcp.KnowledgeBase/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet restore src/Fotoprint.Mcp.KnowledgeBase/Fotoprint.Mcp.KnowledgeBase.csproj

COPY src/Fotoprint.Core/ src/Fotoprint.Core/
COPY src/Fotoprint.Mcp.KnowledgeBase/ src/Fotoprint.Mcp.KnowledgeBase/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet publish src/Fotoprint.Mcp.KnowledgeBase/Fotoprint.Mcp.KnowledgeBase.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8086
ENTRYPOINT ["dotnet", "Fotoprint.Mcp.KnowledgeBase.dll"]

