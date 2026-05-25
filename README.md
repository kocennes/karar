# Karar

Karar, Turkiye odakli anonim sosyal yargilama uygulamasidir. Kullanici bir olay veya ikilem paylasir; topluluk bu paylasimi "Hakli" veya "Haksiz" olarak oylar, yorumlar ve tartisir. Uygulama sadece basit bir sosyal feed degil; anonimlik, UGC moderasyonu, oy manipulasyonu korumasi, bildirim, viral paylasim ve admin operasyonlari dusunulerek gelistirilen bir MVP/erken production projesidir.

Bu README, code review toplantisi icin projenin teknik kapsamini, deploy yapisini, tamamlanan isleri ve planlanan sonraki adimlari ozetler.

## Canli Ortamlar

| Parca | Teknoloji | Canli/Repo |
|---|---|---|
| Mobil + Web uygulama | Flutter 3.x, Riverpod, GoRouter, Dio | Firebase Hosting: `https://judge-app-karar.web.app` |
| Backend API | ASP.NET Core 8 Minimal API | Render: `https://karar-oq5t.onrender.com` |
| Admin panel | Next.js 14, TypeScript, Tailwind, Radix UI | Vercel: `https://karar-admin.vercel.app` |
| Ana repo | Flutter + backend + infra | `https://github.com/kocennes/karar` |
| Admin repo | Private Next.js admin panel | `https://github.com/kocennes/karar_admin` |

Admin panel kodu bu repoda local olarak `admin-panel/` altinda gelistiriliyor, ancak ana repoya commit/push edilmiyor. Ayrica `karar_admin` private reposuna senkronize edilip Vercel uzerinden deploy ediliyor.

## Urun Ozeti

Karar'in temel akisi:

1. Kullanici anonim veya hesapli sekilde post olusturur.
2. Topluluk postu "Hakli" / "Haksiz" seklinde oylar.
3. Yorumlar, yanitlar, nested thread yapisi ve yorum begenileriyle tartisma ilerler.
4. Feed, trend, kategori ve kesfet ekranlariyla icerik dagitilir.
5. Paylasim linkleri, Open Graph onizlemeleri ve deep link mantigi viral buyumeyi destekler.
6. Moderasyon, raporlama, otomatik esik kontrolleri ve admin panel ile platform guvenligi yonetilir.

## Teknoloji Stack

### Frontend

- Flutter 3.x
- Riverpod state management
- GoRouter routing
- Dio HTTP client
- Firebase Messaging, Analytics, Crashlytics, Performance, Remote Config
- Google Mobile Ads / AdMob
- Google Sign-In + Firebase Auth entegrasyonu
- Web build: `flutter build web`

### Backend

- ASP.NET Core 8 Minimal API
- PostgreSQL 15
- Redis 7
- JWT auth
- Argon2 password hashing
- Firebase Admin SDK
- MailKit SMTP
- ImageSharp ile dinamik OG/story gorsel uretimi
- Serilog structured logging
- Health checks

### Admin Panel

- Next.js 14
- TypeScript
- Tailwind CSS
- Radix UI primitives
- SWR data fetching
- Recharts analytics grafikleri
- Cookie tabanli admin session

### Data ve Infra

- PostgreSQL: Neon.tech
- Redis: Upstash
- Storage: Supabase bucket
- Backend hosting: Render
- Web hosting: Firebase Hosting
- Admin hosting: Vercel
- CI: GitHub Actions

## Mimari

```text
Flutter Mobile/Web
        |
        | HTTPS / JSON
        v
ASP.NET Core 8 API  ---- PostgreSQL / Neon
        |                  |
        |                  +-- Ana veri: users, posts, votes, comments, reports,
        |                      notifications, admin actions, automod rules
        |
        +---- Redis / Upstash
        |       Feed cache, rate limit, dirty post flags,
        |       notification batching, OTP, background job state
        |
        +---- Supabase Storage
        |       Post gorselleri ve media storage
        |
        +---- Firebase / FCM
        |       Push notification, analytics, crash/performance
        |
        +---- SMTP
                Email OTP, admin login code, account flows

Next.js Admin Panel
        |
        | HTTPS / Admin API
        v
Same ASP.NET Core API
```

Backend su an monolitik ilerliyor. Bu MVP icin dogru tercih: daha az operasyon yuku, daha hizli iterasyon, tek deployment. Notification, moderation, ranking ve analytics gibi alanlar servis sinirlari belli olacak sekilde yazildi; trafik buyudugunde parcalanabilir.

## Backend Kapsami

Backend `backend/Karar.Api` altinda. Ana giris noktasi `Program.cs`.

Tamamlanan ana endpoint gruplari:

- Device register ve FCM token kaydi
- Auth: register, login, refresh, logout
- Email OTP: e-posta dogrulama, sifre sifirlama, admin login kodu
- Google auth ve Google hesap baglama
- 2FA setup/enable/disable ve backup codes
- User profile, username availability, password/email change
- Session listesi ve session iptali
- Post create/list/detail/update/delete
- Vote/unvote
- Comment create/update/delete
- Nested comment, pin comment, comment reactions
- Feed, discover, today, weekly featured, city trending
- Search ve user search
- Save/unsave post
- Category follow/mute
- Report ve feedback akislari
- Notifications listesi ve read-all
- Data export, account delete/recovery
- Moderation transparency
- Open Graph shell, sitemap, robots, app links

Admin API tarafinda:

- Admin auth
- Moderation queue
- Report list/action
- Post/user/comment/device management
- Device ban/unban ve subnet ban
- User warning, strike, ban, unban, delete
- Bulk moderation ve bulk user action
- Audit/admin actions
- Analytics overview, cache, trends, categories, moderation, moderators, retention, velocity
- Appeal queue ve appeal decision
- AutoMod rule engine
- Category throttling

## Database

Ana veri tabani PostgreSQL. Migration dosyalari `backend/migrations/` altinda tutuluyor.

Kapsanan veri alanlari:

- Users, auth identity, sessions
- Posts, post slug, anonymous ownership, featured state
- Votes ve vote uniqueness
- Comments, nested comments, pinned comments, reactions
- Categories, category follow/mute, category throttling
- Notifications ve push device tokens
- Reports, moderation actions, appeals
- Admin audit logs
- Device trust, banned devices, banned subnets
- AutoMod rules
- Karma/history/analytics destek tablolari

Uygulama UGC oldugu icin DB tarafinda ozellikle su konulara dikkat edildi:

- Soft delete ve status bazli listeleme
- Vote manipulation onlemleri
- Device bazli tekil oy mantigi
- Moderation ve audit log izlenebilirligi
- KVKK/data export/delete akislari
- Post slug ve public share URL destegi

## Redis Kullanimi

Redis, sadece cache degil operasyonel kontrol katmani olarak kullaniliyor.

Kullanim alanlari:

- Feed cache
- Dirty post invalidation
- Distributed rate limit
- Notification rate limiting
- Comment notification batching
- Verdict reminder job state
- Email/admin OTP temporary state
- Cache hit-rate analytics
- Background job dedupe flags

Baslangic icin Upstash yeterli. Trafik arttiginda notification queue ve event stream icin Redis Streams, RabbitMQ veya Kafka degerlendirilecek.

## Storage ve Media

Gorsel yukleme akisi backend uzerinden ilerliyor. Current production configuration Supabase bucket kullaniyor.

Media tarafinda planlanan/uygulanan kontroller:

- Image validation
- SafeSearch/moderation entegrasyonu
- Post image storage
- Dynamic Open Graph image generation
- Instagram Story formatinda 1080x1920 gorsel endpoint
- CDN/cache stratejisi icin hazirlik

## Flutter Uygulamasi

Flutter kodu `lib/` altinda feature-first yapida.

Ana moduller:

- `features/feed`: feed, discover, categories, trend topics
- `features/post_detail`: post detay, voting, comments, AI summary, similar posts, share
- `features/create_post`: post olusturma, image picker, web drop zone
- `features/profile`: profil, postlarim, yorumlarim, kaydedilenler, karma
- `features/auth`: login, register, OTP, 2FA, sessions, email/password change
- `features/settings`: ayarlar, bildirim tercihleri, blocked users, feedback
- `features/notifications`: in-app notification center
- `features/search`: post/user search
- `features/legal`: legal, privacy, moderation transparency, copyright/contact
- `features/report`: report bottom sheet

Frontend tarafinda tamamlanan onemli akisar:

- Responsive web/mobile layout
- Bottom navigation / navigation rail
- Feed infinite scroll ve skeleton loading
- Post detail ve nested comments
- Post create UX
- Anonymous author label ve post owner logic
- Share sheet, copy link, Web Share fallback
- Report/block flows
- Notification preferences UI
- Account/session/security screens
- Legal/compliance screens
- Large text/accessibility layout iyilestirmeleri

## Admin Panel

Admin panel Next.js ile `admin-panel/` altinda gelistiriliyor. Ana repo icinde local calisiyor ama git kurali geregi ana repoya push edilmiyor. Ayrica private `kocennes/karar_admin` reposuna pushlanip Vercel tarafindan deploy ediliyor.

Admin panelde tamamlananlar:

- Admin login
- Email OTP ile admin giris akisi
- Dashboard
- Dark mode
- Ortak pagination
- Moderation queue
- Queue sekmeleri ve priority filtreleri
- Moderation action undo toast
- Reports management
- Posts management
- Post detail modal
- Comments management
- Users management
- User detail: profile, recent posts, strikes, ban history
- Sanction ladder: warning, strike, temporary/permanent ban
- Devices management
- Suspicious devices
- Category health/throttle controls
- Brigade/coordinated attack alerts
- Admin sessions
- Audit log export
- Analytics: overview, moderation, moderators, trends, categories, retention, cache, velocity
- Appeal queue
- AutoMod rules UI
- Bulk moderation and bulk user actions

Admin deploy:

- Local dev: `cd admin-panel && npm install && npm run dev`
- Local port: `3001`
- Production: Vercel
- Repo: `kocennes/karar_admin`
- API target: Render backend URL via `NEXT_PUBLIC_API_URL`

## Authentication ve Security

Tamamlanan veya altyapisi hazir olan guvenlik isleri:

- JWT access/refresh token yapisi
- Argon2 password hashing
- Email OTP
- Admin email OTP login
- User 2FA + backup codes
- Active session management
- Distributed rate limit middleware
- Brute-force service
- Admin security middleware
- Sensitive data redaction
- Redacted request logging
- SSRF protection handler
- Device trust service
- Device/subnet ban
- Play Integrity hazirligi
- Forbidden-file guard CI

Production icin secret degerleri GitHub'a yazilmiyor. Ortam degiskenleri Render/Vercel/Firebase tarafinda tutuluyor.

## Moderasyon ve Trust & Safety

Karar anonim UGC platformu oldugu icin moderasyon ana mimarinin bir parcasi olarak ele alindi.

Tamamlanan veya altyapisi bulunan alanlar:

- User report flow
- Post/comment report support
- Content moderation service
- Perspective API service
- SafeSearch service
- Image moderation worker
- Report threshold service
- Reporter reputation service
- Auto-hidden/under-review akislari
- Appeal system
- Appeal overturn rate analytics
- AutoMod rule engine
- Keyword/regex based rule support
- Category throttling
- Political narrative cluster detection
- Brigade/coordinated activity alerts
- Moderation transparency endpoint
- Admin audit logs

Ozellikle dikkat edilen riskler:

- Vote manipulation
- Coordinated brigade attacks
- Coordinated reporting abuse
- Doxxing / personal data exposure
- Hate/harassment/self-harm indicators
- Removed content resurfacing
- Admin action auditability

## Notifications

Bildirim sistemi hem in-app hem push olarak planlandi.

Mevcut ve planlanan kapsam:

- In-app notification center
- Unread badge/count
- Read-all endpoint
- Notification preferences UI
- FCM token registration
- Comment notification batching
- Notification rate limiter
- Quiet hours
- Verdict reminder job
- Vote milestone notifications
- Viral/trending content notification strategy
- Web Push VAPID entegrasyonu icin hazirlik
- Deep link payload stratejisi
- Multi-device sync ve read/unread sync plani

Bildirim mimarisi detaylari `docs/notification-system.md`, `docs/mobile-push.md`, `docs/web-push.md` ve ilgili plan dokumanlarinda tutuluyor. Bu dokumanlar internal docs oldugu icin ana repo push kurallarinda korunuyor.

## Viral Growth ve Web

Paylasim ve public web tarafinda yapilanlar:

- `/posts/{id}` ve `/posts/{slug}` public share route
- Bot/crawler user-agent tespiti
- Dynamic OG shell HTML
- 1200x630 PNG social card generation
- 1080x1920 story image endpoint
- JSON-LD Question schema
- Sitemap ve robots endpoints
- Apple App Site Association
- Android Asset Links
- Web Share API fallback
- Copy link fallback

Bu sayede post linkleri sosyal platformlarda daha iyi onizleme alacak sekilde tasarlandi.

## CI/CD ve Git Kurallari

GitHub Actions ana CI workflow'u eklendi.

CI kapsami:

- Flutter test
- .NET unit test
- Forbidden-file guard

Forbidden-file guard su dosyalarin pushlanmasini engellemek icin var:

- `docs/`
- `TASK.md`
- `TODO.md`
- `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`
- `admin-panel/`
- `.env*`
- secret/credential dosyalari

Bu kuralin sebebi: repo public/review paylasiminda guvenli kalmali; internal mimari notlari, secretlar ve admin panel private kalmali.

## Lokal Calistirma

### Flutter

```powershell
flutter pub get
flutter run
```

Web build:

```powershell
flutter build web
```

### Backend

```powershell
dotnet restore backend/Karar.Api/Karar.Api.csproj
dotnet run --project backend/Karar.Api/Karar.Api.csproj
```

Backend local profile varsayilan olarak `http://localhost:5088` uzerinden calisir.

### Admin Panel

```powershell
cd admin-panel
npm install
npm run dev
```

Admin panel localde `http://localhost:3001` uzerinden calisir.

## Testler

Flutter:

```powershell
flutter test --no-pub
```

.NET unit tests:

```powershell
dotnet test tests/Karar.UnitTests/Karar.UnitTests.csproj --verbosity quiet
```

.NET integration tests:

```powershell
dotnet test tests/Karar.IntegrationTests/Karar.IntegrationTests.csproj --verbosity quiet
```

Admin panel:

```powershell
cd admin-panel
npm run type-check
npm run build
```

## Environment Variables

Gercek degerler repoya yazilmaz. Production ortamlarinda genel olarak su konfigurasyonlar gerekir:

Backend:

- `ASPNETCORE_ENVIRONMENT`
- `AUTO_MIGRATE`
- `ConnectionStrings__Postgres`
- `ConnectionStrings__Redis`
- `Redis__Password`
- `Redis__Ssl`
- `Jwt__Secret` veya ileride RS256 key pair
- `Admin__Email`
- `Admin__Password` veya `Admin__PasswordHash`
- `Admin__Token`
- `Email__SmtpUser`
- `Email__SmtpPass`
- `Email__FromAddress`
- `Storage__Provider`
- `Supabase__Url`
- `Supabase__Key`
- `Supabase__Bucket`
- Firebase/Google servis ayarlari

Admin panel:

- `NEXT_PUBLIC_API_URL`
- Gerekirse Vercel environment-specific config

Flutter/Web:

- Firebase config
- API base URL
- Web Push VAPID public key, entegrasyon tamamlandiginda

## Su Ana Kadar Tamamlanan Buyuk Isler

- Flutter mobil/web temel uygulama
- ASP.NET Core backend API
- PostgreSQL migration altyapisi
- Redis cache/rate limit altyapisi
- Auth, Google auth, email OTP, 2FA
- Feed, discover, trend, category flows
- Post create/detail/update/delete
- Voting
- Comments, nested replies, reactions, pinning
- Profile, saved posts, user comments, karma
- Settings, privacy, notification preferences
- Reports and moderation
- Admin panel with moderation, analytics, users, devices, reports
- Appeal system
- AutoMod rule engine
- Dynamic OG and story image generation
- Firebase web deploy
- Render backend deploy
- Vercel admin deploy
- CI workflow and forbidden-file guard
- Notification architecture planning
- DevOps/security roadmap

## Bilinen Eksikler ve Yakin Roadmap

Yakin vadede odaklanilacak basliklar:

- Production branch protection
- GitHub secret scanning, Dependabot, CodeQL
- Backend container build, SBOM ve vulnerability scan
- Render/Cloud Run deploy workflow
- Firebase Hosting preview channels
- Admin panel icin ayri CI/CD pipeline
- Web Push VAPID token akisini uctan uca tamamlama
- Privacy Policy / Terms / KVKK public URL dogrulama
- 5651 ve telif/FSEK public sayfalarinin canli dogrulanmasi
- Play Store internal testing ve App Store TestFlight hazirligi
- Real device push/reklam testleri
- Load test: ilk hedef 100 concurrent user
- Product analytics dashboard
- Redis cache hit-rate dashboard
- Notification analytics ve A/B test altyapisi
- Staging environment
- Backup/restore/PITR runbook
- Incident runbook
- Cloud/CDN media cache stratejisi

Buyume fazinda:

- AppsFlyer/Adjust OneLink deferred deep link attribution
- K-factor/share-to-install tracking
- Daha guclu recommendation/ranking modeli
- Search altyapisi icin Elasticsearch veya alternatif arama motoru degerlendirmesi
- Read replica degerlendirmesi
- Servis ayrisma: notification service, moderation service, analytics pipeline

## Review Icin Onemli Sorular

Senior code review toplantisinda ozellikle su konulari tartismak faydali olur:

- Monolitik backend bu faz icin yeterli mi, hangi noktada servis ayrimi gerekir?
- Redis kullanimlari dogru sinirda mi, queue/event stream ihtiyaci ne zaman dogar?
- Vote manipulation ve brigade detection icin eklenmesi gereken DB/index/analytics katmani var mi?
- Admin action audit modeli hukuki ve operasyonel acidan yeterli mi?
- Notification rate limiting ve quiet hours stratejisi retention/churn dengesi icin yeterli mi?
- Render + Neon + Upstash MVP icin yeterli mi, staging/prod ayrimi nasil olmali?
- CI/CD'de release bloklayacak minimum kalite kapilari neler olmali?
- Public legal/KVKK/5651 akislari yayina cikmadan once nasil dogrulanmali?
- Mobil release icin TestFlight/Play Internal Testing sureci nasil standardize edilmeli?

## Notlar

Bu repo code review icin paylasilabilir ana repodur. Internal docs, credential, TODO/TASK, admin panel local dosyalari ve secret dosyalari commit/push kapsamina alinmamalidir.

Admin panel ayrica private repo uzerinden deploy edilir. README'de bahsedilen secret/env isimleri sadece konfigurasyon sozlesmesini anlatir; gercek degerler repo disindaki platformlarda saklanir.
