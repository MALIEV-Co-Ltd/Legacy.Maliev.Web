# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
RUN apk add --no-cache git \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git /dependencies/Legacy.Maliev.ServiceDefaults \
    && git -C /dependencies/Legacy.Maliev.ServiceDefaults checkout 1065c071ae0aca17fa56d88c1e13ae51bae074a4 \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts.git /dependencies/Legacy.Maliev.CompatibilityContracts \
    && git -C /dependencies/Legacy.Maliev.CompatibilityContracts checkout 95c62eb6209411f5aada443b315447a2f76ca0cd
COPY nuget.config Directory.Build.props ./
COPY Legacy.Maliev.Web.Application/Legacy.Maliev.Web.Application.csproj Legacy.Maliev.Web.Application/
COPY Legacy.Maliev.Web.Infrastructure/Legacy.Maliev.Web.Infrastructure.csproj Legacy.Maliev.Web.Infrastructure/
COPY Legacy.Maliev.Web/Legacy.Maliev.Web.csproj Legacy.Maliev.Web/
RUN dotnet restore Legacy.Maliev.Web/Legacy.Maliev.Web.csproj \
    -p:MalievWorkspaceRoot=/dependencies \
    -p:UseLocalMalievDependencies=true
COPY . .
RUN dotnet publish Legacy.Maliev.Web/Legacy.Maliev.Web.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/dependencies \
    -p:UseLocalMalievDependencies=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
RUN apk add --no-cache icu-data-full icu-libs
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENTRYPOINT ["dotnet", "Legacy.Maliev.Web.dll"]
