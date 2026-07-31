using System.Collections.Immutable;
using DiscordSteward.Discord;

namespace DiscordSteward.Tests;

public sealed class ChannelMutationStateTests
{
    [Fact]
    public void FullStateDigest_ChangesForOverwriteStatusAndForumTags()
    {
        var baseState = State();
        var overwrite = baseState with
        {
            PermissionOverwrites = [new ChannelOverwriteApiState("2", 0, "1024", "0")]
        };
        var status = baseState with { VoiceStatus = "Live" };
        var tags = baseState with
        {
            AvailableTags = [new ForumTagApiState("3", "tag", false, null, null)]
        };

        Assert.NotEqual(baseState.ComputeDigest(), overwrite.ComputeDigest());
        Assert.NotEqual(baseState.ComputeDigest(), status.ComputeDigest());
        Assert.NotEqual(baseState.ComputeAvailableTagsDigest(), tags.ComputeAvailableTagsDigest());
    }

    private static ChannelApiState State() => new(
        "1",
        "9",
        2,
        "channel",
        0,
        ParentId: null,
        Topic: null,
        Nsfw: false,
        RateLimitPerUser: 0,
        Bitrate: 64_000,
        UserLimit: 0,
        RtcRegion: null,
        VideoQualityMode: 1,
        DefaultAutoArchiveDuration: null,
        Flags: 0,
        VoiceStatus: null,
        PermissionOverwrites: [],
        AvailableTags: [],
        DefaultReactionEmojiId: null,
        DefaultReactionEmojiName: null,
        DefaultThreadRateLimitPerUser: null,
        DefaultSortOrder: null,
        DefaultForumLayout: null,
        Archived: null,
        Locked: null,
        Invitable: null,
        AutoArchiveDuration: null,
        AppliedTags: []);
}
