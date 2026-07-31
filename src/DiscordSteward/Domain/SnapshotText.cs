namespace DiscordSteward.Domain;

internal static class SnapshotText
{
    internal const int NameLimit = 256;
    internal const int DescriptionLimit = 1_024;
    internal const int TopicLimit = 1_024;

    internal static string Required(string? value, int maximumLength) =>
        Bound(value ?? string.Empty, maximumLength);

    internal static string? Optional(string? value, int maximumLength) =>
        value is null ? null : Bound(value, maximumLength);

    private static string Bound(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }
}
