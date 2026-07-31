using System.Collections.Immutable;
using DiscordSteward.Discord;
using DiscordSteward.Domain;

namespace DiscordSteward.Tools;

public sealed record ToolResultEnvelope<T>(
    int SchemaVersion,
    string OperationId,
    string Outcome,
    GuildReference? Guild,
    T? Data,
    ImmutableArray<string> Warnings,
    ToolError? Error)
    where T : class;

public sealed record OperationToolResultEnvelope<T>(
    int SchemaVersion,
    string RequestId,
    string Outcome,
    GuildReference? Guild,
    T? Data,
    ImmutableArray<string> Warnings,
    ToolError? Error)
    where T : class;

public sealed record ToolError(string Code, string Message, string CorrelationId);

public sealed record ChannelDetailSnapshot(
    string ResourceType,
    string GuildId,
    string Id,
    string Name,
    string Kind,
    int Position,
    string? CategoryId,
    string? ParentChannelId,
    string? Topic,
    bool? IsNsfw,
    int? SlowModeSeconds,
    string? StateDigest,
    PermissionOverwriteSummary PermissionOverwrites,
    string? MutationStateDigest = null,
    string? AvailableTagsDigest = null,
    ChannelMutationSettingsView? MutationSettings = null);
