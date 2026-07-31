using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Linting;
using DiscordSteward.Operations;
using DiscordSteward.Permissions;
using DiscordSteward.Secrets;
using DiscordSteward.Snapshots;
using DiscordSteward.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var commandArguments = args;
var builder = Host.CreateApplicationBuilder(commandArguments.Length == 0 ? args : []);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
var profilePath = builder.Configuration["Steward:ProfilePath"];
var explicitProfile = !string.IsNullOrWhiteSpace(profilePath);
if (explicitProfile)
{
    builder.Configuration
        .AddJsonFile(profilePath!, optional: false, reloadOnChange: false)
        .AddEnvironmentVariables();
    var profileVersion = builder.Configuration.GetValue<int?>("profileVersion");
    if (profileVersion != 1)
    {
        throw new InvalidOperationException(
            $"Unsupported Steward profile version {profileVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing"}; expected version 1.");
    }

    var externalPolicyPath = builder.Configuration["Steward:PolicyPath"];
    if (!string.IsNullOrWhiteSpace(externalPolicyPath))
    {
        builder.Configuration
            .AddJsonFile(externalPolicyPath, optional: false, reloadOnChange: false)
            .AddEnvironmentVariables();
    }
}
else
{
    builder.Configuration
        .AddJsonFile(
            builder.Configuration["Steward:PolicyPath"] ?? "config/steward-policy.json",
            optional: true,
            reloadOnChange: false)
        .AddEnvironmentVariables();
}

var profile = StewardProfileContext.Create(
    builder.Configuration["Steward:ProfileName"],
    explicitProfile,
    builder.Configuration["Discord:BotTokenSecretName"]);
var botToken = StewardProfileContext.ResolveBotToken(
    builder.Configuration["Discord:BotToken"],
    profile.BotTokenSecretName,
    secretName => builder.Configuration[$"DiscordTokens:{secretName}"]);

var discordOptions = DiscordOptions.FromValues(
    builder.Configuration["Discord:GuildId"],
    botToken);
var policySchemaVersion = builder.Configuration.GetValue("schemaVersion", 3);
var authorizationMode = StewardAuthorizationModeParser.Parse(
    policySchemaVersion,
    builder.Configuration["authorization:mode"]);

var defaultPolicy = StewardPolicy.CreateDefault(authorizationMode);
var protectedResourceIds = GetConfiguredValues(builder.Configuration, "protectedResourceIds");
var protectedNamePrefixes = GetConfiguredValues(builder.Configuration, "protectedNamePrefixes");
var deniedPermissions = GetConfiguredValues(builder.Configuration, "deniedPermissions");
var policy = StewardPolicy.Create(
    protectedResourceIds,
    protectedNamePrefixes.Count > 0 ? protectedNamePrefixes : defaultPolicy.ProtectedNamePrefixes,
    deniedPermissions.Count > 0 ? deniedPermissions : defaultPolicy.DeniedPermissions,
    authorizationMode);
var toolManifest = StewardToolManifest.Create(
    GetConfiguredValues(builder.Configuration, "toolAllowlist"),
    new StewardRiskGates(
        SensitiveReads: builder.Configuration.GetValue("riskGates:sensitiveReads", false),
        ReversibleWrites: builder.Configuration.GetValue("riskGates:reversibleWrites", false),
        PersonImpactingWrites: builder.Configuration.GetValue("riskGates:personImpactingWrites", false),
        IrreversibleWrites: builder.Configuration.GetValue("riskGates:irreversibleWrites", false)),
    authorizationMode);
if (authorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy &&
    (!toolManifest.IsCompleteCatalog || !toolManifest.RepresentsEveryRiskClass))
{
    throw new InvalidOperationException(
        "UnrestrictedAutonomy requires the complete current Steward catalog with every defined risk class represented.");
}
var assetVaultOptions = new AssetVaultOptions(
    builder.Configuration["Steward:AssetInboxPath"] ?? "data/asset-inbox",
    builder.Configuration["Steward:AssetVaultPath"] ?? "data/assets",
    builder.Configuration.GetValue("Steward:MaximumAssetBytes", 10L * 1024 * 1024));
var webhookSecretVaultOptions = new WebhookSecretVaultOptions(
    builder.Configuration["Steward:WebhookSecretVaultPath"] ?? "data/webhook-secrets");

builder.Services.AddSingleton(discordOptions);
builder.Services.AddSingleton(StewardRuntimeSettings.FromOptions(discordOptions));
builder.Services.AddSingleton(profile);
builder.Services.AddSingleton(policy);
builder.Services.AddSingleton(toolManifest);
builder.Services.AddSingleton(assetVaultOptions);
builder.Services.AddSingleton<IAssetVault, AssetVault>();
builder.Services.AddSingleton(webhookSecretVaultOptions);
builder.Services.AddSingleton<IWebhookSecretVault, WebhookSecretVault>();
builder.Services.AddSingleton<DiscordRestSession>();
builder.Services.AddSingleton<DiscordApiTransport>();
builder.Services.AddSingleton<IDiscordGuildReader, DiscordGuildReader>();
builder.Services.AddSingleton<IDiscordGuildWriter, DiscordGuildWriter>();
builder.Services.AddSingleton<IDiscordBotInspector, DiscordBotInspector>();
builder.Services.AddSingleton<IDiscordHttpApi, DiscordHttpApi>();
builder.Services.AddSingleton<IDiscordExtendedReader, DiscordExtendedReader>();
builder.Services.AddSingleton<DiscordCatalogReader>();
builder.Services.AddSingleton<IDiscordCatalogReader>(services => services.GetRequiredService<DiscordCatalogReader>());
builder.Services.AddSingleton<IDiscordGuildMutationStateReader>(services => services.GetRequiredService<DiscordCatalogReader>());
builder.Services.AddSingleton<IDiscordChannelStateReader, DiscordChannelStateReader>();
builder.Services.AddSingleton<IDiscordMessageReader, DiscordMessageReader>();
builder.Services.AddSingleton<IDiscordMemberReader, DiscordMemberReader>();
builder.Services.AddSingleton<GuildLinter>();
builder.Services.AddSingleton<RoleHierarchyAnalyzer>();
builder.Services.AddSingleton<ConfigurationAnalysisService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new SqliteOperationJournal(
    builder.Configuration["Steward:DataPath"] ?? "data/discord-steward.db"));
builder.Services.AddSingleton<IOperationJournal>(services =>
    services.GetRequiredService<SqliteOperationJournal>());
builder.Services.AddSingleton<IConfigurationSnapshotStore>(services =>
    services.GetRequiredService<SqliteOperationJournal>());
builder.Services.AddSingleton<ChannelUpdateOperationExecutor>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, RoleMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, ChannelMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, MemberMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, MessageMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, AutoModMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, CommunityMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, EventStageMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, ExpressionMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, IntegrationMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationFamilyHandler, BotGuildProfileMutationHandler>();
builder.Services.AddSingleton<ICatalogMutationAdapter, CatalogMutationAdapter>();
builder.Services.AddSingleton<CatalogMutationOperationExecutor>();
builder.Services.AddSingleton<ConfigurationSnapshotService>();
builder.Services.AddSingleton<DiscordReadTools>();
builder.Services.AddSingleton<StewardInfoTools>();
builder.Services.AddSingleton<DirectOperationTools>();
builder.Services.AddSingleton<ConfigurationSnapshotTools>();
builder.Services.AddSingleton<AnalysisTools>();
builder.Services.AddSingleton<ExtendedReadTools>();
builder.Services.AddSingleton<MemberReadTools>();
builder.Services.AddSingleton<MessageReadTools>();
builder.Services.AddSingleton<LocalCatalogTools>();
builder.Services.AddSingleton<CatalogMutationTools>();
builder.Services.AddSingleton<StewardRequestMetadataContext>();

var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();
mcpBuilder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
{
    var requestMetadata = request.Services!.GetRequiredService<StewardRequestMetadataContext>();
    using var scope = requestMetadata.Push(request.Params.Meta);
    return await next(request, cancellationToken).ConfigureAwait(false);
}));
mcpBuilder.WithTools(toolTypes: ManifestToolRegistry.Resolve(toolManifest));

using var host = builder.Build();
if (commandArguments.Length > 0)
{
    await Console.Error.WriteLineAsync("Discord Steward no longer exposes approval commands.");
    return 2;
}

await host.RunAsync();
return 0;

static IReadOnlyList<string> GetConfiguredValues(IConfiguration configuration, string key) =>
    configuration
        .GetSection(key)
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray();
