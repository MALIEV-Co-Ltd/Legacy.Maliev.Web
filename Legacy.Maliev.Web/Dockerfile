# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
COPY nuget.config Directory.Build.props ./
COPY .dependencies/ .dependencies/
COPY Legacy.Maliev.Web.Application/Legacy.Maliev.Web.Application.csproj Legacy.Maliev.Web.Application/
COPY Legacy.Maliev.Web.Infrastructure/Legacy.Maliev.Web.Infrastructure.csproj Legacy.Maliev.Web.Infrastructure/
COPY Legacy.Maliev.Web/Legacy.Maliev.Web.csproj Legacy.Maliev.Web/
RUN dotnet restore Legacy.Maliev.Web/Legacy.Maliev.Web.csproj -p:MalievWorkspaceRoot=/src/.dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.Web/Legacy.Maliev.Web.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/src/.dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.Web.dll"]
