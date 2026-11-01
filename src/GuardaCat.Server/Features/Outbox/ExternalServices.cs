using System.Net.Mail;
using Amazon;
using Amazon.S3;
using Amazon.SimpleEmailV2;
using GuardaCat.Server.Features.Media;
using Microsoft.Extensions.Options;

namespace GuardaCat.Server.Features.Outbox;

public static class ExternalServices
{
    public static void AddMediaAndOutbox(this WebApplicationBuilder builder)
    {
        var development = builder.Environment.IsDevelopment();
        builder.Services.AddOptions<MediaOptions>().BindConfiguration("Media")
            .Validate(options => options.Provider == "S3" && !string.IsNullOrWhiteSpace(options.Bucket)
                && !string.IsNullOrWhiteSpace(options.Region) || development && options.Provider == "Local",
                "Media requires S3 and a bucket; Local is only allowed in Development.")
            .ValidateOnStart();
        builder.Services.AddOptions<EmailOptions>().BindConfiguration("Email")
            .Validate(options => options.Provider == "SES" && MailAddress.TryCreate(options.From, out _)
                && !string.IsNullOrWhiteSpace(options.Region) || development && options.Provider == "Capture",
                "Email requires SES and a sender; Capture is only allowed in Development.")
            .Validate(options => options.PublicBaseUrl.Length <= 2000
                && Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
                && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment),
                "Email:PublicBaseUrl must be an absolute HTTPS URL without credentials, query or fragment.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IAmazonS3>(services => new AmazonS3Client(
            RegionEndpoint.GetBySystemName(services.GetRequiredService<IOptions<MediaOptions>>().Value.Region)));
        builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(services => new AmazonSimpleEmailServiceV2Client(
            RegionEndpoint.GetBySystemName(services.GetRequiredService<IOptions<EmailOptions>>().Value.Region)));
        builder.Services.AddSingleton<LocalMediaStorage>();
        builder.Services.AddSingleton<S3MediaStorage>();
        builder.Services.AddSingleton<IMediaStorage>(services =>
            services.GetRequiredService<IOptions<MediaOptions>>().Value.Provider == "Local"
                ? services.GetRequiredService<LocalMediaStorage>() : services.GetRequiredService<S3MediaStorage>());
        builder.Services.AddSingleton<CaptureEmailSender>();
        builder.Services.AddSingleton<SesEmailSender>();
        builder.Services.AddSingleton<IEmailSender>(services =>
            services.GetRequiredService<IOptions<EmailOptions>>().Value.Provider == "Capture"
                ? services.GetRequiredService<CaptureEmailSender>() : services.GetRequiredService<SesEmailSender>());
        builder.Services.AddScoped<MediaService>();
        builder.Services.AddScoped<OutboxDispatcher>();
        if (builder.Configuration.GetValue("Outbox:Enabled", true)) builder.Services.AddHostedService<OutboxWorker>();
    }
}
