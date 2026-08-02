namespace DiscordSteward.Errors;

public static class StewardErrorCodes
{
    public const string InvalidArgument = "invalid_argument";
    public const string NotConfigured = "not_configured";
    public const string ToolNotEnabled = "tool_not_enabled";
    public const string DiscordAuthenticationFailed = "discord_authentication_failed";
    public const string DiscordPermissionDenied = "discord_permission_denied";
    public const string GuildFeatureUnavailable = "guild_feature_unavailable";
    public const string GuildMismatch = "guild_mismatch";
    public const string ResourceNotFound = "resource_not_found";
    public const string ResultTooLarge = "result_too_large";
    public const string AmbiguousName = "ambiguous_name";
    public const string PolicyDenied = "policy_denied";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string OperationInProgress = "operation_in_progress";
    public const string PlanExpired = "plan_expired";
    public const string ApprovalRequired = "approval_required";
    public const string StateConflict = "state_conflict";
    public const string AuditUnavailable = "audit_unavailable";
    public const string RateLimited = "rate_limited";
    public const string PartialFailure = "partial_failure";
    public const string OutcomeUnknown = "outcome_unknown";
    public const string DiscordUnavailable = "discord_unavailable";
}

public sealed class StewardException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
}
