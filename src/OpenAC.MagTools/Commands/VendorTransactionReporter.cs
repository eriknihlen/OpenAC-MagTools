using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Commands;

/// <summary>
/// HIGH-2 (P12 review, defect 14a): <see cref="MtCommandRouter"/>'s
/// <c>ReportVendor</c> only covers the IMMEDIATE
/// <see cref="PluginVendorCommandResult"/> a <c>/mt vendor buy</c>/<c>sell</c>
/// (or <c>addbuy</c>/<c>addsell</c>) call gets back -- Sent vs a host-level
/// refusal (Busy, InvalidItem, ...). A server-rejected transaction arrives
/// LATER, asynchronously, as
/// <see cref="IVendorAutomation.TransactionCompleted"/> with
/// <c>Success == false</c>. Before this class, only <c>AutoBuySell</c>
/// subscribed to that event at all, and only while its own automation round
/// was active (<c>_active</c>) -- a manually-driven <c>/mt vendor buy</c>/
/// <c>sell</c> that the server rejected reported nothing at all.
/// </summary>
/// <remarks>
/// Subscribes unconditionally for the whole plugin session (independent of
/// <c>AutoBuySell/Enabled</c>) and reports failures only; success stays
/// silent, matching every other command's success behaviour and the
/// original's own silence on a successful trade.
/// </remarks>
public sealed class VendorTransactionReporter
{
    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private Action<PluginVendorTransaction>? _onTransactionCompleted;

    public VendorTransactionReporter(IPluginHost host, ChatOutput chat)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        _host = host;
        _chat = chat;
    }

    public void Start()
    {
        if (_onTransactionCompleted is not null)
            return;
        _onTransactionCompleted = OnTransactionCompleted;
        _host.Automation.Vendor.TransactionCompleted += _onTransactionCompleted;
    }

    public void Stop()
    {
        if (_onTransactionCompleted is not null)
            _host.Automation.Vendor.TransactionCompleted -= _onTransactionCompleted;
        _onTransactionCompleted = null;
    }

    private void OnTransactionCompleted(PluginVendorTransaction result)
    {
        if (result.Success)
            return;

        string action = result.Kind == PluginVendorTransactionKind.Buy ? "Vendor buy" : "Vendor sell";
        _chat.Write(action + " failed"
            + (string.IsNullOrWhiteSpace(result.Notice) ? "." : ": " + result.Notice));
    }
}
