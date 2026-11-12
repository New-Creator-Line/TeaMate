FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Копируем csproj и восстанавливаем зависимости
COPY src/Fotoprint.Core/Fotoprint.Core.csproj src/Fotoprint.Core/
COPY src/Fotoprint.Mcp.EmployeeReader/Fotoprint.Mcp.EmployeeReader.csproj src/Fotoprint.Mcp.EmployeeReader/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet restore src/Fotoprint.Mcp.EmployeeReader/Fotoprint.Mcp.EmployeeReader.csproj

# Копируем исходники и собираем релиз
COPY src/Fotoprint.Core/ src/Fotoprint.Core/
COPY src/Fotoprint.Mcp.EmployeeReader/ src/Fotoprint.Mcp.EmployeeReader/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet publish src/Fotoprint.Mcp.EmployeeReader/Fotoprint.Mcp.EmployeeReader.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8081
ENTRYPOINT ["dotnet", "Fotoprint.Mcp.EmployeeReader.dll"]

