FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG GITHUB_TOKEN
WORKDIR /src

# Copy nuget.config and replace placeholders with actual token
COPY nuget.config ./
RUN sed -i "s/USERNAME/${GITHUB_TOKEN%%:*}/g; s/GITHUB_TOKEN/${GITHUB_TOKEN##*:}/g" nuget.config || true

COPY ["Psicomy.Services.Billing.csproj", "./"]
RUN dotnet restore "Psicomy.Services.Billing.csproj"

COPY . .
RUN dotnet publish "Psicomy.Services.Billing.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Psicomy.Services.Billing.dll"]
