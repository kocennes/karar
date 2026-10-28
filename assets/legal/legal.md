# Hukuki Uyum — Karar

## Türk Hukuku Kapsamı

### 5651 Sayılı Kanun — Yer Sağlayıcı

Karar, 5651 kapsamında **yer sağlayıcı** statüsündedir.

**Yükümlülükler:**
- Bildirim adresi public web'de gösterilmeli (✅ yapıldı)
- İçerik kaldırma kanalı (✅ destek@karar.app)
- BTK bildirimi için: btk@karar.app
- Mahkeme kararıyla kaldırma: 24 saat içinde
- compliance_logs: IP + device hash, 2 yıl saklama

**İletişim bilgileri (public sayfada görünmeli):**
```
Yer sağlayıcı: Karar Teknoloji
İletişim: destek@karar.app
BTK bildirimleri: bildirim@karar.app
İçerik kaldırma: icerik@karar.app veya in-app şikayet
Telif Hakkı / FSEK: telif@karar.app
```

### KVKK — Kişisel Veri

**Veri Sorumlusu:** Karar Teknoloji

**İşlenen veriler:**
- Cihaz fingerprint (meşru menfaat — güvenlik)
- Kullanıcı adı (sözleşme)
- E-posta (rıza/sözleşme)
- İçerik (sözleşme)
- FCM token (rıza)
- Analitik (rıza — opt-out mevcut)

**Kullanıcı hakları (KVKK Madde 11):**
- Veri işlenip işlenmediğini öğrenme
- Erişim, düzeltme, silme talepleri
- Yanıt süresi: 30 gün

**Başvuru:** kvkk@karar.app

**VERBIS:** MVP aşamasında muafiyet şartları sağlanıyor.

### TCK — İçerik Suçları

**Riskli maddeler:**
- TCK 299: Cumhurbaşkanına hakaret → şikayet üzerine değerlendirme
- TCK 122: Nefret ve ayrımcılık → anında kaldırma
- TCK 314: Terör örgütü propagandası → anında kaldırma, BTK bildirimi
- Doxxing → KVKK + kişisel haklar

---

## Kullanım Koşulları — Özet

(Tam metin hukukçu tarafından hazırlanacak)

**Kapsam:**
- Hizmet tanımı: Anonim sosyal yargı platformu
- Kullanıcı sorumluluğu: İçerik sahibi içerikten sorumludur
- 18+ gereksinimi: Platform 18 yaş altına kapalı
- İçerik lisansı: Kullanıcı platforma world-wide, royalty-free lisans verir
- Askıya alma: İhlal durumunda hesap kapatma hakkı
- Yetkili mahkeme: İstanbul Türk mahkemeleri

---

## Gizlilik Politikası

`docs/privacy.md` — ayrı belge.

Önemli noktalar:
- Reklam veri (AdMob UMP rızası)
- Üçüncü taraf paylaşımı (GCP, Firebase, Perspective)

---

## App Store / Play Store Uyumu

**Apple:**
- Yaş derecelendirmesi: 17+ (UGC platformu)
- Privacy Nutrition Label: `docs/app-privacy-nutrition-label.md`
- In-app purchase: Yok (AdMob banner)

**Google Play:**
- IARC Content Rating: gerekli
- Target audience: 18+
- Data Safety: `docs/app-privacy-nutrition-label.md`
- UGC policy uyumu zorunlu

---

## Telif ve FSEK

**Şikayet kanalı:** telif@karar.app
**Yanıt süresi:** 72 saat
**Kaldırma prosedürü:** in-app rapor veya e-posta → admin incelemesi

Kullanıcı başkasının telif hakkına tabi içeriğini paylaşırsa:
1. FSEK kapsamında kaldırma bildirimi
2. 72 saat içinde değerlendirme
3. İçerik kaldırılırsa kullanıcıya bildirim

---

## Hukuki Risk Matrisi

| Risk | Olasılık | Etki | Mitigasyon |
|------|---------|------|-----------|
| TCK 299 şikayeti | Orta | Yüksek | Moderasyon, hızlı kaldırma |
| Doxxing davası | Düşük | Çok yüksek | Soft-identifying tespiti |
| KVKK cezası | Düşük | Orta | Veri minimizasyonu, VERBIS |
| 5651 erişim engeli | Düşük | Çok yüksek | Hızlı kaldırma, BTK kanalı |
| App Store reddi | Orta | Yüksek | UGC policy uyumu, Age rating |
| Telif ihlali | Düşük | Orta | FSEK kanalı, 72h yanıt |

---

## Hukuki İletişim Noktaları

| Kanal | Adres | Amaç |
|-------|-------|-------|
| Genel | destek@karar.app | Kullanıcı soruları |
| KVKK | kvkk@karar.app | Veri hakları |
| BTK | btk@karar.app | 5651 bildirimleri |
| Telif | telif@karar.app | FSEK şikayetleri |
| Hukuki | hukuk@karar.app | Yasal talepler |
