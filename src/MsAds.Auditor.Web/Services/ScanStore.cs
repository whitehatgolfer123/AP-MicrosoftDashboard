using MsAds.Auditor.Core.Analysis;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Holds the most recent portfolio scan so the page survives a refresh without re-querying
/// every account.
/// </summary>
public interface IScanStore
{
    PortfolioScan? Latest { get; }

    void Set(PortfolioScan scan);

    void Clear();
}

/// <summary>
/// In-memory, single slot. A scan is a snapshot rather than a record worth keeping, and holding
/// only the latest keeps client data out of long-lived storage.
/// </summary>
public sealed class InMemoryScanStore : IScanStore
{
    private PortfolioScan? _latest;

    public PortfolioScan? Latest => _latest;

    public void Set(PortfolioScan scan) => _latest = scan;

    public void Clear() => _latest = null;
}
