using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace UmbracoSite.Migration;

/// <summary>
/// Development only: on application start creates the API user "Kentico migrator" (administrators group)
/// with the client credentials from <c>Migration:ApiClient:Id</c> and <c>Migration:ApiClient:Secret</c>.
/// Idempotent: an existing client id is left untouched.
/// </summary>
public sealed class ApiClientSeeder : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string UserName = "Kentico migrator";
    private const string UserEmail = "kentico-migrator@example.test";
    private const string ClientIdPrefix = "umbraco-back-office-";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IUserService _userService;
    private readonly IBackOfficeUserClientCredentialsManager _credentialsManager;
    private readonly ILogger<ApiClientSeeder> _logger;

    public ApiClientSeeder(
        IConfiguration configuration,
        IHostEnvironment environment,
        IUserService userService,
        IBackOfficeUserClientCredentialsManager credentialsManager,
        ILogger<ApiClientSeeder> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _userService = userService;
        _credentialsManager = credentialsManager;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        string clientId = _configuration["Migration:ApiClient:Id"] ?? "umbraco-back-office-migrator";
        string? secret = _configuration["Migration:ApiClient:Secret"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("Migration:ApiClient:Secret is not set; the migrator API user is not seeded.");
            return;
        }

        if (!clientId.StartsWith(ClientIdPrefix, StringComparison.Ordinal))
        {
            _logger.LogError("Migration:ApiClient:Id must start with '{Prefix}'; the migrator API user is not seeded.", ClientIdPrefix);
            return;
        }

        if (await _credentialsManager.FindUserAsync(clientId) is not null)
        {
            // Rotating the secret needs the API user to be deleted in the back office first (host/README.md).
            _logger.LogWarning(
                "API client {ClientId} already exists; a changed Migration:ApiClient:Secret is not applied.", clientId);
            return;
        }

        // A previous start may have created the user and then failed to attach the credentials.
        var userKey = FindExistingApiUserKey();
        if (userKey is null)
        {
            userKey = await CreateApiUserAsync();
            if (userKey is null)
            {
                return;
            }
        }

        var saved = await _credentialsManager.SaveAsync(userKey.Value, clientId, secret);
        if (!saved.Success)
        {
            _logger.LogError("Could not save the migrator client credentials: {Status}", saved.Result);
            return;
        }

        _logger.LogInformation("Seeded API user '{UserName}' with client id {ClientId}.", UserName, clientId);
    }

    private Guid? FindExistingApiUserKey()
    {
        var matches = _userService.GetAll(0, 10, out _, "email", Direction.Ascending, userState: null, userGroups: null, filter: UserEmail);
        return matches.FirstOrDefault(u => u.Kind == UserKind.Api && string.Equals(u.Email, UserEmail, StringComparison.OrdinalIgnoreCase))?.Key;
    }

    private async Task<Guid?> CreateApiUserAsync()
    {
        var model = new UserCreateModel
        {
            Name = UserName,
            UserName = UserEmail,
            Email = UserEmail,
            Kind = UserKind.Api,
            UserGroupKeys = new HashSet<Guid> { Constants.Security.AdminGroupKey },
        };

        var created = await _userService.CreateAsync(Constants.Security.SuperUserKey, model, approveUser: true);
        if (!created.Success)
        {
            _logger.LogError("Could not create the migrator API user: {Status}", created.Status);
            return null;
        }

        return created.Result.CreatedUser!.Key;
    }
}

/// <summary>Registers <see cref="ApiClientSeeder"/>.</summary>
public sealed class ApiClientSeederComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, ApiClientSeeder>();
}
