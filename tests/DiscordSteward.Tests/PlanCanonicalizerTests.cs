using System.Collections.Immutable;
using DiscordSteward.Domain;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class OperationCanonicalizerTests
{
    private const ulong GuildId = 100000000000000001UL;

    [Fact]
    public void InvocationDigest_IsDeterministicAndBindsEveryApprovedField()
    {
        var invocation = new ChannelUpdateInvocation(
            "01900000-0000-7000-8000-000000000001",
            "20",
            "Configure announcements",
            new string('a', 64),
            new ChannelUpdateSet("announcements", "Updates", 30),
            ["topic"]);

        var firstJson = OperationCanonicalizer.Serialize(invocation);
        var secondJson = OperationCanonicalizer.Serialize(invocation with
        {
            Clear = ImmutableArray.Create("topic")
        });
        var first = OperationCanonicalizer.ComputeDigest(firstJson);
        var second = OperationCanonicalizer.ComputeDigest(secondJson);

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, OperationCanonicalizer.ComputeDigest(
            OperationCanonicalizer.Serialize(invocation with { Reason = "Different" })));
    }

    [Fact]
    public void ChannelDigest_ChangesOnlyWithBoundResourceState()
    {
        var first = CreateChannel("general", topic: null, slowModeSeconds: 0, categoryId: 10);
        var same = CreateChannel("general", topic: null, slowModeSeconds: 0, categoryId: 10);
        var changed = CreateChannel("general", topic: "Changed", slowModeSeconds: 0, categoryId: 10);

        Assert.Equal(ChannelStateCanonicalizer.ComputeDigest(first), ChannelStateCanonicalizer.ComputeDigest(same));
        Assert.NotEqual(ChannelStateCanonicalizer.ComputeDigest(first), ChannelStateCanonicalizer.ComputeDigest(changed));
        Assert.Equal(ChannelStateCanonicalizer.ToMutableState(first), ChannelStateCanonicalizer.ToMutableState(same));
        Assert.NotEqual(ChannelStateCanonicalizer.ToMutableState(first), ChannelStateCanonicalizer.ToMutableState(changed));
    }

    private static ChannelSnapshot CreateChannel(
        string name,
        string? topic,
        int slowModeSeconds,
        ulong? categoryId) => ChannelSnapshot.Create(
            GuildId,
            20,
            name,
            "text",
            0,
            categoryId,
            topic,
            false,
            slowModeSeconds,
            []);
}
