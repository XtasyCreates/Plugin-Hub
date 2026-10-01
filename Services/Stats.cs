using System.Globalization;

namespace PluginHub.Services;

/// <summary>
/// The rows behind the Statistik tab: what was sold, the trials and what became of them, and the
/// code requests from the mailbox. Only what the charts need leaves the hub - month, plug-in, way
/// of sale, amount - never names, addresses or device ids. The page adds the rows up for the
/// period and plug-in chosen, so a filter never needs another request.
/// </summary>
public sealed class Stats
{
    private readonly Status _status;
    private readonly Inbox _inbox;
    private readonly Settings _settings;
    private readonly Config _config;

    public Stats(Status status, Inbox inbox, Settings settings, Config config)
    {
        _status   = status;
        _inbox    = inbox;
        _settings = settings;
        _config   = config;
    }

    public StatsView View()
    {
        var shared = _settings.Shared;
        var views  = _status.RegisterView().Licenses;
        var byId   = views.ToDictionary(v => v.License.Id);

        License? Previous(License l) => l.Replaces != null && byId.TryGetValue(l.Replaces, out var p) ? p.License : null;
        License? Next(License l) => l.ReplacedBy != null && byId.TryGetValue(l.ReplacedBy, out var n) ? n.License : null;

        // A sale: a full licence that is new, or the one a trial turned into. A full code that only
        // replaces another full code (same device, new key or end) sells nothing new.
        var sales = views.Select(v => v.License)
            .Where(l => !l.Trial && Previous(l) is not { Trial: false })
            .ToList();
        var saleRows = sales.Select(l => new SaleRow(Month(l.Issued), l.Product, Channel(l), l.Price, l.Paid)).ToList();

        // A trial: where a device's run of trial codes starts - its extensions belong to it
        var trialRows = new List<TrialRow>();
        foreach (var start in views.Where(v => v.License.Trial && Previous(v.License) is not { Trial: true }))
        {
            var trial = start.License;
            var last = start;
            int trialCodes = 1;
            var seen = new HashSet<string> { trial.Id };
            while (Next(last.License) is { } next && seen.Add(next.Id))
            {
                last = byId[next.Id];
                if (next.Trial) trialCodes++;
            }

            // Bought: the run ends in a full licence, or the same customer or device bought this
            // plug-in afterwards
            var bought = sales
                .Where(s => s.Product == trial.Product && s.Issued >= trial.Issued
                         && (s.MachineId == trial.MachineId || (trial.CustomerId != null && s.CustomerId == trial.CustomerId)))
                .OrderBy(s => s.Issued)
                .FirstOrDefault();
            bool running = last.License.Trial && last.Status.State is "valid" or "ending";

            trialRows.Add(new TrialRow(Month(trial.Issued), trial.Product, Channel(trial), running, bought != null,
                bought == null ? null : Math.Max(0, (int)(bought.Issued - trial.Issued).TotalDays), trialCodes > 1));
        }

        var requestRows = _inbox.State().Requests
            .Select(r => new RequestRow(Month(r.Received), r.Product, r.Wants, r.Status == "neu"))
            .ToList();

        var plugins = _config.Current.Projects.Where(p => p.IsPlugin).Select(p => new StatsProduct(p.Id, p.Name)).ToList();
        return new StatsView(shared.SalesChannel, shared.SiteStats, Month(DateTimeOffset.Now), plugins,
            saleRows, trialRows, requestRows);
    }

    private static string Month(DateTimeOffset when) =>
        when.ToLocalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Channel(License l) => l.Channel == "online" ? "online" : "mail";
}

/// <summary>
/// Channel: the Verkaufsweg set now. SiteStats: the source of the website's visit numbers ("off").
/// Now: this month (yyyy-MM), so the page counts its periods back from the hub's calendar.
/// </summary>
public sealed record StatsView(string Channel, string SiteStats, string Now, List<StatsProduct> Products,
    List<SaleRow> Sales, List<TrialRow> Trials, List<RequestRow> Requests);

public sealed record StatsProduct(string Id, string Name);

/// <summary>One sale: month, plug-in, way of sale (mail | online), price (null when none was given), paid.</summary>
public sealed record SaleRow(string Month, string Product, string Channel, decimal? Price, bool Paid);

/// <summary>One device's trial: still running, bought afterwards and after how many days, extended.</summary>
public sealed record TrialRow(string Month, string Product, string Channel, bool Running, bool Bought, int? DaysToBuy, bool Extended);

/// <summary>One code request from the mailbox: wants trial | full | null (nothing ticked), still open.</summary>
public sealed record RequestRow(string Month, string? Product, string? Wants, bool Open);
