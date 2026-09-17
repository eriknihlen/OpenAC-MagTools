using AcDream.Plugin.Abstractions;
using OpenAC.MagTools;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Commands;

// HIGH-2 (P12 review, defect 14a): a server-rejected /mt vendor buy/sell
// arrives asynchronously as IVendorAutomation.TransactionCompleted with
// Success == false; before this class, only AutoBuySell subscribed to that
// event at all, and only while its own automation round was active, so a
// manually-driven buy/sell that the server rejected reported nothing.
public sealed class VendorTransactionReporterTests
{
    private static (FakeHost Host, VendorTransactionReporter Reporter) Build()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var reporter = new VendorTransactionReporter(host, chat);
        reporter.Start();
        return (host, reporter);
    }

    [Fact]
    public void ReportsAFailedBuy()
    {
        (FakeHost host, _) = Build();

        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Buy, success: false);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Vendor buy failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAFailedSell()
    {
        (FakeHost host, _) = Build();

        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Sell, success: false);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Vendor sell failed", StringComparison.Ordinal));
    }

    [Fact]
    public void StaysSilentOnSuccess()
    {
        (FakeHost host, _) = Build();

        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Buy, success: true);
        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Sell, success: true);

        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void StopsReportingAfterStop()
    {
        (FakeHost host, VendorTransactionReporter reporter) = Build();
        reporter.Stop();

        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Buy, success: false);

        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void ReportsIndependentlyOfAutoBuySellsOwnEnabledSetting()
    {
        // Unlike AutoBuySell (which only observes TransactionCompleted
        // while its own automation round is active), this reporter must
        // fire for a MANUALLY-driven /mt vendor buy/sell too -- simulated
        // here simply by never touching AutoBuySell/its settings at all.
        (FakeHost host, _) = Build();

        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Sell, success: false);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Vendor sell failed", StringComparison.Ordinal));
    }
}
