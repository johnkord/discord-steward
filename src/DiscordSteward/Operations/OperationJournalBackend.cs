namespace DiscordSteward.Operations;

public enum OperationJournalBackend
{
    Sqlite,
    File
}

public static class OperationJournalBackendParser
{
    public static OperationJournalBackend Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return OperationJournalBackend.Sqlite;
        }

        if (string.Equals(value.Trim(), "File", StringComparison.OrdinalIgnoreCase))
        {
            return OperationJournalBackend.File;
        }

        throw new InvalidOperationException(
            $"Unsupported Steward:JournalBackend '{value}'. Expected 'Sqlite' or 'File'.");
    }
}