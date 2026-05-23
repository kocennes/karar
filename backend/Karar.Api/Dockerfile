FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sadece csproj dosyasını kopyalayıp restore yapıyoruz (Build Cache için)
COPY Karar.Api/Karar.Api.csproj Karar.Api/
RUN dotnet restore Karar.Api/Karar.Api.csproj

# Tüm kaynak kodunu kopyalayıp yayınlıyoruz
COPY Karar.Api/ Karar.Api/
RUN dotnet publish Karar.Api/Karar.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Render.com default portu 10000 kullanabilir, ASPNETCORE_URLS ile 8080'e sabitliyoruz
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# Migrations klasörü Program.cs tarafından okunuyor, doğru yere kopyalandığından emin olalım
COPY migrations ./migrations

# Güvenlik için root olmayan kullanıcı
RUN addgroup --gid 1001 appgroup \
    && adduser --uid 1001 --gid 1001 --disabled-password appuser
USER appuser

# Health check için endpoint'i Program.cs içinde tanımladık mı kontrol etmeliyiz
ENTRYPOINT ["dotnet", "Karar.Api.dll"]
