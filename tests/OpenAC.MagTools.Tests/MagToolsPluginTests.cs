using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests;

public sealed class MagToolsPluginTests
{
    [Fact]
    public void EnableRegistersTheMtVerbAndBothPanels()
    {
        var host = new FakeHost { HasUi = true };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        Assert.True(host.Commands.Handlers.ContainsKey("mt"));
        Assert.Equal(2, host.Ui.Panels.Count);
        Assert.Equal(["main", "hud"],
            host.Ui.Panels.Select(panel => panel.Descriptor.WindowId));
        Assert.All(host.Ui.Panels, static panel =>
            Assert.False(panel.Descriptor.StartVisible));
        Assert.All(host.Ui.Panels, static panel =>
            Assert.True(panel.Descriptor.ShowInSidePanel));
        Assert.Equal("MT", host.Ui.Panels[0].Descriptor.IconText);
        Assert.IsType<MainViewModel>(host.Ui.Panels[0].Binding);
        Assert.IsType<HudViewModel>(host.Ui.Panels[1].Binding);
    }

    [Fact]
    public void AHeadlessHostGetsNoPanelsButStillGetsTheVerb()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        Assert.Empty(host.Ui.Panels);
        Assert.True(host.Commands.Handlers.ContainsKey("mt"));
    }

    [Fact]
    public void DisableUnhooksEverythingItRegistered()
    {
        var host = new FakeHost { HasUi = true };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();
        plugin.Disable();

        Assert.Empty(host.Commands.Handlers);
        Assert.Equal(0, host.Events.TickSubscriberCount);
        Assert.Empty(host.Ui.Panels);
    }

    [Fact]
    public void DisableAndReenableRefiresTheLoginBanner()
    {
        // A plugin reload with no underlying reconnect: the host will not
        // raise LoginComplete a second time for a session that never went
        // away, so SessionContext.Subscribe re-fires it itself on Enable
        // when the automation surface is still in-world.
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Automation.IsAvailable = true;
        host.Automation.Character.IsInWorld = true;
        host.Events.RaiseLoginComplete();

        plugin.Disable();
        plugin.Enable();

        Assert.Equal(
            2,
            host.ChatLines.Count(line =>
                line == ChatOutput.Prefix + "Plugin now online."));
    }

    [Fact]
    public void DisableFlushesWhatTheSettingsDebounceStillOwes()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Commands.Handlers["mt"](
            new AcDream.Plugin.Abstractions.PluginCommand(
                "mt", "opt set Filters.AttackEvades true", "/mt opt set Filters.AttackEvades true"));
        Assert.Equal(0, host.Storage.WriteCount);

        plugin.Disable();
        Assert.Equal(1, host.Storage.WriteCount);

        var reloaded = new SettingsManager(new SettingsFile(host.Storage));
        Assert.True(reloaded.Filters.AttackEvades.Value);
    }

    [Fact]
    public void LoginCompleteEventPrintsTheOnlineBanner()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Automation.IsAvailable = true;
        host.Automation.Character.IsInWorld = true;
        host.Events.RaiseLoginComplete();

        Assert.Contains(
            ChatOutput.Prefix + "Plugin now online.",
            host.ChatLines);
    }

    [Fact]
    public void ATickExceptionIsLoggedEveryTimeButPostedToChatOnlyOnce()
    {
        // A recurring exception (broken invariant, not a one-off) must not
        // flood chat every frame; the log gets every occurrence.
        var host = new FakeHost { HasUi = false };
        host.Storage.ThrowOnWrite = true;
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        // Two separate settings writes -> two separate debounced flush
        // attempts, each failing with the same exception shape.
        host.Commands.Handlers["mt"](
            new AcDream.Plugin.Abstractions.PluginCommand(
                "mt", "opt set Filters.AttackEvades true", "/mt opt set Filters.AttackEvades true"));
        host.Events.RaiseTick(1.0d);

        host.Commands.Handlers["mt"](
            new AcDream.Plugin.Abstractions.PluginCommand(
                "mt", "opt set Filters.AttackEvades false", "/mt opt set Filters.AttackEvades false"));
        host.Events.RaiseTick(1.0d);

        Assert.Equal(
            2,
            ((RecordingLogger)host.Log).Messages.Count(message =>
                message.StartsWith("error: Mag-Tools tick failed", StringComparison.Ordinal)));
        Assert.Single(
            host.ChatLines,
            line => line.Contains("Exception caught", StringComparison.Ordinal));
    }

    [Fact]
    public void PastTheDistinctShapeCapATickExceptionIsLoggedButNotPostedToChat()
    {
        // A tick loop throwing many DIFFERENT exception shapes is itself the
        // flood the dedup exists to stop — the 33rd distinct shape must
        // still reach the log (every occurrence, always) but not chat.
        var host = new FakeHost { HasUi = false };
        host.Storage.ThrowOnWrite = true;
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Commands.Handlers["mt"](
            new AcDream.Plugin.Abstractions.PluginCommand(
                "mt", "opt set Filters.AttackEvades true", "/mt opt set Filters.AttackEvades true"));

        for (int index = 0; index < 33; index++)
        {
            host.Storage.ThrowMessage = "Storage write failed (test fault) #" + index;
            host.Events.RaiseTick(1.0d);
        }

        Assert.Equal(
            33,
            ((RecordingLogger)host.Log).Messages.Count(message =>
                message.StartsWith("error: Mag-Tools tick failed", StringComparison.Ordinal)));
        Assert.Equal(
            32,
            host.ChatLines.Count(line => line.Contains("Exception caught", StringComparison.Ordinal)));
    }

    [Fact]
    public void EnableRegistersThePackAndHealHotkeysWithTheirDefaultChords()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        (string Id, string DisplayName, AcDream.Plugin.Abstractions.PluginKeyChord DefaultChord, Action Handler) pack =
            Assert.Single(host.Hotkeys.Registrations, r => r.Id == "pack-inventory");
        Assert.Equal("Pack Inventory", pack.DisplayName);
        Assert.Equal(AcDream.Plugin.Abstractions.PluginKey.P, pack.DefaultChord.Key);
        Assert.True(pack.DefaultChord.Ctrl);

        (string Id, string DisplayName, AcDream.Plugin.Abstractions.PluginKeyChord DefaultChord, Action Handler) heal =
            Assert.Single(host.Hotkeys.Registrations, r => r.Id == "one-touch-heal");
        Assert.Equal("One Touch Heal", heal.DisplayName);
        Assert.Equal(AcDream.Plugin.Abstractions.PluginKey.Unknown, heal.DefaultChord.Key);
    }

    [Fact]
    public void DisableRevokesBothHotkeyRegistrations()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        FakeHotkeyRegistration packHandle = host.Hotkeys.Handles["pack-inventory"];
        FakeHotkeyRegistration healHandle = host.Hotkeys.Handles["one-touch-heal"];
        Assert.False(packHandle.Disposed);
        Assert.False(healHandle.Disposed);

        plugin.Disable();

        Assert.True(packHandle.Disposed);
        Assert.True(healHandle.Disposed);
    }

    [Fact]
    public void PackInventoryHotkeyStartsTheInventoryPackerWhenFired()
    {
        var host = new FakeHost { HasUi = false };
        host.Automation.Character.Name = "Acdream";
        host.LootClassifiers.Available.Add(
            new AcDream.Plugin.Abstractions.PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        host.LootClassifiers.ProfileClassifyHandler = (profile, _)
            => profile == "Default.AutoPack"
                ? new AcDream.Plugin.Abstractions.PluginLootClassification(
                    Matched: false, Action: AcDream.Plugin.Abstractions.PluginLootAction.NoLoot)
                : null;
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();
        host.Events.RaiseLoginComplete();

        host.Hotkeys.Fire("pack-inventory");

        Assert.Contains(host.ChatLines, line => line.Contains("Auto Pack - Started.", StringComparison.Ordinal));
    }

    [Fact]
    public void OneTouchHealHotkeyAppliesAKitWhenFired()
    {
        var host = new FakeHost { HasUi = false };
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 50;
        host.Automation.Character.Skills.Add(new AcDream.Plugin.Abstractions.PluginSkillInfo(
            OpenAC.MagTools.Macros.OneTouchHeal.HealingSkillId,
            "Healing",
            AcDream.Plugin.Abstractions.PluginSkillTraining.Trained,
            300));
        host.Automation.Items.Owned.Add(new AcDream.Plugin.Abstractions.PluginInventoryItem(
            1u, 0u, "Kit", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 1, 1, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = AcDream.Plugin.Abstractions.PluginObjectClass.HealingKit,
        });
        host.Automation.Objects.Objects.Add(new AcDream.Plugin.Abstractions.PluginWorldObject(
            1u, 0u, "Kit", AcDream.Plugin.Abstractions.PluginObjectClass.HealingKit, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
        });
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Hotkeys.Fire("one-touch-heal");

        Assert.Contains(("apply", 1u, 999u), host.Automation.Items.Calls);
    }

    [Fact]
    public void LoginCompleteOpensTheMainPackByDefault()
    {
        var host = new FakeHost { HasUi = true };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();
        host.Events.RaiseLoginComplete();

        Assert.Contains(
            AcDream.Plugin.Abstractions.PluginClientWindow.Inventory,
            host.Ui.ShowClientWindowCalls);
    }

    [Fact]
    public void LoginCompleteDoesNotOpenTheMainPackOnANoWindowHost()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();
        host.Events.RaiseLoginComplete();

        Assert.Empty(host.Ui.ShowClientWindowCalls);
    }

    [Fact]
    public void LoginCompleteBindsTheCharacterAndServerCommandTabsToTheLiveScope()
    {
        var host = new FakeHost { HasUi = true };
        host.Automation.Character.AccountName = "acct";
        host.Automation.Character.WorldName = "Server";
        host.Automation.Character.Name = "Acdream";

        // Pre-seed the character-scope on-login list before the plugin ever
        // initializes, the way a user's saved Mag-Tools.xml would arrive
        // (the plugin's own SettingsFile loads once, at Initialize()).
        var seedFile = new SettingsFile(host.Storage);
        new ScopedCommandStore(seedFile).SetOnLoginCommands(
            SettingsScope.Character("acct", "Server", "Acdream"), ["/mt test"]);
        seedFile.Flush();

        var plugin = new MagToolsPlugin();
        plugin.Initialize(host);
        plugin.Enable();
        host.Events.RaiseLoginComplete();

        var main = (MainViewModel)host.Ui.Panels[0].Binding;
        Assert.Equal(["/mt test"], main.CharacterCommands.LoginCommands);
    }

    [Fact]
    public void LogOutOnDeathIsDisabledByDefault()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();
        host.Events.RaiseLoginComplete();
        host.Events.RaiseLocalPlayerDied("You have died.");

        Assert.Equal(0, host.Automation.Login.LogoutCalls);
    }

    [Fact]
    public void LogOutOnDeathLogsOutWhenEnabled()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        // Flip the live setting through the same route a user would: the
        // plugin's own /mt console.
        host.Commands.Handlers["mt"](
            new AcDream.Plugin.Abstractions.PluginCommand(
                "mt", "opt set Misc.LogOutOnDeath true", "/mt opt set Misc.LogOutOnDeath true"));

        host.Events.RaiseLoginComplete();
        host.Events.RaiseLocalPlayerDied("You have died.");

        Assert.Equal(1, host.Automation.Login.LogoutCalls);
    }
}
