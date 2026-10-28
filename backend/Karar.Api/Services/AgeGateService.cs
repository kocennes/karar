using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Karar.Api.Contracts;
using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

// P0-02: temporary neutral 18+ age gate, in place until the e-Devlet age token (P0-01) lands.
//
// "Neutral" (FTC NGL settlement): the UI never names the threshold, and a refusal neither
// explains why nor hints that a different date would work. The declared date of an ineligible
// person is never stored. A refusal is remembered as a timestamp only (V95), and that part is
// always on, not behind a flag:
//   devices.age_gate_failed_at  the device stays refused: /auth/register, /users/me/onboarding,
//                               /devices/age-declaration and the Google sign-up of a new account.
//   users.age_gate_failed_at    the account stays refused from any device: /users/me/onboarding,
//                               /auth/login, /auth/google and /auth/refresh.
//
// Flags (both default false, see appsettings.json). With both off the write filter returns
// without touching the database, exactly as before the gate existed.
//   AgeGate:Enforce    write endpoints (.RequireAgeGate) need a signed-in account with a birth
//                      date: a missing or invalid Bearer token is 401 AUTH_REQUIRED (post votes
//                      still accept guests), a NULL date_of_birth is 403 AGE_VERIFICATION_REQUIRED,
//                      and a refused account or device is 403 AGE_NOT_ELIGIBLE, guest votes included.
//                      Turn on only after the app version with the onboarding step
//                      (POST /api/v1/users/me/onboarding) is live.
//   AgeGate:GuestVote  a guest device must make a neutral age declaration
//                      (POST /api/v1/devices/age-declaration) before it can vote.
//                      Waits for the legal opinion (P0-01).
public static class AgeGateCodes
{
    public const string NotEligible = "AGE_NOT_ELIGIBLE";
    public const string VerificationRequired = "AGE_VERIFICATION_REQUIRED";
    public const string DeclarationRequired = "AGE_DECLARATION_REQUIRED";
    public const string InvalidDateOfBirth = "INVALID_DATE_OF_BIRTH";
    public const string SignInRequired = "AUTH_REQUIRED";

    // Neutral copy: no threshold, no reason, no invitation to try another date.
    public const string NotEligibleMessage = "Üzgünüz, Karar'ı kullanamıyorsun.";
    public const string VerificationRequiredMessage = "Devam etmek için doğum tarihini girmen gerekiyor.";
    public const string DeclarationRequiredMessage = "Oy vermeden önce doğum tarihini girmen gerekiyor.";
    public const string InvalidDateOfBirthMessage = "Geçerli bir doğum tarihi gir.";
    public const string SignInRequiredMessage = "Devam etmek için giriş yapman gerekiyor.";
}

public enum AgeGateDecision
{
    Eligible,
    NotEligible,
    InvalidDate
}

public enum AgeGateScope
{
    // Account writes: create/edit post and comment, comment reactions and up/down votes, pin,
    // profile edit, guest-data migration. Under AgeGate:Enforce a signed-in account is required.
    UserWrite,
    // Post vote: signed-in users as UserWrite. Guests stay allowed unless their device is refused,
    // behind the neutral declaration when AgeGate:GuestVote is on.
    Vote
}

public sealed class AgeGateService(IConfiguration configuration, Db db, JwtService jwtService)
{
    public const int MinimumAge = 18;
    private const int MaximumPlausibleAge = 120;

    // Türkiye has been on fixed UTC+3 since 2016. Using the Turkish calendar date keeps the
    // server from refusing someone whose 18th birthday is "today" in Türkiye but not yet in UTC.
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public bool Enforce => configuration.GetValue("AgeGate:Enforce", false);

    public bool GuestVote => configuration.GetValue("AgeGate:GuestVote", false);

    public static DateOnly TodayInTurkey(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(utcNow.ToOffset(TurkeyOffset).DateTime);

    public static DateOnly TodayInTurkey() => TodayInTurkey(DateTimeOffset.UtcNow);

    public static int AgeOn(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;
        return age;
    }

    // A future date or one more than 120 years ago is a typo, not an age statement: it is
    // rejected as invalid and does not lock the device.
    public static AgeGateDecision Evaluate(DateOnly dateOfBirth, DateOnly today)
    {
        if (dateOfBirth > today) return AgeGateDecision.InvalidDate;
        var age = AgeOn(dateOfBirth, today);
        if (age > MaximumPlausibleAge) return AgeGateDecision.InvalidDate;
        return age >= MinimumAge ? AgeGateDecision.Eligible : AgeGateDecision.NotEligible;
    }

    public static IResult NotEligibleResult() =>
        Forbidden(AgeGateCodes.NotEligible, AgeGateCodes.NotEligibleMessage);

    public static IResult InvalidDateResult() =>
        Results.BadRequest(new ErrorEnvelope(new ErrorBody(
            AgeGateCodes.InvalidDateOfBirth, AgeGateCodes.InvalidDateOfBirthMessage)));

    public static async Task<bool> IsDeviceLockedAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "SELECT age_gate_failed_at IS NOT NULL FROM devices WHERE id = @deviceId",
            connection,
            transaction);
        command.Parameters.AddWithValue("deviceId", deviceId);
        return await command.ExecuteScalarAsync() is true;
    }

    // Idempotent: the first failure timestamp is kept.
    public static async Task MarkDeviceFailedAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE devices SET age_gate_failed_at = COALESCE(age_gate_failed_at, NOW()) WHERE id = @deviceId",
            connection,
            transaction);
        command.Parameters.AddWithValue("deviceId", deviceId);
        await command.ExecuteNonQueryAsync();
    }

    // Idempotent: the first declaration timestamp is kept.
    public static async Task MarkDeviceDeclaredAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE devices SET age_declared_at = COALESCE(age_declared_at, NOW()) WHERE id = @deviceId",
            connection,
            transaction);
        command.Parameters.AddWithValue("deviceId", deviceId);
        await command.ExecuteNonQueryAsync();
    }

    // Account-level refusal (users.age_gate_failed_at). Idempotent: the first timestamp is kept.
    // date_of_birth stays NULL: the declared date of an ineligible person is never stored.
    public static async Task MarkAccountFailedAsync(
        NpgsqlConnection connection,
        Guid userId,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE users SET age_gate_failed_at = COALESCE(age_gate_failed_at, NOW()), updated_at = NOW() WHERE id = @userId",
            connection,
            transaction);
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync();
    }

    // Returns the refusal (401 or 403) when the write must be stopped, otherwise null. With both
    // flags off (the default) it returns immediately without touching the database.
    public async Task<IResult?> CheckWriteAsync(HttpRequest request, AgeGateScope scope)
    {
        var enforce = Enforce;
        var guestVote = scope == AgeGateScope.Vote && GuestVote;
        if (!enforce && !guestVote) return null;

        var bearerToken = ReadBearerToken(request);
        var userId = bearerToken is null ? null : TryGetUserId(bearerToken);
        var deviceToken = request.Headers["X-Device-Token"].ToString();
        var hasDeviceToken = !string.IsNullOrWhiteSpace(deviceToken);

        if (!enforce)
        {
            // AgeGate:GuestVote alone: signed-in voters pass, a guest device needs the declaration.
            if (userId is not null || !hasDeviceToken) return null; // no device: the endpoint answers 401
            await using var guestConnection = await db.OpenConnectionAsync();
            var guestDevice = await ReadDeviceStateAsync(guestConnection, deviceToken);
            if (guestDevice is null) return null; // unknown device: the endpoint answers 401
            if (guestDevice.Value.Failed) return NotEligibleResult();
            return guestDevice.Value.Declared ? null : DeclarationRequiredResult();
        }

        // AgeGate:Enforce. A Bearer token that does not validate (expired, forged) is a sign-in
        // problem in every scope: 401 lets the client refresh and retry, instead of the write
        // silently falling back to the guest device.
        if (bearerToken is not null && userId is null) return SignInRequiredResult();
        if (userId is null && scope == AgeGateScope.UserWrite) return SignInRequiredResult();
        if (userId is null && !hasDeviceToken) return null; // guest vote without a device: 401 from the endpoint

        await using var connection = await db.OpenConnectionAsync();

        // A refused device stays refused for every write, signed in or not.
        var device = hasDeviceToken ? await ReadDeviceStateAsync(connection, deviceToken) : null;
        if (device is { Failed: true }) return NotEligibleResult();

        if (userId is not null)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT date_of_birth IS NULL, age_gate_failed_at IS NOT NULL
                FROM users
                WHERE id = @userId AND deleted_at IS NULL
                """,
                connection);
            command.Parameters.AddWithValue("userId", userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return SignInRequiredResult(); // deleted or unknown account
            var missingDateOfBirth = reader.GetBoolean(0);
            var accountRefused = reader.GetBoolean(1);
            if (accountRefused) return NotEligibleResult();
            return missingDateOfBirth
                ? Forbidden(AgeGateCodes.VerificationRequired, AgeGateCodes.VerificationRequiredMessage)
                : null;
        }

        // Guest post vote, the only guest write left under Enforce.
        if (!guestVote || device is null) return null; // unknown device: the endpoint answers 401
        return device.Value.Declared ? null : DeclarationRequiredResult();
    }

    // (declared, failed) for a device token; null when the device is unknown or banned.
    private static async Task<(bool Declared, bool Failed)?> ReadDeviceStateAsync(
        NpgsqlConnection connection,
        string deviceToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT age_declared_at IS NOT NULL, age_gate_failed_at IS NOT NULL
            FROM devices
            WHERE device_token = @token AND is_banned = FALSE
            """,
            connection);
        command.Parameters.AddWithValue("token", deviceToken);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    private Guid? TryGetUserId(string bearerToken)
    {
        var principal = jwtService.ValidateAccessToken(bearerToken);
        var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private static IResult SignInRequiredResult() =>
        Results.Json(
            new ErrorEnvelope(new ErrorBody(AgeGateCodes.SignInRequired, AgeGateCodes.SignInRequiredMessage)),
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult DeclarationRequiredResult() =>
        Forbidden(AgeGateCodes.DeclarationRequired, AgeGateCodes.DeclarationRequiredMessage);

    private static IResult Forbidden(string code, string message) =>
        Results.Json(new ErrorEnvelope(new ErrorBody(code, message)), statusCode: StatusCodes.Status403Forbidden);
}

// Attached with .RequireAgeGate(...) so the gate stays out of the endpoint bodies.
public sealed class AgeGateEndpointFilter(AgeGateScope scope) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var gate = context.HttpContext.RequestServices.GetRequiredService<AgeGateService>();
        var refusal = await gate.CheckWriteAsync(context.HttpContext.Request, scope);
        return refusal ?? await next(context);
    }
}

public static class AgeGateEndpointExtensions
{
    public static TBuilder RequireAgeGate<TBuilder>(this TBuilder builder, AgeGateScope scope = AgeGateScope.UserWrite)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new AgeGateEndpointFilter(scope));
}
