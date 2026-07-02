FROM node:24-alpine AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY HiddenSeason.Api/HiddenSeason.Api.csproj HiddenSeason.Api/
RUN dotnet restore HiddenSeason.Api/HiddenSeason.Api.csproj
COPY HiddenSeason.Api/ HiddenSeason.Api/
RUN dotnet publish HiddenSeason.Api/HiddenSeason.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api-build /app ./
COPY --from=web-build /src/web/dist ./wwwroot
RUN mkdir -p /data/progress && chown -R $APP_UID:$APP_UID /data
ENV ASPNETCORE_URLS=http://+:8080
ENV Game__ProgressPath=/data/progress
ENV Proxy__TrustForwardedHeaders=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "HiddenSeason.Api.dll"]
