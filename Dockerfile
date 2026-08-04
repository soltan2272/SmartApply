# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY JobApplicationBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
RUN mkdir -p /app/keys /home/app/keys
ENV ASPNETCORE_URLS=http://+:8080
ENV DataProtection__KeysPath=/app/keys
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JobApplicationBot.dll"]
