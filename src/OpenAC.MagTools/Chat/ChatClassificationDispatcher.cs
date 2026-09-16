using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// One shared, filter-independent fan-out over the host's raw
/// <c>IAutomationSurface.Chat.Received</c> feed.
/// </summary>
/// <remarks>
/// Before this existed, <c>ChatLogger</c> and <c>CombatTrackerHost</c> each
/// subscribed to the raw feed directly and independently called
/// <see cref="ChatClassifier.Classify"/> on the same
/// <see cref="PluginChatMessage"/> — every delivered line was classified
/// twice for no behavioral difference (classification is a pure function of
/// the message and the automation surface). This dispatcher subscribes once,
/// classifies once, and hands the same message plus its one
/// <see cref="ChatClassifier.ChatLine"/> to every subscriber — the chat
/// logger, the combat tracker host, and any future filter-independent
/// consumer (a mana-recharge listener, say). See docs/deviations.md.
/// </remarks>
public sealed class ChatClassificationDispatcher
{
    private readonly IPluginHost _host;
    private Action<PluginChatMessage>? _onReceived;
    private bool _running;

    public ChatClassificationDispatcher(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>
    /// Raised once per delivered chat line, after this dispatcher's single
    /// <see cref="ChatClassifier.Classify"/> call — never once per subscriber.
    /// </summary>
    public event Action<PluginChatMessage, ChatClassifier.ChatLine>? Classified;

    /// <summary>Begins fanning out for the session that just started.</summary>
    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _onReceived = OnReceived;
        _host.Automation.Chat.Received += _onReceived;
    }

    /// <summary>Ends fanning out for the session that just ended.</summary>
    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        if (_onReceived is not null)
            _host.Automation.Chat.Received -= _onReceived;
        _onReceived = null;
    }

    private void OnReceived(PluginChatMessage message)
    {
        ChatClassifier.ChatLine line = ChatClassifier.Classify(message, _host.Automation);
        Classified?.Invoke(message, line);
    }
}
