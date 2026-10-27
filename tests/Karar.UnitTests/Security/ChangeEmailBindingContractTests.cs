using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regresyon: e-posta değişikliği OTP'si yalnızca yeni adrese bağlıydı (chgmail:{newEmail}).
// Talebi şifreyle başlatan hesap A, kodu başka bir hesabın (B) access token'ıyla confirm edip
// B'nin e-postasını kendi adresine taşıyabiliyordu. Bekleyen talep artık userId'ye bağlı.
// (Postgres/Redis olmadan handler çalıştırılamadığı için kaynak sözleşmesi testi.)
public sealed class ChangeEmailBindingContractTests
{
    private const string RequestStart = "app.MapPost(\"/api/v1/auth/change-email/request\"";
    private const string ConfirmStart = "app.MapPost(\"/api/v1/auth/change-email/confirm\"";
    private const string ConfirmEnd = "app.MapPost(\"/api/v1/auth/recover-account\"";

    [Fact]
    public void ChangeEmailRequest_StoresPendingChangeUnderRequestingUser()
    {
        var block = Slice(ReadProgram(), RequestStart, ConfirmStart);

        block.Should().Contain("var dbKey = $\"chgmail:{userId}:{newEmail}\";");
        block.Should().NotContain("$\"chgmail:{newEmail}\"",
            "an OTP stored only under the address can be consumed by any authenticated account");
        block.Should().Contain("$\"otp:chgemail:{userId}:{newEmail}\"");
    }

    [Fact]
    public void ChangeEmailConfirm_OnlyConsumesOtpIssuedToCaller()
    {
        var block = Slice(ReadProgram(), ConfirmStart, ConfirmEnd);

        block.Should().Contain("var otpKey = $\"chgmail:{userId}:{newEmail}\";");
        block.Should().NotContain("$\"chgmail:{newEmail}\"");
        block.Should().Contain("KeyDeleteAsync($\"otp:chgemail:{userId}:{newEmail}\")");
    }

    [Fact]
    public void ChangeEmailConfirm_RechecksProviderAndAddressBeforeUpdating()
    {
        var block = Slice(ReadProgram(), ConfirmStart, ConfirmEnd);
        var update = block.IndexOf("UPDATE users SET email = @email", StringComparison.Ordinal);

        update.Should().BeGreaterThan(0);
        var providerCheck = block.IndexOf("\"GOOGLE_ACCOUNT\"", StringComparison.Ordinal);
        providerCheck.Should().BeGreaterThan(0, "confirm must refuse Google-provider accounts like request does");
        providerCheck.Should().BeLessThan(update);

        var uniquenessCheck = block.IndexOf("SELECT 1 FROM users WHERE email = @email AND id <> @userId", StringComparison.Ordinal);
        uniquenessCheck.Should().BeGreaterThan(0);
        uniquenessCheck.Should().BeLessThan(update);

        block.Should().Contain("PostgresErrorCodes.UniqueViolation",
            "a race on users.email UNIQUE must map to 409 instead of a 500 with exception detail");
        block.Should().Contain("ExecuteNonQueryAsync() != 1");
    }

    [Fact]
    public void ChangeEmailConfirm_NotifiesPreviousAddress()
    {
        var block = Slice(ReadProgram(), ConfirmStart, ConfirmEnd);

        block.Should().Contain("emailService.SendEmailChangedNoticeAsync(oldEmail)");
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "EmailService.cs")
            .Should().Contain("public Task SendEmailChangedNoticeAsync(string oldEmail)");
    }

    private static string ReadProgram() => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
