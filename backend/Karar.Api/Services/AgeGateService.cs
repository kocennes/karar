using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Karar.Api.Contracts;
using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

// P0-02: temporary neutral 18+ age gate, in place until the e-Devlet age token (P0-01) lands.
//
// "Neutral" (FTC NGL settlement): the UI never names the threshold, and a refusal neither
// explains why nor hints that a different date would work. A device that declared an
// ineligible birth date stays refused (devices.age_gate_failed_at, V95). The declared date
// of an ineligible person is never stored.
//
// Flags (both default false, see appsettings.json):
//   AgeGate:Enforce    write endpoints answer 403 AGE_VERIFICATION_REQUIRED to signed-in users
//                      whose date_of_birth is NULL. Turn on only after the app version with the
//                      onboarding step (POST /api/v1/users/me/onboarding) is live.
//   AgeGate:GuestVote  a guest device must make a neutral age declaration
//                      (POST /api/v1/devices/age-declaration) before it can vote.
//                      Waits for the legal opinion (P0-01).
public static class AgeGateCodes
{
    public const string NotEligible = "AGE_NOT_ELIGIBLE";
    public const string VerificationRequired = "AGE_VERIFICATION_REQUIRED";
    public const string DeclarationRequired = "AGE_DECLARATION_REQUIRED";
    public const string InvalidDateOfBirth = "INVALID_DATE_OF_BIRTH";

    // Neutral copy: no threshold, no reason, no invitation to try another date.
    public const string NotEligibleMessage = "Üzgünüz, Karar'ı kullanamıyorsun.";
    public const string VerificationRequiredMessage = "Devam etmek için doğum tarihini girmen gerekiyor.";
    public const string DeclarationRequiredMessage = "Oy vermeden önce doğum tarihini girmen gerekiyor.";
    public const string InvalidDateOfBirthMessage = "Geçerli bir doğum tarihi gir.";
}

public enum AgeGateDecision
{
    Eligible,
    NotEligible,
    InvalidDate
}

public enum AgeGateScope
{
    // Signed-in writes: create post, comment, comment reactions and up/down votes.
    UserWrite,
    // Post vote: signed-in users as UserWrite, guests behind AgeGate:GuestVote.
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

    // Returns a 403 result when the request must be refused, otherwise null. With both flags
    // off (the default) it returns immediately without touching the database.
    public async Task<IResult?> CheckWriteAsync(HttpRequest request, AgeGateScope scope)
    {
        var enforce = Enforce;
        var guestVote = scope == AgeGateScope.Vote && GuestVote;
        if (!enforce && !guestVote) return null;

        var userId = TryGetUserId(request);
        if (userId is not null)
        {
            if (!enforce) return null;

            await using var connection = await db.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT date_of_birth IS NULL FROM users WHERE id = @userId AND deleted_at IS NULL",
                connection);
            command.Parameters.AddWithValue("userId", userId.Value);
            return await command.ExecuteScalarAsync() is true
                ? Forbidden(AgeGateCodes.VerificationRequired, AgeGateCodes.VerificationRequiredMessage)
                : null;
        }

        if (!guestVote) return null;

        var deviceToken = request.Headers["X-Device-Token"].ToString();
        if (string.IsNullOrWhiteSpace(deviceToken)) return null; // the endpoint answers 401 itself

        await using (var connection = await db.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand(
            """
            SELECT age_declared_at IS NOT NULL, age_gate_failed_at IS NOT NULL
            FROM devices
            WHERE device_token = @token AND is_banned = FALSE
            """,
            connection))
        {
            command.Parameters.AddWithValue("token", deviceToken);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null; // unknown device: the endpoint answers 401

            var declared = reader.GetBoolean(0);
            var failed = reader.GetBoolean(1);
            if (failed) return NotEligibleResult();
            if (!declared) return Forbidden(AgeGateCodes.DeclarationRequired, AgeGateCodes.DeclarationRequiredMessage);
        }

        return null;
    }

    private Guid? TryGetUserId(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var principal = jwtService.ValidateAccessToken(header["Bearer ".Length..].Trim());
        var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

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
