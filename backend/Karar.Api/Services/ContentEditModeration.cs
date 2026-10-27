namespace Karar.Api.Services;

// Post/yorum düzenlemeleri oluşturma ile aynı moderasyon hattından geçer
// (ContentModerationService: TC kimlik/IBAN/kart, tehdit/nefret, telefon/e-posta, kriz;
// postlarda ayrıca Perspective). Aksi halde zararsız bir içerik onaydan geçip düzenleme
// penceresinde doxxing/tehdit metnine çevrilebiliyordu.
//
// Oluşturmada inceleme gerektiren içerik 'under_review' olarak saklanır. Düzenlemede ise daha
// önce onaylanmış metin yayında kalır ve incelemeye düşecek düzenleme kaydedilmez: içerik
// feed'den kaybolmaz, yorum sayaçları ve SSE akışı tutarlı kalır.
public static class ContentEditModeration
{
    public const string RequiresReviewCode = "EDIT_REQUIRES_REVIEW";

    public const string RequiresReviewMessage =
        "Bu düzenleme moderasyon incelemesi gerektirdiği için kaydedilmedi; içeriğin önceki hali yayında kalır.";

    public static ContentEditDecision Decide(ModerationDecision moderation, PerspectiveResult? perspective = null)
    {
        if (moderation.IsRejected)
        {
            return ContentEditDecision.Reject(moderation.Code ?? "CONTENT_REJECTED", moderation.Message);
        }

        if (perspective is not null)
        {
            var perspectiveStatus = PerspectiveApiService.DetermineStatus(perspective);
            if (perspectiveStatus == "rejected")
            {
                return ContentEditDecision.Reject(
                    "CONTENT_REJECTED",
                    "İçerik politikası gereği bu metin yayınlanamaz.");
            }

            if (perspectiveStatus == "under_review")
            {
                return ContentEditDecision.RequiresReview(moderation.IsCrisisFlagged);
            }
        }

        return moderation.Status == "active"
            ? ContentEditDecision.Allow(moderation.Message)
            : ContentEditDecision.RequiresReview(moderation.IsCrisisFlagged);
    }
}

public enum ContentEditOutcome
{
    Allowed,
    Rejected,
    RequiresReview
}

public sealed record ContentEditDecision(
    ContentEditOutcome Outcome,
    string? ErrorCode,
    string Message,
    bool IsCrisisFlagged
)
{
    public static ContentEditDecision Allow(string message) =>
        new(ContentEditOutcome.Allowed, null, message, false);

    public static ContentEditDecision Reject(string code, string message) =>
        new(ContentEditOutcome.Rejected, code, message, false);

    public static ContentEditDecision RequiresReview(bool isCrisisFlagged) =>
        new(
            ContentEditOutcome.RequiresReview,
            ContentEditModeration.RequiresReviewCode,
            ContentEditModeration.RequiresReviewMessage,
            isCrisisFlagged);
}
