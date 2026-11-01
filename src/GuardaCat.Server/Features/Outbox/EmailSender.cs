using System.Text.Json;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;

namespace GuardaCat.Server.Features.Outbox;

public sealed class EmailOptions
{
    public string Provider { get; set; } = "SES";
    public string Region { get; set; } = "eu-west-1";
    public string From { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string CapturePath { get; set; } = "App_Data/mail";
}

public interface IEmailSender
{
    Task SendAsync(OutboxMessage message, CancellationToken ct);
}

public sealed class CaptureEmailSender(IOptions<EmailOptions> options, IWebHostEnvironment environment) : IEmailSender
{
    public async Task SendAsync(OutboxMessage message, CancellationToken ct)
    {
        var directory = Path.Combine(environment.ContentRootPath, options.Value.CapturePath);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{message.Id:N}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            message.Id, To = message.Recipient, message.Subject, message.Body
        }), ct);
    }
}

public sealed class SesEmailSender(IAmazonSimpleEmailServiceV2 ses, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutboxMessage message, CancellationToken ct) =>
        await ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = options.Value.From,
            Destination = new Destination { ToAddresses = [message.Recipient!] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                    Body = new Body { Text = new Content { Data = message.Body, Charset = "UTF-8" } }
                }
            }
        }, ct);
}
