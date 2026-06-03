using FluentAssertions;

namespace Karar.UnitTests.Privacy;

public sealed class AccountDeletionContractTests
{
    [Fact]
    public void DeleteAccountEndpoint_RevokesSessionsAndPushTokensDuringRecoveryWindow()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = Slice(
            program,
            "app.MapDelete(\"/api/v1/users/me\"",
            "app.MapGet(\"/api/v1/users/{username}/posts\"");

        endpointBlock.Should().Contain("UPDATE users");
        endpointBlock.Should().Contain("deleted_at = NOW()");
        endpointBlock.Should().Contain("UPDATE refresh_tokens");
        endpointBlock.Should().Contain("SET revoked_at = NOW()");
        endpointBlock.Should().Contain("DELETE FROM fcm_tokens WHERE device_id = @deviceId");
        endpointBlock.Should().Contain("UPDATE devices");
        endpointBlock.Should().Contain("deleted:");
        endpointBlock.Should().Contain("INSERT INTO account_recovery_tokens");
        endpointBlock.Should().Contain("SendAccountRecoveryAsync");
    }

    [Fact]
    public void DataRetentionService_AnonymizesPersonalFieldsAfterRecoveryWindow()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "DataRetentionService.cs");

        service.Should().Contain("deleted_at < NOW() - INTERVAL '30 days'");
        service.Should().Contain("username = 'silinen_'");
        service.Should().Contain("email = 'deleted-'");
        service.Should().Contain("password_hash = NULL");
        service.Should().Contain("google_id = NULL");
        service.Should().Contain("fingerprint = 'deleted-'");
    }

    [Fact]
    public void FlutterDeleteAccount_ClearsLocalSessionAndBroadcastsLogout()
    {
        var authService = TestRepoPaths.ReadText("lib", "core", "auth", "auth_service.dart");
        var methodBlock = Slice(
            authService,
            "Future<void> deleteAccount({String? password}) async",
            "Future<String?> refreshAccessToken() async");

        methodBlock.Should().Contain("ApiEndpoints.userMe");
        methodBlock.Should().Contain("_googleSignIn.signOut()");
        methodBlock.Should().Contain("FirebaseAuth.instance.signOut()");
        methodBlock.Should().Contain("_storage.clearAuthTokens()");
        methodBlock.Should().Contain("_currentUser = null");
        methodBlock.Should().Contain("TabAuthSync.notifyLoggedOut()");
        methodBlock.Should().Contain("_onLogout?.call(LogoutReason.manual)");
    }

    [Fact]
    public void SettingsDeleteFlow_RemovesCurrentFcmTokenBeforeDeletingAccount()
    {
        var settingsScreen = TestRepoPaths.ReadText("lib", "features", "settings", "settings_screen.dart");
        var deleteDialogBlock = Slice(
            settingsScreen,
            "void _confirmDeleteAccount",
            "class _SettingsTile");

        deleteDialogBlock.Should().Contain("notificationServiceProvider");
        deleteDialogBlock.Should().Contain("deleteCurrentToken()");
        deleteDialogBlock.Should().Contain("deleteAccount(");
        deleteDialogBlock.Should().Contain("context.go('/auth/login')");
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"missing start marker {start}");

        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"missing end marker {end}");

        return text[startIndex..endIndex];
    }
}
