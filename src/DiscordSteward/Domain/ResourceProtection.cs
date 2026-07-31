using System.Collections.Immutable;

namespace DiscordSteward.Domain;

public sealed record ResourceProtection(bool IsProtected, ImmutableArray<string> Reasons)
{
    public static ResourceProtection None { get; } = new(false, []);
}
