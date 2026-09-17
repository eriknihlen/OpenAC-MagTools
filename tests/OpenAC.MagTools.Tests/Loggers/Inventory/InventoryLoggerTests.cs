using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Loggers.Inventory;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Loggers.Inventory;

public sealed class InventoryLoggerTests
{
    [Theory]
    [InlineData(PluginObjectClass.Armor, "", true)]
    [InlineData(PluginObjectClass.Clothing, "", true)]
    [InlineData(PluginObjectClass.MeleeWeapon, "", true)]
    [InlineData(PluginObjectClass.MissileWeapon, "", true)]
    [InlineData(PluginObjectClass.WandStaffOrb, "", true)]
    [InlineData(PluginObjectClass.Jewelry, "", true)]
    [InlineData(PluginObjectClass.Gem, "Coalesced Aetheria", true)]
    [InlineData(PluginObjectClass.Gem, "Some Other Gem", false)]
    [InlineData(PluginObjectClass.Misc, "Acid Phyntos Wasp Essence", true)]
    [InlineData(PluginObjectClass.Misc, "A Random Misc Item", false)]
    [InlineData(PluginObjectClass.Food, "", false)]
    public void ObjectClassNeedsIdentMatchesTheOriginalRuleSet(PluginObjectClass objectClass, string name, bool expected)
        => Assert.Equal(expected, InventoryLogger.ObjectClassNeedsIdent(objectClass, name));

    private static (FakeHost Host, ChatOutput Chat, InventoryManagementSettings Settings, TickScheduler Scheduler) Make()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settingsFile = new SettingsFile(host.Storage);
        var settings = new InventoryManagementSettings(settingsFile);
        settings.InventoryLogger.Value = true;
        host.Automation.Character.ObjectId = 500u;
        // MEDIUM-A (P10 re-review): OnSnapshotPoll's HIGH-1 refresh now
        // gates on IsInWorld -- an ordinary active session is in-world by
        // default; a test exercising the logoff-adjacent guard sets this
        // to false explicitly.
        host.Automation.Character.IsInWorld = true;
        var scheduler = new TickScheduler(host.Events, chat);
        return (host, chat, settings, scheduler);
    }

    /// <summary>
    /// HIGH-3 (P10 review): the startup/snapshot poll now runs at 1 Hz
    /// through <see cref="TickScheduler"/> instead of every raw
    /// <see cref="IEvents.Tick"/>. Tests that need to drive it forward call
    /// this instead of a single <c>RaiseTick</c>.
    /// </summary>
    private static void AdvanceOneSecond(FakeHost host) => host.Events.RaiseTick(1.0);

    [Fact]
    public void FirstLoginWithNoFileRequestsIdsForIdentWorthyItemsAndWaits()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();

        var sword = new PluginInventoryItem(1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u, 1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.MeleeWeapon };
        host.Automation.Items.Owned.Add(sword);
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Contains(host.ChatLines, line => line.Contains("Requesting id information", StringComparison.Ordinal));
        // Nothing written yet -- still waiting for id data.
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void OnceEveryTrackedItemHasIdDataItDumpsAndPrintsCompletion()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();

        host.Automation.Items.Owned.Add(new PluginInventoryItem(1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u, 1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.MeleeWeapon });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        // Ident data arrives.
        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { HasAppraisalData = true };
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        Assert.Contains(host.ChatLines, line => line.Contains("completed. Log file written.", StringComparison.Ordinal));
        Assert.NotNull(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void ExistingFileDumpsImmediatelyWithoutTheWaitMessage()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        Assert.DoesNotContain(host.ChatLines, line => line.Contains("This will take a few minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptExistingFileReportsAndTreatsAsEmpty()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "not xml <<<");
        // The corrupt-file check runs inside Dump(), which since defect 8's
        // fix only runs once the owned pack is actually populated -- give
        // it one item so Start()'s existing-file branch runs synchronously.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Trade Notes", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Misc,
        });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        Assert.Contains(host.ChatLines, line => line.Contains("Inventory file is corrupt.", StringComparison.Ordinal));
    }

    [Fact]
    public void StopDumpsOnLogoff()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = true });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        logger.Stop();

        Assert.NotNull(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void StopWithNothingEverCapturedDoesNotOverwriteAnExistingFileWithAnEmptyDocument()
    {
        // HIGH-2 (P10 review): Stop() unconditionally dumped
        // _lastOwnedSnapshot even when it was still the empty default
        // (nothing was ever captured this session -- an empty character, or
        // Disable() before the pack streamed in) -- Export([]) then
        // overwrote a perfectly good pre-existing file with an empty
        // <ArrayOfMyWorldObject />.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        const string existing = "<ArrayOfMyWorldObject><MyWorldObject><Id>7</Id></MyWorldObject></ArrayOfMyWorldObject>";
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", existing);
        // Deliberately no owned items at all -- nothing was ever captured.

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);
        logger.Stop();

        Assert.Equal(existing, host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void AnOwnedItemWithNoWorldObjectYetIsStillDumpedNotDropped()
    {
        // Defect #3: an owned item the object table has no full
        // PluginWorldObject for yet (only the lightweight
        // PluginInventoryItem snapshot exists) used to be dropped from the
        // dump entirely instead of appearing with HasIdData=false, like the
        // original always wrote every owned item.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        // A file already exists, so Start() dumps immediately via Dump() --
        // the code path this test targets. (Start()'s SEPARATE "file
        // missing" request loop is a different code path, out of scope
        // here.)
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            7u, 0u, "Unresolved Trinket", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Jewelry,
        });
        // Deliberately no matching entry in host.Automation.Objects.Objects.

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        MyWorldObjectRecord record = Assert.Single(records);
        Assert.Equal(7u, record.Id);
        Assert.False(record.HasIdData);
        Assert.Equal((int)PluginObjectClass.Jewelry, record.ObjectClass);
        // MEDIUM-3: the name is the one piece of real data this fallback can
        // still carry, under PropertyString.Name's key (1) -- same
        // convention Create() uses from properties.Strings.
        Assert.Equal("Unresolved Trinket", record.StringValues[1]);

        // HIGH-A (P9 re-review): on the real host, Objects.TryGet returning
        // false means there is no ClientObject for this id either, so an
        // Identify call here would be a guaranteed no-op -- must NOT be
        // called, and must NOT be recorded in _requestedIds (that would
        // poison OnObjectChanged's dedup guard and permanently block the
        // real request below).
        Assert.DoesNotContain(7u, host.Automation.Objects.IdentifyRequests);

        // Once the ClientObject actually shows up and OnObjectChanged fires
        // for it, the real request DOES happen. This assertion fails on the
        // pre-HIGH-A-fix code, which already "consumed" id 7 in
        // _requestedIds from the unresolved branch above.
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            7u, 0u, "Unresolved Trinket", PluginObjectClass.Jewelry, 0u, 500u, 0u)
        {
            HasAppraisalData = false,
        });
        host.Events.RaiseObjectChanged(7u, PluginObjectChangeKind.Created);

        Assert.Contains(7u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void APreviouslyAppraisedItemMissingFromTheObjectTableKeepsItsIdData()
    {
        // HIGH-1: a thin object-table dump (the item is still owned but this
        // session's object table hasn't resolved it yet -- e.g. right after
        // a reconnect, before the world repopulates) must not overwrite a
        // persisted, previously-appraised record with an empty unresolved
        // stub. The previous record's id data has to survive via Combine.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        var previouslyAppraised = new MyWorldObjectRecord
        {
            HasIdData = true,
            Id = 7u,
            LastIdTime = 12345,
            ObjectClass = (int)PluginObjectClass.Jewelry,
            StringValues = new Dictionary<int, string> { [1] = "Ring of Fortitude" },
            IntValues = new Dictionary<int, int> { [1] = 42 },
        };
        host.Storage.WriteText(
            "ACServer/Acdream.Inventory.xml", InventoryLoggerXml.Export([previouslyAppraised]));

        // Owned this session, but the object table has no full
        // PluginWorldObject for it yet -- deliberately no matching entry in
        // host.Automation.Objects.Objects.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            7u, 0u, "Ring of Fortitude", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Jewelry,
        });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler); // existing file -> dumps immediately

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        MyWorldObjectRecord record = Assert.Single(records);
        Assert.Equal(7u, record.Id);
        Assert.True(record.HasIdData);
        Assert.Equal("Ring of Fortitude", record.StringValues[1]);
        Assert.Equal(42, record.IntValues[1]);

        // Already had id data -- must not re-request.
        Assert.DoesNotContain(7u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void FullLoginSequenceRequestsWaitsAndDumpsEveryOwnedItem()
    {
        // The whole §1 login sequencing the live gate found broken: print
        // the request line, request ids for the ident-worthy classes,
        // wait for ObjectChanged(IdentReceived), then dump a document that
        // includes every owned item -- appraised or not.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });
        // A second owned item that will never get appraisal data this
        // session (e.g. a plain trade good) -- still must show up.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            2u, 0u, "Trade Notes", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Misc,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            2u, 0u, "Trade Notes", PluginObjectClass.Misc, 0u, 500u, 0u) { HasAppraisalData = true });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Requesting id information", StringComparison.Ordinal));
        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));

        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { HasAppraisalData = true };
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("completed. Log file written.", StringComparison.Ordinal));

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Id == 1u);
        Assert.Contains(records, r => r.Id == 2u);
    }

    [Fact]
    public void NewIdentWorthyItemInMyContainerRequestsIdOnce()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        host.Automation.Objects.Objects.Add(new PluginWorldObject(2u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 500u, 0u) { HasAppraisalData = false });
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created); // second delivery must not double-request

        Assert.Equal([2u], host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void AnItemSeenOnTheGroundStillGetsItsIdRequestedOncePickedUp()
    {
        // H7: the container check must run BEFORE _requestedIds records the
        // id, or an object first seen on the ground (not yet in my
        // container) poisons _requestedIds and never gets a real request
        // once it's actually picked up.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        // First seen on the ground: some other container (or none), not me.
        host.Automation.Objects.Objects.Add(new PluginWorldObject(3u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u) { HasAppraisalData = false });
        host.Events.RaiseObjectChanged(3u, PluginObjectChangeKind.Created);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);

        // Now picked up into my container.
        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { ContainerObjectId = 500u };
        host.Events.RaiseObjectChanged(3u, PluginObjectChangeKind.Created);

        Assert.Equal([3u], host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void StartAtSessionReadyBeforeThePackStreamsInWaitsThenDumpsWhenItemsArrive()
    {
        // Defect 8: Start() used to run immediately at SessionReady, when
        // Items.CaptureOwnedItems() is still empty because the pack hasn't
        // streamed in yet. That wrote (or immediately queued a dump toward)
        // an empty <ArrayOfMyWorldObject />. The fix waits for the first
        // Tick on which CaptureOwnedItems() is non-empty before doing
        // anything.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        // No items owned yet -- CaptureOwnedItems() returns empty, as it
        // does immediately after SessionReady on a real host.
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        // Nothing should have happened yet: no request message, no ident
        // requests, no dump.
        Assert.DoesNotContain(host.ChatLines, line => line.Contains("Requesting id information", StringComparison.Ordinal));
        Assert.Empty(host.Automation.Objects.IdentifyRequests);
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));

        // A 1-second poll fires with still nothing owned -- still must wait.
        AdvanceOneSecond(host);
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));

        // The pack streams in.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = true });

        // Next poll after items appear: CaptureOwnedItems() is now
        // non-empty. Since no file exists, this enters the fresh-file
        // branch and prints the "Requesting id information..." message. The
        // sword already has appraisal data, so nothing is queued to wait on
        // -- the logger dumps immediately with the real item, not an empty
        // one.
        AdvanceOneSecond(host);

        Assert.Contains(host.ChatLines, line => line.Contains("Requesting id information", StringComparison.Ordinal));

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        MyWorldObjectRecord record = Assert.Single(records);
        Assert.Equal(1u, record.Id);
    }

    [Fact]
    public void StopAfterTheHostHasTornDownTheObjectsKeepsTheLastRealSnapshot()
    {
        // Defect 8: Stop() used to re-capture CaptureOwnedItems() fresh at
        // logoff teardown, when the host has already released the owned
        // objects -- producing an empty document even though real items
        // were dumped earlier in the session. The fix dumps from the last
        // known-good (non-empty) snapshot instead.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = true });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);
        // The pack is already populated at Start() time, so the initial
        // dump ran synchronously; no poll needed to produce it.

        string? beforeTeardown = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(beforeTeardown);
        Assert.True(InventoryLoggerXml.TryImport(beforeTeardown!, out List<MyWorldObjectRecord> before));
        Assert.Single(before);

        // The host tears the objects down for logoff before Stop() runs.
        host.Automation.Items.Owned.Clear();
        host.Automation.Objects.Objects.Clear();

        logger.Stop();

        string? afterTeardown = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(afterTeardown);
        Assert.True(InventoryLoggerXml.TryImport(afterTeardown!, out List<MyWorldObjectRecord> after));
        MyWorldObjectRecord record = Assert.Single(after);
        Assert.Equal(1u, record.Id);
    }

    [Fact]
    public void AppraisalDataThatArrivesWithoutAnIdentReceivedEventStillSurvivesLogoffTeardown()
    {
        // Defect 8b (live-gate round 4): the host can merge real appraisal
        // properties into the object table (CaptureOwnedItems/TryGet sees
        // HasAppraisalData=true) WITHOUT ever raising the plugin's
        // IdentReceived ObjectChanged event for that object -- observed
        // live wherever RuntimeInteractionTransactionState's single
        // awaiting-appraisal slot gets displaced by a later request before
        // the earlier one's response comes back, which a startup pass over
        // many ident-worthy items does routinely. Before the fix, the only
        // dump that ever wrote real id data to Storage was gated on
        // OnObjectChanged's "every item now has id data" completion latch
        // -- which never fires here because that event never arrives -- so
        // Stop()'s post-teardown dump (TryGet failing for a torn-down
        // object table) fell back to the empty "unresolved" shape and
        // wrote HasIdData=false even though the item was, in fact,
        // identified this session.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        // Nothing dumped yet -- still waiting.
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));

        // The server's appraisal response lands and the host merges the
        // properties into the object table, but -- unlike every other test
        // in this file -- NO IdentReceived ObjectChanged event is raised
        // for it, matching the live displaced-slot behaviour.
        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { HasAppraisalData = true };

        // The post-startup snapshot poll (1 Hz) is the only other place
        // this class looks at the object table.
        AdvanceOneSecond(host);

        // The host tears the owned objects down for logoff before Stop()
        // runs, exactly like the existing teardown test above.
        host.Automation.Items.Owned.Clear();
        host.Automation.Objects.Objects.Clear();

        logger.Stop();

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        MyWorldObjectRecord record = Assert.Single(records);
        Assert.Equal(1u, record.Id);
        Assert.True(record.HasIdData);
    }

    [Fact]
    public void ItemsAcquiredAfterTheStartupDumpAppearInTheStopDump()
    {
        // HIGH-1 (P10 review): _lastOwnedSnapshot was refreshed only at
        // startup and in the id-wait completion branch, so Stop()'s logoff
        // dump wrote the LOGIN-TIME inventory -- anything looted, bought,
        // or tinkered mid-session was absent even though it was real,
        // currently-owned, in-object-table data at the moment of Stop().
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = true });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        // A second item is looted well into the session, still resolved in
        // the object table when Stop() eventually runs (unlike the
        // already-covered "host tore the objects down" case).
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            2u, 0u, "Looted Ring", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Jewelry,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            2u, 0u, "Looted Ring", PluginObjectClass.Jewelry, 0u, 500u, 0u) { HasAppraisalData = true });

        // The post-startup snapshot poll (HIGH-3) runs at 1 Hz and is what
        // picks up the mid-session loot (HIGH-1) -- advance it before Stop.
        AdvanceOneSecond(host);

        logger.Stop();

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        Assert.Contains(records, r => r.Id == 1u);
        Assert.Contains(records, r => r.Id == 2u);
    }

    [Fact]
    public void AnItemEquippedMidSessionIsIdRequested()
    {
        // MEDIUM-5 (P10 review): OnObjectChanged's per-item identify path
        // returned early unless ContainerObjectId == Character.ObjectId.
        // On this host an equipped item's ContainerObjectId is whatever
        // container it was equipped FROM (or 0), never the player -- the
        // wielding entity's id lives in WielderObjectId instead (the same
        // host-shape fact behind defect 9's IsEquippedByMe fix) -- so an
        // item equipped mid-session was never id-requested at all.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        // Host-shaped equip: ContainerObjectId is 0 (equipped from nothing/
        // an old container), WielderObjectId is the player.
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            4u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 500u) { HasAppraisalData = false });
        host.Events.RaiseObjectChanged(4u, PluginObjectChangeKind.Created);

        Assert.Contains(4u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void AnUnidentifiedItemThatArrivesDuringTheStartupWaitIsStillIdRequested()
    {
        // LOW-9 (P10 review): while _waitingForIdData, OnObjectChanged
        // returned before the per-item Identify path ran at all -- so an
        // item that arrived DURING the wait (e.g. looted mid-startup) was
        // never in the original request loop and nothing else would ever
        // request its id. The wait could then never complete: it re-checks
        // "are all currently-owned ident-worthy items identified", which
        // now includes the never-requested new item.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        var sword = new PluginInventoryItem(1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.MeleeWeapon };
        host.Automation.Items.Owned.Add(sword);
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler); // no file -> requests the sword's id, waits

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);

        // A second item arrives mid-wait, never previously requested.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            2u, 0u, "Looted Ring", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Jewelry,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            2u, 0u, "Looted Ring", PluginObjectClass.Jewelry, 0u, 500u, 0u) { HasAppraisalData = false });
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);

        Assert.Contains(2u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void StartupWaitGivesUpAfterTheBoundedPollBudgetAndStopWritesNothing()
    {
        // HIGH-3 (P10 review): the startup wait now polls through the
        // plugin's one TickScheduler clock at 1 Hz, bounded at 60 polls
        // (~60s), instead of an unbounded raw Events.Tick subscription.
        // HIGH-2's "nothing was ever captured -> Stop writes nothing" must
        // hold for this give-up path too.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        const string existing = "<ArrayOfMyWorldObject><MyWorldObject><Id>9</Id></MyWorldObject></ArrayOfMyWorldObject>";
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", existing);
        // No owned items ever -- the pack never streams in this session.

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler);

        for (int i = 0; i < 60; i++)
            AdvanceOneSecond(host);

        Assert.Contains(
            ((RecordingLogger)host.Log).Messages,
            message => message.Contains("owned pack never populated", StringComparison.Ordinal));

        // MEDIUM-B (P10 re-review): the give-up must dispose the poll
        // itself, not just latch a flag. Prove it behaviorally -- the
        // ORIGINAL weak asserts here (re-checking the warning count, which
        // is already latched by _startupCaptureDone even without disposing
        // the poll) could never fail. This can: if the poll were still
        // running, a pack that streams in AFTER give-up would still run
        // RunStartupCapture and issue an id request / start a dump.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            9u, 0u, "Late Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            9u, 0u, "Late Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });
        AdvanceOneSecond(host);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);
        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains("Requesting id information", StringComparison.Ordinal));

        logger.Stop();

        // Nothing was ever captured before give-up, and the post-give-up
        // arrival must not have been picked up either -- the pre-existing
        // file must survive completely untouched (HIGH-2).
        Assert.Equal(existing, host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void APollThatLandsAfterTheSessionLeftTheWorldDoesNotRefreshTheSnapshot()
    {
        // MEDIUM-A (P10 re-review): host ordering (checked under
        // OpenAcRoot) shows the actual entity/inventory teardown is
        // ATOMIC -- RuntimeGenerationReset.Reset's Drain loop
        // (src/AcDream.Runtime/RuntimeGenerationReset.cs:259) never
        // yields mid-stage, so a poll can never observe a genuinely
        // PARTIAL owned set. What it CAN observe is the narrow gap where
        // IsInWorld has already flipped false (both the character-logoff
        // path, LiveSessionController.cs:1219, and the full-quit path,
        // LiveSessionController.cs:1751, set it synchronously) before this
        // plugin's own Logoff handler (Stop(), which disposes the poll)
        // has run. A poll landing in that gap must not treat whatever
        // CaptureOwnedItems() still returns as a fresh mid-session update.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = true });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler); // synchronous startup capture: {Sword}

        // The session leaves the world (IsInWorld flips false), but a
        // poll still lands before this plugin's own Stop() runs -- and
        // CaptureOwnedItems() still happens to return SOMETHING different
        // from the last good snapshot (e.g. a stale/partial read).
        host.Automation.Character.IsInWorld = false;
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            2u, 0u, "Should Not Appear", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Jewelry,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            2u, 0u, "Should Not Appear", PluginObjectClass.Jewelry, 0u, 500u, 0u) { HasAppraisalData = true });

        AdvanceOneSecond(host); // the poll fires while not in-world

        logger.Stop();

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        Assert.Contains(records, r => r.Id == 1u);
        Assert.DoesNotContain(records, r => r.Id == 2u);
    }

    [Fact]
    public void AStartupIdentifyRequestIsNotDuplicatedByALaterCreatedEventForTheSameItem()
    {
        // LOW-D (P10 re-review): RunStartupCapture's identify loop called
        // Objects.Identify without recording the id in _requestedIds, so a
        // later Created/IdentReceived delivery for that SAME
        // still-unappraised startup item fell into OnObjectChanged's
        // per-item path, found it absent from _requestedIds, and issued a
        // second, redundant Identify.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        var sword = new PluginInventoryItem(1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.MeleeWeapon };
        host.Automation.Items.Owned.Add(sword);
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream", scheduler); // no file -> startup loop requests the sword's id

        Assert.Equal([1u], host.Automation.Objects.IdentifyRequests);

        // A benign re-announce for the same, still-unappraised item.
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Created);

        Assert.Equal([1u], host.Automation.Objects.IdentifyRequests);
    }
}
