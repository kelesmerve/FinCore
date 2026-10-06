# FinCore

## Proje Hakkında

FinCore, güvenli finansal işlemler ve veri tutarlılığına odaklanan ASP.NET Core tabanlı bir fintech backend projesidir. Katmanlı yapısı, finansal iş kurallarını HTTP ve veritabanı ayrıntılarından ayırır.

## Özellikler

- Kullanıcı kaydı, login ve güvenli parola hashing
- JWT authentication; refresh token üretimi, rotation, logout ve token ailesini iptal etme
- Customer/Admin rol tabanlı yetkilendirme ve açıkça çalıştırılan güvenli Admin bootstrap komutu
- Kullanıcının kendi hesaplarını sorgulaması; Money value object ve Account iş kuralları
- Hesaplar arası transfer ve her transfer için çift kayıtlı (Debit/Credit) ledger
- Veritabanı transaction ve hata durumunda rollback
- `Idempotency-Key` ile tekrarlanan transferin yeniden uygulanmasını önleme
- PostgreSQL `xmin` ile optimistic concurrency
- ProblemDetails tabanlı güvenli hata yanıtları ve health checks
- Unit ve gerçek PostgreSQL integration testleri; Docker Compose ile yerel çalışma

## Kullanılan Teknolojiler

.NET 10, ASP.NET Core Web API, Entity Framework Core, PostgreSQL, Npgsql, xUnit, Docker, Docker Compose ve GitHub Actions.

## Mimari

| Katman | Sorumluluk |
| --- | --- |
| `FinCore.Domain` | Money, kullanıcı, hesap, ledger ve refresh token iş kuralları |
| `FinCore.Application` | Use-case, uygulama sözleşmeleri ve güvenli sonuç modelleri |
| `FinCore.Infrastructure` | EF Core persistence, PostgreSQL ve güvenlik implementasyonları |
| `FinCore.Api` | HTTP endpoint'leri, kimlik doğrulama ve bağımlılık kayıtları |
| `tests/` | Domain ve Application unit testleri ile gerçek PostgreSQL integration testleri |

Bağımlılık yönü `Application → Domain`, `Infrastructure → Application ve Domain`, `API → Application ve Infrastructure` şeklindedir. Domain katmanı EF Core, PostgreSQL ve ASP.NET Core bilmez.

## Temel Finansal Tasarım

`Money` immutable bir value object'tir; tutarlar `decimal` ile tutulur ve iki ondalık basamak kuralı korunur. Transfer, kaynak için bir Debit ve hedef için bir Credit ledger entry üretir. Bakiye, ledger ve idempotency kaydı aynı veritabanı transaction'ında kaydedilir; hata halinde rollback yapılır. Aynı kullanıcı ve işlem için `Idempotency-Key` tekrarlanan transferi yeniden uygulamaz. PostgreSQL `xmin`, eşzamanlı hesap güncellemelerinde optimistic concurrency kontrolü sağlar.

## Güvenlik

Parolalar düz metin olarak saklanmaz. Refresh token'ın yalnızca SHA-256 hash'i veritabanında tutulur. JWT issuer, audience, imza ve süre doğrulaması yapılır; rol tabanlı yetkilendirme uygulanır. Kullanıcı kimliği, doğrulanmış token'ın `sub` claim'inden alınır. Secret değerler kaynak koda yazılmaz; yerelde User Secrets veya environment variable kullanılır. Beklenen hatalar güvenli ProblemDetails yanıtlarına çevrilir. Logout, token'ın durumunu açıklamadan idempotent sonuç verir.

## API Endpointleri

| Endpoint | Erişim | Amaç |
| --- | --- | --- |
| `POST /api/auth/register` | Anonim | Customer kaydı ve hesap oluşturma |
| `POST /api/auth/login` | Anonim | Access ve refresh token alma |
| `POST /api/auth/refresh` | Anonim; refresh token gerekli | Token rotation |
| `POST /api/auth/logout` | Anonim; refresh token gerekli | Token ailesini iptal etme |
| `GET /api/auth/me` | JWT gerekli | Token'daki kimlik bilgilerini okuma |
| `GET /api/accounts/me` | JWT gerekli | Kullanıcının kendi hesaplarını listeleme |
| `GET /api/admin/users` | Admin rolü gerekli | Sayfalı kullanıcı listesi |
| `POST /api/transfers` | JWT ve `Idempotency-Key` header'ı gerekli | Hesaplar arası transfer |
| `GET /health/live` | Anonim | Uygulama canlılığı |
| `GET /health/ready` | Anonim | PostgreSQL erişilebilirliği |

## Yerel Kurulum

Windows PowerShell'de aşağıdaki komutları proje kökünden çalıştırın:

```powershell
git clone https://github.com/kelesmerve/FinCore.git
Set-Location FinCore
Copy-Item .env.example .env
```

`.env` içindeki PostgreSQL parolasını ve JWT anahtarını güvenli yerel değerlerle değiştirin. JWT anahtarı en az 32 UTF-8 byte olmalıdır. `.env` Git tarafından izlenmez. API host üzerinde `localhost:8080`, PostgreSQL `localhost:5433` portunu kullanır.

```powershell
dotnet tool restore
docker compose up -d postgres
```

Migration aracı host üzerindeki PostgreSQL'e bağlanır. API projesinin User Secrets alanına bağlantı bilgisini ve JWT anahtarını kendi `.env` değerlerinizle kaydedin; aşağıdaki değerler yalnızca yer tutucudur:

```powershell
dotnet user-secrets set 'ConnectionStrings:FinCoreDatabase' 'Host=localhost;Port=5433;Database=fincore;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>' --project src/FinCore.Api
dotnet user-secrets set 'Jwt:SecretKey' '<JWT_SECRET_AT_LEAST_32_UTF8_BYTES>' --project src/FinCore.Api
dotnet tool run dotnet-ef database update --project src/FinCore.Infrastructure --startup-project src/FinCore.Api
docker compose build api
docker compose up -d api
docker compose ps
Invoke-WebRequest http://localhost:8080/health/live
Invoke-WebRequest http://localhost:8080/health/ready
```

Migration'lar API başlangıcında otomatik uygulanmaz; yukarıdaki açık komutla uygulanmalıdır. Compose içindeki API, PostgreSQL'e servis adı üzerinden iç port `5432` ile bağlanır.

## Admin Bootstrap

Public Admin kayıt endpoint'i yoktur. `BootstrapAdmin:Email` ve `BootstrapAdmin:Password` değerlerini API User Secrets üzerinden veya `BootstrapAdmin__Email` ve `BootstrapAdmin__Password` environment variable'larıyla sağlayın. Bootstrap yalnızca şu komut açıkça çalıştırıldığında devreye girer:

```powershell
dotnet run --project src/FinCore.Api -- --bootstrap-admin
```

## Build ve Test

```powershell
dotnet restore FinCore.sln
dotnet build FinCore.sln
dotnet test FinCore.sln
```

Domain testleri iş kurallarını, Application testleri use-case akışlarını, integration testleri ise gerçek PostgreSQL persistence ve HTTP davranışlarını doğrular. Integration testleri için PostgreSQL ve `ConnectionStrings:FinCoreDatabase` yapılandırması gerekir.

## Docker ve Health Checks

Docker Compose, PostgreSQL 17 ve API container'larını başlatır; API, PostgreSQL healthy olduktan sonra çalışır. `/health/live` uygulamanın canlılığını, `/health/ready` PostgreSQL bağlantısını denetler. Her iki endpoint de kimlik doğrulama istemez ve hata ayrıntısı döndürmez.

## CI

GitHub Actions, main branch push ve pull request'lerinde .NET tool ve NuGet restore, Release build, PostgreSQL service üzerinde migration ve tüm testleri çalıştırır.
