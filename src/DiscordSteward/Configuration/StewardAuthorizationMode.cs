namespace DiscordSteward.Configuration;

public enum StewardAuthorizationMode
{
    InteractiveApproval,
    UnrestrictedAutonomy
}


public static class StewardAuthorizationModeParser
{
    public static StewardAuthorizationMode Parse(int policySchemaVersion, string? configuredMode)
    {
        if (policySchemaVersion == 3)
        {
            if (!string.IsNullOrWhiteSpace(configuredMode))
            {
                throw new InvalidOperationException(
                    "Policy schema version 3 does not support authorization.mode; migrate the policy to schema version 4.");
            }

            return StewardAuthorizationMode.InteractiveApproval;
        }

        if (policySchemaVersion != 4)
        {
            throw new InvalidOperationException(
                $"Unsupported Steward policy schema version {policySchemaVersion}; expected schema version 3 or 4.");
        }

        if (!Enum.TryParse<StewardAuthorizationMode>(configuredMode, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException(
                "Policy schema version 4 requires authorization.mode to be InteractiveApproval or UnrestrictedAutonomy.");
        }

        return mode;
    }
}
