using System.Text.Json.Serialization;

namespace Karar.Api.Common;

/// Global exception middleware'in istemciye yazdığı gövde.
/// Şekil Flutter istemcisinin ayrıştırdığı { error: { code, message } } zarfıyla aynıdır;
/// traceId log satırıyla eşleşir, böylece destek istemcideki hatayı sunucu logunda bulabilir.
public static class UnhandledExceptionResponse
{
    public const string Code = "INTERNAL_ERROR";
    public const string GenericMessage = "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.";

    /// Development'ta ex.Message ve ex.ToString() (detail) korunur; diğer tüm ortamlarda
    /// (Staging, Production, Testing) yalnızca genel mesaj döner ve detail hiç yazılmaz.
    public static UnhandledErrorEnvelope BuildErrorBody(bool isDevelopment, Exception ex, string traceId) =>
        isDevelopment
            ? new(new UnhandledErrorBody(Code, ex.Message, traceId, ex.ToString()))
            : new(new UnhandledErrorBody(Code, GenericMessage, traceId));
}

public sealed record UnhandledErrorEnvelope(UnhandledErrorBody Error);

public sealed record UnhandledErrorBody(
    string Code,
    string Message,
    string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null);
