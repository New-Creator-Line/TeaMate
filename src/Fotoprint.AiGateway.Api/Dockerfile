FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY src/Fotoprint.Core/Fotoprint.Core.csproj src/Fotoprint.Core/
COPY src/Fotoprint.AiGateway.Api/Fotoprint.AiGateway.Api.csproj src/Fotoprint.AiGateway.Api/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet restore src/Fotoprint.AiGateway.Api/Fotoprint.AiGateway.Api.csproj

COPY src/Fotoprint.Core/ src/Fotoprint.Core/
COPY src/Fotoprint.AiGateway.Api/ src/Fotoprint.AiGateway.Api/
RUN --mount=type=cache,id=fotoprint-nuget,target=/root/.nuget/packages dotnet publish src/Fotoprint.AiGateway.Api/Fotoprint.AiGateway.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fotoprint.AiGateway.Api.dll"]
