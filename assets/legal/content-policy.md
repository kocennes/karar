# İçerik Politikası — Karar

## Temel İlkeler

Karar topluluğu için içerik kuralları. Bu politika platform bütünlüğünü ve kullanıcı güvenliğini korumak için uygulanır.

## İzin Verilen İçerik

✅ **Uygun içerik:**
- Gerçek hayat anlaşmazlıkları (iş, aile, arkadaşlık, ilişki)
- "Haklı mıydım?" sorusu sorulabilen durumlar
- Toplumsal etik ikilemler
- Sosyal, komşu, ulaşım gibi gündelik durum çatışmaları
- Anonim paylaşım gerektiren profesyonel veya kişisel durumlar

## Yasaklanan İçerik

### 1. Kişisel Bilgi (Doxxing)
- İsim, adres, telefon, sosyal medya hesabı
- Kendi veya başkasının bilgileri
- Kombinasyonla tanımlanabilir veri (≥3 alan)

**Moderasyon:** Anında kaldırma, compliance log

### 2. Cinsel İçerik
- Karar 18+ platformdur ancak cinsel içerik uygun değildir
- Örtülü cinsel içerik: under_review

### 3. Nefret Söylemi
- Etnik, dini, cinsiyet kimliğine dayalı aşağılama
- Siyasi grup veya inanç hedefli koordineli saldırı

**Moderasyon:** Kaldırma, strike, kalıcı ban (ağır vakada)

### 4. Taciz ve Zorbalık
- Belirli kullanıcıyı hedef alan birden fazla post/yorum
- Koordineli taciz (brigade)

**Moderasyon:** İçerik kaldırma + yaptırım merdiveni

### 5. Kendine Zarar Verme ve İntihar
- İntiharı teşvik eden içerik
- **İstisna:** Yardım arayan içerik yasaklanmaz, 112 yönlendirmeli destek banner'ı gösterilir

**Moderasyon:** Hızlı kaldırma, içerik sahibine 112 yönlendirmeli destek bildirimi

### 6. Yasa Dışı İçerik (5651 ve TCK)
- Terör propagandası
- Uyuşturucu reklamı
- Yasadışı ticaret
- TCK 299 (Cumhurbaşkanına hakaret) — şikayet üzerine değerlendirme

**Moderasyon:** Hızlı kaldırma, 5651 compliance log, BTK bildirimi gerekiyorsa

### 7. Spam ve Sahte İçerik
- Koordineli oylama (brigade)
- Sahte durumlar oluşturma
- Tekrarlayan içerik kopyalama
- Bot hesaplar

**Moderasyon:** İçerik kaldırma, cihaz ban

## Yaptırım Merdiveni

```
Hafif (ilk ihlal, düşük şiddet)
    → Uyarı bildirimi
    → İçerik kaldırma

Orta (tekrar, orta şiddet)
    → 1-7 gün geçici ban
    → Strike kaydı

Ağır (nefret söylemi, doxxing, koordineli saldırı)
    → 30 gün - kalıcı ban
    → Tüm içerik kaldırma

Çok ağır (yasadışı, CSAM, terör)
    → Anında kalıcı ban
    → Yetkili makama bildirim
    → compliance_logs kaydı
```

## İçerik Kaldırma Bildirimi

Kullanıcı, içeriği kaldırıldığında bildirilir. Kaldırma nedeni gösterilir (genel kategori, detay değil):
- "İçerik politikamıza aykırı"
- "Kişisel bilgi içeriyor"
- "Nefret söylemi tespit edildi"

## İtiraz Süreci

```
Kullanıcı → Moderasyon Geçmişim → İtiraz Et
    │ (in-app veya destek@karar.app)
    ▼
Admin inceleme (48 saat hedef)
    ├── İtiraz kabul: içerik aktive edilir
    └── İtiraz red: karar gerekçesiyle bildirilir
```

## Politika Değişikliği Süreci

Büyük değişiklikler için:
1. 7 gün önceden in-app banner
2. Changelog bu belgede güncellenir
3. Mevcut kullanıcılara e-posta bildirimi

## Politika Changelog

| Tarih | Değişiklik |
|-------|-----------|
| MVP | İlk politika yayını |
| - | - |

## Özel Durumlar

### Anonim Platform ve Sorumluluk
Karar anonim bir platform olmasına karşın "yer sağlayıcı" statüsündedir (5651). İçerik sahipliği cihaz fingerprint + IP hash ile izlenir (compliance log), kaldırma talebi durumunda kullanılır.

### Siyasi İçerik
Siyasi görüş paylaşımı yasak değildir. Ancak:
- Koordineli siyasi maniplasyon tespit edilirse kategori throttle
- TCK/5651 kapsamındaki içerik anında kaldırılır
- PoliticalNarrativeClusterJob koordineli siyasi saldırı tespiti yapar

### Mahkeme Kararı ile Kaldırma
Türk mahkemelerinin URAP (Url Removal Application) kararları BTK üzerinden iletilir. 24 saat içinde içerik erişime kapatılır, compliance_logs kaydı oluşturulur.
