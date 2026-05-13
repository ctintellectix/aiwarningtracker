FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AiCapexSlowdownMonitor.slnx ./
COPY src ./src
COPY tests ./tests
RUN dotnet restore AiCapexSlowdownMonitor.slnx
RUN dotnet publish src/AiCapex.Api/AiCapex.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AiCapex.Api.dll"]
