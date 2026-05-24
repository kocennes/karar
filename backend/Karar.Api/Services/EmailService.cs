using MailKit.Net.Smtp;
using MimeKit;

namespace Karar.Api.Services;

public sealed class EmailService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUser;
    private readonly string _smtpPass;
    private readonly string _fromAddress;
    private readonly string _fromName;

    private readonly bool _configured;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _logger = logger;
        _smtpHost = configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(configuration["Email:SmtpPort"] ?? "587");
        _smtpUser = configuration["Email:SmtpUser"] ?? "";
        _smtpPass = configuration["Email:SmtpPass"] ?? "";
        _fromAddress = configuration["Email:FromAddress"] ?? _smtpUser;
        _fromName = configuration["Email:FromName"] ?? "Karar";
        _configured = !string.IsNullOrEmpty(_smtpUser);
        if (!_configured)
        {
            logger.LogWarning("E-posta yapılandırması eksik. OTP e-postaları gönderilmeyecek.");
        }
    }

    public Task SendOtpAsync(string toEmail, string otp) =>
        SendAsync(toEmail, "Karar — E-posta Doğrulama Kodunuz", $"""
                Karar'a Hoş Geldiniz!

                Doğrulama kodunuz: {otp}

                Bu kod 10 dakika geçerlidir. Kodu kimseyle paylaşmayın.

                Eğer bu işlemi siz yapmadıysanız bu e-postayı görmezden gelebilirsiniz.
                """);

    public Task SendPasswordResetOtpAsync(string toEmail, string otp) =>
        SendAsync(toEmail, "Karar — Şifre Sıfırlama Kodunuz", $"""
                Şifre sıfırlama kodunuz: {otp}

                Bu kod 10 dakika geçerlidir. Kodu kimseyle paylaşmayın.

                Eğer bu işlemi siz yapmadıysanız bu e-postayı görmezden gelebilirsiniz.
                """);

    public Task SendChangeEmailOtpAsync(string toEmail, string otp) =>
        SendAsync(toEmail, "Karar — E-posta Değişikliği Doğrulama Kodunuz", $"""
                E-posta değişikliği için doğrulama kodunuz: {otp}

                Bu kod 10 dakika geçerlidir. Kodu kimseyle paylaşmayın.

                Eğer bu işlemi siz yapmadıysanız şifrenizi değiştirmenizi öneririz.
                """);

    public Task SendAdminLoginOtpAsync(string toEmail, string otp) =>
        SendAsync(toEmail, "Karar Admin giris kodunuz", $"""
                Karar Admin giris kodunuz: {otp}

                Bu kod 10 dakika gecerlidir. Kodu kimseyle paylasmayin.
                """);

    public Task SendAccountRecoveryAsync(string toEmail, string username, string recoveryUrl) =>
        SendAsync(toEmail, "Karar — Hesabınızı Geri Alın", $"""
                Merhaba {username},

                Hesabınızı sildiniz. 30 gün içinde geri almak için aşağıdaki bağlantıyı kullanabilirsiniz:

                {recoveryUrl}

                Bu bağlantı 30 gün geçerlidir. Hesabınızı geri almak istemiyorsanız bu e-postayı görmezden gelebilirsiniz.
                """);

    public Task SendAdminAlertAsync(string subject, string body) =>
        SendAsync("admin@karar.app", subject, body);

    private async Task SendAsync(string toEmail, string subject, string body)
    {
        if (!_configured) return;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_smtpHost, _smtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtpUser, _smtpPass);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP gönderim hatası: {To} {Subject}", toEmail, subject);
            throw;
        }
    }
}
