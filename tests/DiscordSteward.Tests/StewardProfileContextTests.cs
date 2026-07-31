using DiscordSteward.Configuration;

namespace DiscordSteward.Tests;

public sealed class StewardProfileContextTests
{
    [Fact]
    public void Create_ExplicitProfileRequiresSafeName()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StewardProfileContext.Create(name: null, isExplicit: true, botTokenSecretName: null));
        Assert.Throws<InvalidOperationException>(() =>
            StewardProfileContext.Create("../other", isExplicit: true, botTokenSecretName: null));

        var profile = StewardProfileContext.Create("primary-observer", isExplicit: true, "Primary.Bot");

        Assert.Equal("primary-observer", profile.Name);
        Assert.Equal("Primary.Bot", profile.BotTokenSecretName);
    }

    [Fact]
    public void ResolveBotToken_PrefersDirectTokenThenNamedSecret()
    {
        Assert.Equal(
            "direct",
            StewardProfileContext.ResolveBotToken("direct", "Primary", _ => "named"));
        Assert.Equal(
            "named",
            StewardProfileContext.ResolveBotToken(null, "Primary", name => name == "Primary" ? "named" : null));
        Assert.Null(StewardProfileContext.ResolveBotToken(null, null, _ => "unused"));
    }
}
