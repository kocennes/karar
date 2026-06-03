using System.Net.Http.Json;
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
    private readonly string? _resendApiKey;
    private readonly string? _brevoApiKey;

    private readonly bool _configured;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _smtpHost = configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(configuration["Email:SmtpPort"] ?? "587");
        _smtpUser = configuration["Email:SmtpUser"] ?? "";
        _smtpPass = configuration["Email:SmtpPass"] ?? "";
        _fromAddress = configuration["Email:FromAddress"] ?? _smtpUser;
        _fromName = configuration["Email:FromName"] ?? "Karar";
        _resendApiKey = configuration["Resend:ApiKey"];
        _brevoApiKey = configuration["Brevo:ApiKey"];

        _configured = !string.IsNullOrEmpty(_brevoApiKey)
                   || !string.IsNullOrEmpty(_resendApiKey)
                   || !string.IsNullOrEmpty(_smtpUser);

        if (!_configured)
            logger.LogWarning("E-posta yapılandırması eksik. OTP e-postaları gönderilmeyecek.");
        else if (!string.IsNullOrEmpty(_brevoApiKey))
            logger.LogInformation("E-posta servisi: Brevo API");
        else if (!string.IsNullOrEmpty(_resendApiKey))
            logger.LogInformation("E-posta servisi: Resend API");
        else
            logger.LogInformation("E-posta servisi: SMTP ({Host}:{Port})", _smtpHost, _smtpPort);
    }

    public bool IsConfigured => _configured;

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
        SendAsync(toEmail, "Karar Admin Giriş Kodunuz", $"""
                Karar Admin giriş kodunuz: {otp}

                Bu kod 10 dakika geçerlidir. Kodu kimseyle paylaşmayın.
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
        if (!_configured)
        {
            _logger.LogWarning(
                "[DEV] E-posta yapılandırılmamış — console'a yazıldı | To={To} | Subject={Subject} | Body={Body}",
                toEmail, subject, body);
            return;
        }

        if (!string.IsNullOrEmpty(_brevoApiKey))
        {
            try
            {
                await SendViaBrevoAsync(toEmail, subject, body);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brevo başarısız, Resend'e geçiliyor");
            }
        }

        if (!string.IsNullOrEmpty(_resendApiKey))
        {
            try
            {
                await SendViaResendAsync(toEmail, subject, body);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend başarısız, SMTP'ye geçiliyor");
            }
        }

        await SendViaSmtpAsync(toEmail, subject, body);
    }

    private async Task SendViaBrevoAsync(string toEmail, string subject, string body)
    {
        var client = _httpClientFactory.CreateClient("brevo");
        var payload = new
        {
            sender = new { name = _fromName, email = _fromAddress },
            to = new[] { new { email = toEmail } },
            subject,
            textContent = body,
        };

        var response = await client.PostAsJsonAsync("smtp/email", payload);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo API hatası: {Status} {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"Brevo API hatası: {response.StatusCode}");
        }

        _logger.LogInformation("Brevo: e-posta gönderildi → {To}", toEmail);
    }

    private async Task SendViaResendAsync(string toEmail, string subject, string body)
    {
        var client = _httpClientFactory.CreateClient("resend");
        var payload = new
        {
            from = $"{_fromName} <{_fromAddress}>",
            to = new[] { toEmail },
            subject,
            text = body,
        };

        var response = await client.PostAsJsonAsync("emails", payload);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Resend API hatası: {Status} {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"Resend API hatası: {response.StatusCode}");
        }

        _logger.LogInformation("Resend: e-posta gönderildi → {To}", toEmail);
    }

    private async Task SendViaSmtpAsync(string toEmail, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _smtpPort == 465
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls;
            await client.ConnectAsync(_smtpHost, _smtpPort, socketOptions);
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
