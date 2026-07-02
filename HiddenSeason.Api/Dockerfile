FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HiddenSeason.slnx global.json ./
COPY apps/HiddenSeason.Api/HiddenSeason.Api.csproj apps/HiddenSeason.Api/
RUN dotnet restore apps/HiddenSeason.Api/HiddenSeason.Api.csproj
COPY apps/HiddenSeason.Api/ apps/HiddenSeason.Api/
RUN dotnet publish apps/HiddenSeason.Api/HiddenSeason.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "HiddenSeason.Api.dll"]
