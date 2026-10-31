# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Upcheck.sln ./
COPY src/Upcheck.Api/Upcheck.Api.csproj src/Upcheck.Api/
COPY tests/Upcheck.Api.Tests/Upcheck.Api.Tests.csproj tests/Upcheck.Api.Tests/
RUN dotnet restore

COPY . .
RUN dotnet test --no-restore --configuration Release
RUN dotnet publish src/Upcheck.Api -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/upcheck.db"
# The runtime image's non-root "app" user must own the data directory or SQLite cannot create its file.
RUN mkdir -p /data && chown app:app /data
VOLUME /data
EXPOSE 8080

COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "Upcheck.Api.dll"]
