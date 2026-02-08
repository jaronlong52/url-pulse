FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["UrlPulse.csproj", "./"]
RUN dotnet restore "UrlPulse.csproj"
COPY . .
RUN dotnet publish "UrlPulse.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Run as non-root numeric UID (recommended for chiseled images)
USER 10001

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "UrlPulse.dll"]
