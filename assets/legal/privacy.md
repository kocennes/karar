# Gizlilik ve Veri Yönetimi — Karar

## Toplanan Veriler

### Otomatik Toplanan Veriler

| Veri | Amaç | Hukuki Dayanak | Saklama |
|------|------|---------------|---------|
| Cihaz fingerprint | Anonim kimlik, ban yönetimi | Meşru menfaat | Süresiz |
| IP adresi | Güvenlik, rate limiting | Meşru menfaat | 90 gün (loglar) |
| Platform (iOS/Android) | Teknik uyumluluk | Meşru menfaat | Hesap silinene kadar |
| App versiyon | Bug takibi | Meşru menfaat | 30 gün |

### Kullanıcı Tarafından Sağlanan Veriler

| Veri | Amaç | Zorunluluk | Saklama |
|------|------|-----------|---------|
| Kullanıcı adı | Pseudonymous kimlik | Opsiyonel (kayıt gerekiyorsa) | Hesap silme + 30 gün |
| E-posta | Bildirim, kimlik doğrulama | Tamamen opsiyonel | Hesap silme + 30 gün |
| Şifre (hash) | Kimlik doğrulama | Kayıt gerekliyse zorunlu | Hesap silme + 30 gün |
| Post içeriği | Platform işlevi | Kullanıcı tercihi | 90 gün (soft delete) |
| Yorumlar | Platform işlevi | Kullanıcı tercihi | 90 gün (soft delete) |
| FCM token | Push bildirimi | Opsiyonel | Cihaz süresi boyunca |

### Üçüncü Taraf Veri İşleme

| Servis | Hangi veri | Neden |
|--------|-----------|-------|
| Google Cloud (GCP) | Tüm uygulama verisi | Hosting sağlayıcı |
| Firebase Analytics | Anonim kullanım metrikleri | Analitik |
| Perspective API | Post/yorum metni | Moderasyon |
| Cloud Vision SafeSearch | Yüklenen görseller | Görsel moderasyon |
| FCM | FCM token, bildirim içeriği | Push bildirim |
| Google AdMob | Cihaz reklam ID'si | Reklam gösterimi |

**Önemli:** Perspective API'ye gönderilen metin Google'ın gizlilik politikasına tabidir. Kişisel bilgi içeren metin (isim, adres) gönderilmemeli — moderasyon bu nedenle anlam kaydeder, raw content göndermez.

---

## Veri Minimizasyonu

Karar, yalnızca platform işlevi için zorunlu verileri toplar:

- Kullanıcı adı → gerçek isim değil, pseudonym
- E-posta → tamamen opsiyonel
- Konum verisi → toplanmıyor
- Cihaz sensör verisi → toplanmıyor
- Kişiler / takvim / kamera → toplanmıyor (yalnızca görsel seçici izni)

---

## Kullanıcı Hakları (KVKK Madde 11)

```
Haklarınız:
1. Kişisel verilerinizin işlenip işlenmediğini öğrenme
2. İşlenmişse buna ilişkin bilgi talep etme
3. Yanlış verilerin düzeltilmesini isteme
4. Verilerinizin silinmesini talep etme
5. Veri aktarımlarından haberdar olma
6. Otomatik karar alma süreçlerine itiraz
```

### Silme Talebi Akışı

```
Kullanıcı: DELETE /api/v1/users/me
    │
    ▼
Backend:
    1. users.deleted_at = NOW()
    2. refresh token'ları iptal et
    3. FCM token'ı sil
    4. Pasif device token
    │
    ▼ (Async, 30 gün sonra — DataRetentionService)
    5. Kullanıcı adını "silinen_kullanici" ile değiştir
    6. E-postayı null yap
    7. Şifre hash'ini sil
    8. İçerikler (post, yorum) anonim kalır — silinmez
       (İçerikler kullanıcıdan bağımsız değer taşır)
```

İçeriklerin de silinmesi isteniyorsa ayrıca talep edilebilir.

### Veri Erişim Talebi (Data Export)

```
GET /api/v1/users/me/data-export
→ JSON formatında veriler
→ SHA-256 + HMAC-SHA256 bütünlük zinciri
→ Content-Disposition: attachment; filename=karar-veri-disa-aktarma-{tarih}.json
```

Envelope içeriği:
```json
{
  "schemaVersion": "1.0",
  "exportData": {
    "user": { ... },
    "posts": [ ... ],
    "comments": [ ... ],
    "exportedAt": "2024-..."
  },
  "integrity": {
    "sha256": "...",
    "hmacSha256": "...",
    "algorithm": "HMAC-SHA256 with server signing key"
  }
}
```

---

## Veri Saklama Politikası

```
Soft-deleted postlar: 90 gün sonra fiziksel silme
Soft-deleted yorumlar: 90 gün sonra fiziksel silme
Silinmiş kullanıcılar: 30 gün sonra anonimleştirme
Rapor kayıtları: 1 yıl sonra anonimleştirme
IP logları: 90 gün
compliance_logs: 2 yıl (5651)
```

---

## Reklam ve Veri

**AdMob kişiselleştirilmiş reklam:**
- Türkiye için UMP (User Messaging Platform) SDK aktif
- Rıza verilmezse non-personalized ads gösterilir
- Rıza yönetimi Ayarlar → Gizlilik → Reklam Tercihlerinden değiştirilebilir

**Reklam veri sınırı:**
- Google AdMob Advertising ID kullanır
- iOS: ATT (App Tracking Transparency) izni gerekli
- Android: Advertising ID sıfırlanabilir

---

## Çocuk Gizliliği

Karar 18+ uygulamasıdır. Bir kullanıcının 18 yaş altı olduğu tespit edilirse hesap derhal kapatılır.

---

## Analitik Opt-Out

Kullanıcı Firebase Analytics toplamayı devre dışı bırakabilir:
```
Ayarlar → Gizlilik → Analitik veri paylaşımını kapat
```
Bu seçim `analytics_opt_out` kolonu ile saklanır ve uygulanır.

---

## Gizlilik Politikası Güncelleme Süreci

1. Önemli değişiklikler 7 gün önceden in-app banner ile duyurulur
2. Değişiklik yürürlük tarihinden önce kullanıcıya bildirilir
3. Büyük değişikliklerde kullanıcı yeni politikayı kabul etmesi istenebilir
4. Değişiklik geçmişi bu belgede kaydedilir

---

## İletişim

Gizlilik ile ilgili talepler:
- E-posta: kvkk@karar.app
- Yanıt süresi: 30 gün (KVKK madde 13 gereği)
