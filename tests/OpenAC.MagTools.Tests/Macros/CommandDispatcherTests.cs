using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class CommandDispatcherTests
{
    [Theory]
    [InlineData("/mt test", true, "test")]
    [InlineData("/MT TEST", true, "TEST")]
    [InlineData("/mt", true, "")]
    [InlineData("/mtblah", false, "")]
    [InlineData("hello world", false, "")]
    [InlineData("/mtx foo", false, "")]
    public void TryGetMtArgumentsMatchesOnlyTheMtVerb(string command, bool expectedIsMt, string expectedArguments)
    {
        bool isMt = CommandDispatcher.TryGetMtArguments(command, out string arguments);

        Assert.Equal(expectedIsMt, isMt);
        if (expectedIsMt)
            Assert.Equal(expectedArguments, arguments);
    }

    [Fact]
    public void DispatchRoutesAnMtCommandThroughTheRouterNotChat()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var router = new MtCommandRouter(host, chat, settings);

        CommandDispatcher.Dispatch(host, router, "/mt test");

        Assert.Empty(host.Automation.Chat.Submitted);
    }

    [Fact]
    public void DispatchSubmitsAnythingElseToChat()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var router = new MtCommandRouter(host, chat, settings);

        CommandDispatcher.Dispatch(host, router, "hail Bob");

        Assert.Equal(["hail Bob"], host.Automation.Chat.Submitted);
    }
}
