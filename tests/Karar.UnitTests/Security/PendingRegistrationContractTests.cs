using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regresyon: doğrulanmamış şifreli kayıt (/auth/register) başkasının e-postasını süresiz rezerve edip
// (1) gerçek sahibin Google girişini 500 ile kırmamalı, (2) sahibin doğrulamasından sonra saldırganın
// şifresiyle hesaba girilmesine (pre-account hijack) yol açmamalı, (3) sonsuza dek kalmamalı.
public sealed class PendingRegistrationContractTests
{
    [Fact]
    public void GoogleSignIn_HandlesEmailHeldByAnotherRowInsteadOfFailingOnUniqueConstraint()
    {
        var block = SliceEndpointBlock(
            ReadProgram(),
            "app.MapPost(\"/api/v1/auth/google\", async (",
            "app.MapPost(\"/api/v1/auth/google/link\"");

        // E-postayı tutan herhangi bir satır (auth_provider filtresi olmadan) kilitlenerek okunmalı.
        block.Should().Contain("WHERE email = @email");
        block.Should().Contain("FOR UPDATE");
        block.Should().Contain("AuthAccountPolicy.ResolveGoogleEmailCollision(");
        block.Should().Contain("AuthAccountPolicy.IsFirebaseEmailVerified(");
        block.Should().Contain("EMAIL_ALREADY_REGISTERED");

        // Yarış durumunda bile 23505 → 500 değil 409.
        block.Should().Contain("catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)");
    }

    [Fact]
    public void GoogleSignIn_TakeoverOfPendingRegistrationDiscardsRegistrantCredentials()
    {
        var block = SliceEndpointBlock(
            ReadProgram(),
            "app.MapPost(\"/api/v1/auth/google\", async (",
            "app.MapPost(\"/api/v1/auth/google/link\"");

        block.Should().Contain("GoogleEmailCollision.TakeOverPendingRegistration");
        block.Should().Contain("password_hash = NULL");
        block.Should().Contain("auth_provider = 'google'");
        block.Should().Contain("AND email_verified = FALSE");
        block.Should().Contain("DELETE FROM email_otps WHERE email = @email");
        block.Should().Contain("UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @id AND revoked_at IS NULL");
    }

    [Fact]
    public void VerifyEmail_DiscardsRegistrantPasswordWhenVerifiedFromAnotherDevice()
    {
        var block = SliceEndpointBlock(
            ReadProgram(),
            "app.MapPost(\"/api/v1/auth/verify-email\"",
            "app.MapPost(\"/api/v1/auth/resend-otp\"");

        block.Should().Contain("var verifyingDeviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);");
        block.Should().Contain("RETURNING id, username, karma, auth_provider, device_id");
        block.Should().Contain("AuthAccountPolicy.KeepsRegistrantPassword(registeredDeviceId, verifyingDeviceId)");
        // Şifre atılır ve hesap (bildirimler, misafir veri taşıma) kodu giren cihaza bağlanır.
        block.Should().Contain("UPDATE users SET password_hash = NULL, device_id = @deviceId");

        var device = block.IndexOf("var verifyingDeviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);", StringComparison.Ordinal);
        var otp = block.IndexOf("FROM email_otps", StringComparison.Ordinal);
        otp.Should().BeGreaterThan(device, "cihazsız doğrulama isteği OTP denemesi tüketmeden reddedilmeli");

        var keep = block.IndexOf("AuthAccountPolicy.KeepsRegistrantPassword(", StringComparison.Ordinal);
        var tokens = block.IndexOf("jwtService.GenerateAccessToken(", StringComparison.Ordinal);
        tokens.Should().BeGreaterThan(keep, "şifre kararı token üretilmeden önce verilmeli");
    }

    [Fact]
    public void DataRetention_PurgesStaleUnverifiedPasswordRegistrations()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "DataRetentionService.cs");

        service.Should().Contain("PurgeStaleUnverifiedRegistrationsAsync(");
        service.Should().Contain("DELETE FROM users");
        service.Should().Contain("email_verified = FALSE");
        service.Should().Contain("auth_provider = 'password'");
        service.Should().Contain("deleted_at IS NULL");
        service.Should().Contain("created_at < NOW() - INTERVAL '24 hours'");
        // Hesaba bağlı veri varsa (NO ACTION FK) silme denenmemeli.
        service.Should().Contain("NOT EXISTS (SELECT 1 FROM posts p WHERE p.user_id = u.id)");
        service.Should().Contain("NOT EXISTS (SELECT 1 FROM account_recovery_tokens r WHERE r.user_id = u.id)");
        service.Should().Contain("NOT EXISTS (SELECT 1 FROM admin_roles ar WHERE ar.user_id = u.id)");
        service.Should().Contain("DELETE FROM email_otps");
    }

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
