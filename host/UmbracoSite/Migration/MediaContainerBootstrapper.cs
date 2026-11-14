using Azure.Storage.Blobs;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace UmbracoSite.Migration;

/// <summary>
/// Development only: the Umbraco blob provider does not create its container, so the first media
/// upload against a fresh Azurite fails with ContainerNotFound. This creates the container on start.
/// </summary>
public sealed class MediaContainerBootstrapper : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MediaContainerBootstrapper> _logger;

    public MediaContainerBootstrapper(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<MediaContainerBootstrapper> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        string? connectionString = _configuration["Umbraco:Storage:AzureBlob:Media:ConnectionString"];
        string? containerName = _configuration["Umbraco:Storage:AzureBlob:Media:ContainerName"];
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(containerName))
        {
            return;
        }

        try
        {
            var container = new BlobContainerClient(connectionString, containerName);
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Azurite not running: the host still starts, media uploads fail until it is.
            _logger.LogWarning(ex, "Could not ensure the blob container '{Container}' exists. Is Azurite running?", containerName);
        }
    }
}

/// <summary>Registers <see cref="MediaContainerBootstrapper"/>.</summary>
public sealed class MediaContainerBootstrapperComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, MediaContainerBootstrapper>();
}
