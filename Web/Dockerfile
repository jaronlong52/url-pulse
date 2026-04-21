# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project file first (better layer caching)
COPY ["Web/UrlPulse.csproj", "Web/"]
COPY ["UrlPulse.Core/UrlPulse.Core.csproj", "UrlPulse.Core/"]
COPY ["UrlPulse.Infrastructure/UrlPulse.Infrastructure.csproj", "UrlPulse.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "Web/UrlPulse.csproj"

# Copy the rest of the source code
COPY Web/ Web/
COPY UrlPulse.Core/ UrlPulse.Core/
COPY UrlPulse.Infrastructure/ UrlPulse.Infrastructure/

# Publish the application
WORKDIR "/src/Web"
RUN dotnet publish "UrlPulse.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "UrlPulse.dll"]