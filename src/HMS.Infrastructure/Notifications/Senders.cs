using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HMS.Application.Notifications;
using HMS.Domain.Enums;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HMS.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    /// <summary>None (email disabled), Smtp, or Pickup (writes .eml files to a folder; for development and testing).</summary>
    public string Mode { get; set; } = "None";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    /// <summary>StartTls (port 587), SslOnConnect (port 465) or None (local relay only).</summary>
    public string Security { get; set; } = "StartTls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Hotel";
    public string? PickupDirectory { get; set; }
}

public sealed class SmsOptions
{
    public const string SectionName = "Sms";
    /// <summary>None (SMS disabled) or Termii.</summary>
    public string Provider { get; set; } = "None";
    public string BaseUrl { get; set; } = "https://api.ng.termii.com";
    public string? ApiKey { get; set; }
    public string? SenderId { get; set; }
}

internal static class MimeFactory
{
    public static MimeMessage Create(EmailOptions options, OutgoingMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.Recipient));
        mime.Subject = message.Subject ?? "";
        // Lets the mail server and the guest's client recognise a duplicate after an ambiguous retry.
        mime.MessageId = $"{message.Id:N}@hms.local";
        mime.Body = new TextPart("plain") { Text = message.Body };
        return mime;
    }
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;
    public string Provider => "smtp";

    public async Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct)
    {
        var o = options.Value;
        MimeMessage mime;
        try
        {
            mime = MimeFactory.Create(o, message);
        }
        catch (ParseException)
        {
            return SendResult.Fail("The recipient address is not valid.", permanent: true);
        }

        using var client = new SmtpClient { Timeout = 30_000 };
        var security = o.Security switch
        {
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        if (string.IsNullOrWhiteSpace(o.Host)) return SendResult.Fail("Email:Host is not configured.", permanent: true);
        await client.ConnectAsync(o.Host, o.Port, security, ct);
        if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        try
        {
            var response = await client.SendAsync(mime, ct);
            return SendResult.Ok(string.IsNullOrWhiteSpace(response) ? mime.MessageId : response[..Math.Min(response.Length, 200)]);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            return SendResult.Fail("The mail server rejected the recipient.", permanent: true);
        }
        finally
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
    }
}

/// <summary>Writes each email as an .eml file instead of sending it. For development, demos and automated tests.</summary>
internal sealed class PickupEmailSender(IOptions<EmailOptions> options) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;
    public string Provider => "pickup";

    public async Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct)
    {
        var folder = options.Value.PickupDirectory ?? Path.Combine(Path.GetTempPath(), "hms-mail");
        Directory.CreateDirectory(folder);
        MimeMessage mime;
        try
        {
            mime = MimeFactory.Create(options.Value, message);
        }
        catch (ParseException)
        {
            return SendResult.Fail("The recipient address is not valid.", permanent: true);
        }
        var path = Path.Combine(folder, $"{message.Id:N}.eml");
        await using var file = File.Create(path);
        await mime.WriteToAsync(file, ct);
        return SendResult.Ok(mime.MessageId);
    }
}

/// <summary>Termii SMS gateway (widely used in Nigeria).</summary>
internal sealed class TermiiSmsSender(HttpClient http, IOptions<SmsOptions> options) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Sms;
    public string Provider => "termii";

    public async Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct)
    {
        var o = options.Value;
        var to = new string(message.Recipient.Where(char.IsDigit).ToArray());
        if (to.StartsWith('0') && to.Length == 11) to = "234" + to[1..]; // local Nigerian format to international
        if (to.Length < 10) return SendResult.Fail("The phone number is not valid.", permanent: true);

        using var response = await http.PostAsJsonAsync($"{o.BaseUrl.TrimEnd('/')}/api/sms/send", new TermiiRequest(to, o.SenderId ?? "", message.Body,
            "plain", "generic", o.ApiKey ?? ""), ct);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<TermiiResponse>(ct);
            return SendResult.Ok(body?.MessageId);
        }
        // 4xx other than rate limiting will not succeed on retry.
        var permanent = (int)response.StatusCode is >= 400 and < 500 and not 429;
        return SendResult.Fail($"SMS gateway returned {(int)response.StatusCode}.", permanent);
    }

    private sealed record TermiiRequest(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("sms")] string Sms,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("channel")] string ChannelName,
        [property: JsonPropertyName("api_key")] string ApiKey);

    private sealed record TermiiResponse([property: JsonPropertyName("message_id")] string? MessageId);
}

/// <summary>Picks the email provider from Email:Mode when each message is sent, so configuration changes need no rebuild of the container.</summary>
internal sealed class ConfiguredEmailSender(IOptions<EmailOptions> options, SmtpEmailSender smtp, PickupEmailSender pickup) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;
    public string Provider => Current?.Provider ?? "none";

    private INotificationSender? Current => options.Value.Mode switch
    {
        var m when m.Equals("Smtp", StringComparison.OrdinalIgnoreCase) => smtp,
        var m when m.Equals("Pickup", StringComparison.OrdinalIgnoreCase) => pickup,
        _ => null,
    };

    public Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct) =>
        Current?.SendAsync(message, ct) ?? Task.FromResult(SendResult.Fail("No Email provider is configured.", permanent: true));
}

/// <summary>Picks the SMS provider from Sms:Provider when each message is sent.</summary>
internal sealed class ConfiguredSmsSender(IOptions<SmsOptions> options, TermiiSmsSender termii) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Sms;
    public string Provider => IsTermii ? termii.Provider : "none";

    private bool IsTermii => options.Value.Provider.Equals("Termii", StringComparison.OrdinalIgnoreCase);

    public Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct) =>
        IsTermii ? termii.SendAsync(message, ct) : Task.FromResult(SendResult.Fail("No Sms provider is configured.", permanent: true));
}
