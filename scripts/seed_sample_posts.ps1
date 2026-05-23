
# Karar - Örnek İçerik Yükleme Scripti
# Bu script yerel veya canlı API'ye 5 tane gerçekçi "Karar" senaryosu yükler.

$baseUrl = "http://localhost:5088" # Canlıya çıkınca burayı güncellersin kral
$adminToken = "dev-admin-token"    # appsettings.json'daki Admin:Token

$headers = @{
    "Content-Type" = "application/json"
    "X-Admin-Token" = $adminToken
}

$posts = @(
    @{
        title = "Ofis Arkadaşım Sürekli Sesli Müzik Dinliyor"
        content = "Yan masamda oturan arkadaşım kulaklık takmadan müzik dinliyor. Defalarca uyardım ama 'yaratıcılığımı artırıyor' diyor. Sizce yönetime şikayet etmeli miyim yoksa katlanmalı mıyım? Çok kararsızım."
        categoryId = 1
    },
    @{
        title = "Sevgilim Maaşını Benden Gizliyor"
        content = "2 yıldır beraberiz, ciddi düşünüyoruz ama ne kadar kazandığını asla söylemiyor. Bu güven eksikliği değil mi? Sürekli 'yetiyor işte' diyerek geçiştiriyor. Ayrılmalı mıyım yoksa normal bir durum mu bu?"
        categoryId = 2
    },
    @{
        title = "Annem Odama Haber Vermeden Giriyor"
        content = "22 yaşındayım, çalışıyorum. Annem hala kapıyı vurmadan odama giriyor ve bazen eşyalarımı karıştırıyor. Kavga ediyoruz ama 'ben senin annenim' diyor. Evden ayrılmalı mıyım? Başka çarem kalmadı gibi."
        categoryId = 3
    },
    @{
        title = "Borç Verdim, Arkadaşım Tatilden Fotoğraf Atıyor"
        content = "3 ay önce acil ihtiyacı var diye 5 bin TL borç verdim. Hala geri ödemedi, unuttuğunu da sanmıyorum. Ama dün Maldivler'den tatil fotoğrafı paylaştı. Parayı hemen istemeli miyim yoksa ayıp mı olur?"
        categoryId = 4
    },
    @{
        title = "Sokaktaki Kedileri Beslememi İstemiyorlar"
        content = "Apartman önünde her sabah kedileri besliyorum. Komşular imza toplamış, 'koku yapıyor, bahçeye kedi tüyü geliyor' diye şikayet ediyorlar. Hayvanları sevmek suç mu? Sizce kim haklı?"
        categoryId = 5
    }
)

Write-Host "🚀 Örnek içerikler yükleniyor..." -ForegroundColor Cyan

foreach ($post in $posts) {
    try {
        $json = $post | ConvertTo-Json
        $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/posts" -Method Post -Body $json -Headers $headers
        Write-Host "✅ Başarılı: $($post.title)" -ForegroundColor Green
    } catch {
        Write-Host "❌ Hata: $($post.title) - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n✨ Tüm içerikler yüklendi! Oylar ve yorumlar tertemiz (0)." -ForegroundColor Yellow
