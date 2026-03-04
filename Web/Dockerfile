# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project file first (better layer caching)
COPY Web/UrlPulse.csproj Web/
WORKDIR /src/Web
RUN dotnet restore

# Copy the rest of the source
WORKDIR /src
COPY Web/ Web/

# Publish
WORKDIR /src/Web
RUN dotnet publish UrlPulse.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "UrlPulse.dll"]