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

        // LOW-D (P13 review): RuntimeVendorAutomation's own Notice already
        // reads "Vendor transaction failed." or "Vendor transaction failed
        // (weenie error N)." -- prefixing that with "Vendor buy failed: "
        // produced a doubled, redundant line ("Vendor buy failed: Vendor
        // transaction failed (weenie error 48)."). Print the host's own
        // notice as-is when it already carries that generic prefix; fall
        // back to this class's own "<action> failed[: notice]." shape only
        // for a notice that doesn't (or none at all).
        string? notice = result.Notice;
        if (!string.IsNullOrWhiteSpace(notice)
            && notice.StartsWith("Vendor transaction failed", StringComparison.Ordinal))
        {
            _chat.Write(notice);
            return;
        }

        string action = result.Kind == PluginVendorTransactionKind.Buy ? "Vendor buy" : "Vendor sell";
        _chat.Write(action + " failed"
            + (string.IsNullOrWhiteSpace(notice) ? "." : ": " + notice));
    }
}
