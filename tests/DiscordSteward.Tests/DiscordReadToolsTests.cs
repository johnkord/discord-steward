using System.Collections.Immutable;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Linting;
using DiscordSteward.Operations;
using DiscordSteward.Tools;

namespace DiscordSteward.Tests;

public sealed class DiscordReadToolsTests
{
    private const ulong GuildId = 100000000000000001UL;

    [Fact]
    public async Task ReadTools_ReturnFreshGuildBoundData()
    {
        var (tools, reader) = CreateTools(CreateSnapshot(GuildId));

        var snapshotResult = await tools.GetGuildConfigurationSnapshot(CancellationToken.None);
        var roleResult = await tools.GetRole("10", CancellationToken.None);
        var channelResult = await tools.GetChannel("20", CancellationToken.None);
        var lintResult = await tools.LintGuildConfiguration(CancellationToken.None);
        var permissionResult = await tools.ExplainRolePermissions(
            "10",
            CancellationToken.None,
            "21");

        Assert.Equal("ok", snapshotResult.Outcome);
        Assert.Equal("100000000000000001", snapshotResult.Guild?.Id);
        Assert.Equal("role", roleResult.Data?.Name);
        Assert.Equal("category", channelResult.Data?.ResourceType);
        Assert.NotNull(lintResult.Data);
        Assert.Equal("10", permissionResult.Data?.Role.Id);
        Assert.Equal("21", permissionResult.Data?.Channel?.Id);
        Assert.Equal(4, reader.ReadCount);
        Assert.All(
            [
                snapshotResult.OperationId,
                roleResult.OperationId,
                channelResult.OperationId,
                lintResult.OperationId,
                permissionResult.OperationId
            ],
            operationId => Assert.True(Guid.TryParseExact(operationId, "N", out _)));
    }

    [Fact]
    public async Task GetGuild_UsesRawFeatureEvidenceForDisplayAndPreconditionDigest()
    {
        var (tools, _) = CreateTools(CreateSnapshot(GuildId));

        var result = await tools.GetGuild(CancellationToken.None);

        Assert.Equal("ok", result.Outcome);
        Assert.Equal(["RAW_FEATURE"], result.Data?.Features);
        Assert.Equal(
            ResourceStateCanonicalizer.ComputeObjectDigest(new[] { "RAW_FEATURE" }.ToImmutableArray()),
            result.Data?.FeaturesStateDigest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-snowflake")]
    [InlineData("999")]
    public async Task GetRole_UnknownOrMalformedId_ReturnsSafeNotFound(string roleId)
    {
        var (tools, _) = CreateTools(CreateSnapshot(GuildId));

        var result = await tools.GetRole(roleId, CancellationToken.None);

        Assert.Equal("error", result.Outcome);
        Assert.Equal(StewardErrorCodes.ResourceNotFound, result.Error?.Code);
        Assert.Equal(result.OperationId, result.Error?.CorrelationId);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ExplainRolePermissions_UnknownChannel_ReturnsSafeNotFound()
    {
        var (tools, _) = CreateTools(CreateSnapshot(GuildId));

        var result = await tools.ExplainRolePermissions("10", CancellationToken.None, "999");

        Assert.Equal("error", result.Outcome);
        Assert.Equal(StewardErrorCodes.ResourceNotFound, result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ExplainRolePermissions_ThreadMissingParent_ReturnsSafeNotFound()
    {
        var snapshot = CreateSnapshotWithMissingThreadParent();
        var (tools, _) = CreateTools(snapshot);

        var result = await tools.ExplainRolePermissions("10", CancellationToken.None, "22");

        Assert.Equal("error", result.Outcome);
        Assert.Equal(StewardErrorCodes.ResourceNotFound, result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ReadTool_Unconfigured_DoesNotCallReader()
    {
        var policy = StewardPolicy.CreateDefault();
        var reader = new InMemoryDiscordGuildReader(CreateSnapshot(GuildId));
        var tools = new DiscordReadTools(
            reader,
            new FakeChannelStateReader(CreateSnapshot(GuildId)),
            new FakeGuildStateReader(),
            new GuildLinter(policy),
            new StewardRuntimeSettings(GuildId, DiscordTokenConfigured: false));

        var result = await tools.GetGuildConfigurationSnapshot(CancellationToken.None);

        Assert.Equal(StewardErrorCodes.NotConfigured, result.Error?.Code);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task ReadTool_MismatchedSnapshotGuild_FailsClosed()
    {
        var (tools, _) = CreateTools(CreateSnapshot(guildId: 42));

        var result = await tools.GetGuildConfigurationSnapshot(CancellationToken.None);

        Assert.Equal("error", result.Outcome);
        Assert.Equal(StewardErrorCodes.GuildMismatch, result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task R1Outputs_RedactMemberOverwriteIdentityButBindItsState()
    {
        const ulong memberId = 999_999;
        var baseSnapshot = CreateSnapshot(GuildId);
        var roleOverwrite = PermissionOverwriteSnapshot.Create(
            "role",
            10,
            ["ViewChannel"],
            []);
        var memberOverwrite = PermissionOverwriteSnapshot.Create(
            "member",
            memberId,
            ["SendMessages"],
            []);
        var channel = ChannelSnapshot.Create(
            GuildId,
            21,
            "channel",
            "text",
            0,
            20,
            null,
            false,
            0,
            [roleOverwrite, memberOverwrite]);
        var snapshot = GuildSnapshot.Create(
            GuildId,
            baseSnapshot.Name,
            ownerId: 1,
            baseSnapshot.Description,
            baseSnapshot.CreatedAt,
            baseSnapshot.Features,
            baseSnapshot.Roles,
            baseSnapshot.Categories,
            [channel]);
        var (tools, _) = CreateTools(snapshot);

        var aggregate = await tools.GetGuildConfigurationSnapshot(CancellationToken.None);
        var exact = await tools.GetChannel("21", CancellationToken.None);
        var lint = await tools.LintGuildConfiguration(CancellationToken.None);
        var sensitive = await tools.ListChannelMemberOverwrites(
            "21",
            limit: 100,
            afterMemberId: null,
            CancellationToken.None);
        var serialized = JsonSerializer.Serialize(new
        {
            Aggregate = aggregate.Data,
            Exact = exact.Data,
            Lint = lint.Data
        });

        Assert.DoesNotContain(memberId.ToString(System.Globalization.CultureInfo.InvariantCulture), serialized, StringComparison.Ordinal);
        Assert.Equal("10", aggregate.Data?.Channels.Single().PermissionOverwrites.RoleOverwrites.Single().TargetId);
        Assert.Equal(1, aggregate.Data?.Channels.Single().PermissionOverwrites.MemberOverwriteCount);
        Assert.Equal(64, aggregate.Data?.Channels.Single().PermissionOverwrites.MemberOverwriteDigest?.Length);
        Assert.Equal(1, exact.Data?.PermissionOverwrites.MemberOverwriteCount);
        Assert.Equal(memberId.ToString(System.Globalization.CultureInfo.InvariantCulture), sensitive.Data?.Items.Single().MemberId);
        var finding = Assert.Single(lint.Data!.Findings, finding => finding.Code == "member_specific_overwrite");
        Assert.Empty(finding.RelatedResourceIds);
        Assert.Contains(finding.Evidence, value => value == "member_overwrite_count:1");
        Assert.Contains(finding.Evidence, value => value.StartsWith("member_overwrite_digest:", StringComparison.Ordinal));
    }

    private static (DiscordReadTools Tools, InMemoryDiscordGuildReader Reader) CreateTools(
        GuildSnapshot snapshot)
    {
        var policy = StewardPolicy.CreateDefault();
        var reader = new InMemoryDiscordGuildReader(snapshot);
        var tools = new DiscordReadTools(
            reader,
            new FakeChannelStateReader(snapshot),
            new FakeGuildStateReader(),
            new GuildLinter(policy),
            new StewardRuntimeSettings(GuildId, DiscordTokenConfigured: true));

        return (tools, reader);
    }

    private sealed class FakeChannelStateReader(GuildSnapshot snapshot) : IDiscordChannelStateReader
    {
        public Task<ChannelMutationEvidence> GetMutationEvidenceAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChannelMutationEvidence(
                channelId,
                new string('a', 64),
                new string('b', 64)));
        }

        public Task<ExactChannelSnapshot> GetExactChannelAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var category = snapshot.Categories.SingleOrDefault(item => item.Id == channelId);
            var channel = snapshot.Channels.SingleOrDefault(item => item.Id == channelId);
            if (category is null && channel is null)
            {
                throw new StewardException(StewardErrorCodes.ResourceNotFound, "Missing channel.");
            }

            return Task.FromResult(new ExactChannelSnapshot(
                category,
                channel,
                new string('a', 64),
                new string('b', 64)));
        }
    }

    private sealed class FakeGuildStateReader : IDiscordGuildMutationStateReader
    {
        public Task<GuildMutationEvidence> GetGuildMutationEvidenceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GuildMutationEvidence(
                new string('c', 64),
                Features: ["RAW_FEATURE"]));
        }
    }

    private static GuildSnapshot CreateSnapshot(ulong guildId)
    {
        var everyone = RoleSnapshot.Create(
            guildId,
            guildId,
            "@everyone",
            0,
            isManaged: false,
            isEveryone: true,
            ResourceProtection.None,
            ["ViewChannel"],
            colorHex: null,
            isHoisted: false,
            isMentionable: false);
        var role = RoleSnapshot.Create(
            guildId,
            10,
            "role",
            1,
            isManaged: false,
            isEveryone: false,
            ResourceProtection.None,
            ["ViewChannel"],
            colorHex: null,
            isHoisted: false,
            isMentionable: false);

        return GuildSnapshot.Create(
            guildId,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            [],
            [role, everyone],
            [CategorySnapshot.Create(guildId, 20, "category", 0, [])],
            [ChannelSnapshot.Create(guildId, 21, "channel", "text", 0, 20, null, false, 0, [])]);
    }

    private static GuildSnapshot CreateSnapshotWithMissingThreadParent()
    {
        var baseSnapshot = CreateSnapshot(GuildId);
        var thread = ChannelSnapshot.Create(
            GuildId,
            22,
            "thread",
            "thread",
            1,
            null,
            null,
            false,
            0,
            [],
            parentChannelId: 999);

        return GuildSnapshot.Create(
            GuildId,
            baseSnapshot.Name,
            ownerId: 1,
            baseSnapshot.Description,
            baseSnapshot.CreatedAt,
            baseSnapshot.Features,
            baseSnapshot.Roles,
            baseSnapshot.Categories,
            baseSnapshot.Channels.Append(thread));
    }
}
