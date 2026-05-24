
# Karar - Örnek İçerik Yükleme Scripti
$baseUrl = "https://karar-oq5t.onrender.com"
$adminToken = "kral-token-123"

# 1. Önce bir dummy cihaz kaydedelim
Write-Host "📱 Dummy cihaz kaydediliyor..." -ForegroundColor Cyan
$regBody = @{
    fingerprint = "seed-dummy-device-001"
    platform = "web"
    appVersion = "1.0.0"
} | ConvertTo-Json

try {
    $regResponse = Invoke-RestMethod -Uri "$baseUrl/api/v1/devices/register" -Method Post -Body $regBody -ContentType "application/json"
    $deviceToken = $regResponse.deviceToken
    Write-Host "✅ Cihaz kaydedildi. Token: $deviceToken" -ForegroundColor Green
} catch {
    Write-Host "❌ Cihaz kaydı başarısız: $($_.Exception.Message)" -ForegroundColor Red
    return
}

# 2. Örnek postları yükleyelim
$headers = @{
    "Content-Type" = "application/json"
    "X-Admin-Token" = $adminToken
    "X-Device-Token" = $deviceToken
}

$posts = @(
    @{
        title = "Ofis Arkadasim Surekli Sesli Muzik Dinliyor"
        content = "Yan masamda oturan arkadasim kulaklik takmadan muzik dinliyor. Defalarca uyardim ama yaraticiligimi artiriyor diyor. Sizce yonetime sikayet etmeli miyim yoksa katlanmali miyim?"
        categoryId = 1
    },
    @{
        title = "Sevgilim Maasinı Benden Gizliyor"
        content = "2 yildir beraberiz, ciddi dusunuyoruz ama ne kadar kazandigini asla soylemiyor. Bu guven eksikligi degil mi? Surekli yetiyor iste diyerek gecistiriyor. Ayrilmali miyim?"
        categoryId = 2
    },
    @{
        title = "Annem Odama Haber Vermeden Giriyor"
        content = "22 yasindayim, calisiyorum. Annem hala kapiyi vurmadan odama giriyor ve bazen esyalarimi karistiriyor. Kavga ediyoruz ama ben senin annenim diyor. Evden ayrilmali miyim?"
        categoryId = 3
    },
    @{
        title = "Borc Verdim, Arkadasim Tatilden Fotograf Atiyor"
        content = "3 ay once acil ihtiyaci var diye 5 bin TL borc verdim. Hala geri odemedi. Ama dun tatil fotografi paylasti. Parayi hemen istemeli miyim yoksa ayip mi olur?"
        categoryId = 4
    },
    @{
        title = "Sokaktaki Kedileri Beslememi Istemiyorlar"
        content = "Apartman onunde her sabah kedileri besliyorum. Komsular imza toplamis, koku yapiyor diyorlar. Hayvanlari sevmek suc mu? Sizce kim hakli?"
        categoryId = 5
    }
)

Write-Host "`n🚀 Ornek icerikler yukleniyor..." -ForegroundColor Cyan

foreach ($post in $posts) {
    try {
        $json = $post | ConvertTo-Json
        $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/posts" -Method Post -Body $json -Headers $headers
        Write-Host "✅ Basarili: $($post.title)" -ForegroundColor Green
    } catch {
        Write-Host "❌ Hata: $($post.title) - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n✨ Tum icerikler yuklendi! Oylar ve yorumlar tertemiz (0)." -ForegroundColor Yellow
