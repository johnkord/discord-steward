using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class CreateScheduledEventTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_scheduled_event", Title = "Create scheduled event", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one explicit voice, stage, external, or supported recurring event bound to the event-list digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed event, cover asset, recurrence, and list-state input.")] CreateScheduledEventInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_scheduled_event", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateScheduledEventTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_scheduled_event", Title = "Update scheduled event", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates explicit public metadata, times, entity, recurrence, location, channel, or cover on a nonterminal event.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current digest and typed set/clear event update.")] UpdateScheduledEventInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_scheduled_event", request_id, event_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class StartScheduledEventTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "start_scheduled_event", Title = "Start scheduled event", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Transitions one exact scheduled event from scheduled to active.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Start(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current scheduled-event state digest.")] TransitionScheduledEventInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("start_scheduled_event", request_id, event_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CompleteScheduledEventTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "complete_scheduled_event", Title = "Complete scheduled event", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Irreversibly transitions one exact active event to completed.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Complete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current active-event state digest.")] TransitionScheduledEventInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("complete_scheduled_event", request_id, event_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CancelScheduledEventTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "cancel_scheduled_event", Title = "Cancel scheduled event", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Irreversibly transitions one exact scheduled event to canceled.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Cancel(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current scheduled-event state digest.")] TransitionScheduledEventInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("cancel_scheduled_event", request_id, event_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteScheduledEventTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_scheduled_event", Title = "Delete scheduled event", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact scheduled event after current-state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current scheduled-event state digest.")] DeleteScheduledEventInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_scheduled_event", request_id, event_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class StartStageInstanceTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "start_stage_instance", Title = "Start stage instance", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Starts one exact stage with explicit topic, optional event association, and notification choice.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Start(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact stage channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact channel digest and complete stage start input.")] StartStageInstanceInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("start_stage_instance", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateStageInstanceTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_stage_instance", Title = "Update stage instance", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates the documented privacy level of one exact live stage.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact stage channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact stage-state digest and privacy level.")] UpdateStageInstanceInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_stage_instance", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class EndStageInstanceTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "end_stage_instance", Title = "End stage instance", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Ends one exact live stage after validating its full stage-state digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> End(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact stage channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current stage-state digest.")] EndStageInstanceInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("end_stage_instance", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetStageSpeakerTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_stage_speaker", Title = "Set stage speaker", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Suppresses or unsuppresses one exact member already connected to one exact live stage.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact stage channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact stage/member digests, member ID, and desired speaker state.")] SetStageSpeakerInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_stage_speaker", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class RequestStageSpeakingTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "request_stage_speaking", Title = "Request stage speaking", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets or clears Steward's own request-to-speak state in one exact live stage.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact stage channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact stage digest and explicit requesting value.")] RequestStageSpeakingInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("request_stage_speaking", request_id, channel_id, reason, input, cancellationToken);
}
