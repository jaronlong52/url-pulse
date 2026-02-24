# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Enables Docker layer caching for faster rebuilds
# Dependencies only change when the project file changes
COPY UrlPulse.csproj .
# Restore dependencies (this layer will be cached unless UrlPulse.csproj changes)
RUN dotnet restore

# Copy the rest of the source code
COPY . .
# Compiles app in release mode and writes the output to /app/publish
RUN dotnet publish UrlPulse.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy the published output from the build stage to the runtime image
COPY --from=build /app/publish .

# Listens on port 8080 but doesn't actually open the port
EXPOSE 8080
# Tell ASP.NET Core to listen on port 8080
# The '+' mean "all network interfaces", which is necessary for the app to be accessible from outside the container
ENV ASPNETCORE_URLS=http://+:8080

# Command that runs when container is started
ENTRYPOINT ["dotnet", "UrlPulse.dll"]
