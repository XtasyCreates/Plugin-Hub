using Kit = RhinoKit.Licensing;

namespace PluginHub.Services;

/// <summary>
/// The suite (decided 17.11.2026): codes name products instead of belonging to one plug-in.
/// Laser Create's key signs them all, one code can unlock several products, and the plug-ins of
/// the suite carry that key as their SuiteKey. What a product is - its id, name, whether it has a
/// free trial - comes from RhinoKit's Products.cs, which the hub compiles, so the hub offers what
/// the plug-ins check.
///
/// Codes from before name no products: they belong to the plug-in in License.Product and were
/// signed with its own key. Everything that shows or counts a code goes through here, so both
/// kinds read the same.
/// </summary>
public static class Suite
{
    /// <summary>The hub key that signs every code with products: Laser Create's.</summary>
    public const string KeyProduct = Kit.Products.LaserCreate;

    /// <summary>Which hub key made this code - a code dies with its key.</summary>
    public static string KeyOf(License l) => l.Products != null ? KeyProduct : l.Product;

    /// <summary>
    /// The products a code covers. An older Site Create code counts as Laser Create: the two are
    /// one product now (it still unlocks only Site Create, as it always did).
    /// </summary>
    public static IReadOnlyList<string> ProductsOf(License l) =>
        (IReadOnlyList<string>?)l.Products ?? new[] { ProductOf(l.Product) };

    /// <summary>The product a plug-in project belongs to: Site Create comes with Laser Create.</summary>
    public static string ProductOf(string projectId) => projectId == "swtolaser" ? Kit.Products.LaserCreate : projectId;

    /// <summary>"Laser Create + AutoDim" for a suite code, the plug-in's name for an older one.</summary>
    public static string Name(License l, HubConfig config) =>
        l.Products != null ? Kit.Products.Names(l.Products) : ProjectName(l.Product, config);

    /// <summary>The name for what License.Product holds: a product, several ("autodim+laser") or a project.</summary>
    public static string Name(string? id, HubConfig config)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (id.Contains('+')) return Kit.Products.Names(id.Split('+'));
        return Kit.Products.IsKnown(id) ? Kit.Products.Name(id) : ProjectName(id, config);
    }

    /// <summary>What License.Product holds for a suite code: its products, joined with '+'.</summary>
    public static string OfferId(IReadOnlyList<string> products) => string.Join("+", products);

    /// <summary>
    /// The plug-in project whose commands a mail about this code names and whose settings
    /// (trial length, follow-up) it follows: the first product that is a project here, else
    /// Laser Create.
    /// </summary>
    public static string ProjectOf(License l, HubConfig config)
    {
        if (l.Products == null) return l.Product;
        return l.Products.FirstOrDefault(id => config.Projects.Any(p => p.Id == id && p.IsPlugin)) ?? KeyProduct;
    }

    /// <summary>
    /// The products as the code form sends them, checked and in the code's order. "suite"
    /// stands alone. A trial may only name products that have one - ArchLibrary has none.
    /// </summary>
    public static List<string> Parse(IEnumerable<string>? input, bool trial)
    {
        var ids = (input ?? Array.Empty<string>()).Select(p => (p ?? "").Trim().ToLowerInvariant())
            .Where(p => p.Length > 0).Distinct().ToList();
        if (ids.Count == 0) throw new HubError("Wähl mindestens ein Produkt.");

        var unknown = ids.Where(id => !Kit.Products.IsKnown(id)).ToList();
        if (unknown.Count > 0) throw new HubError($"Unbekanntes Produkt: {string.Join(", ", unknown)}.");
        if (ids.Contains(Kit.Products.Suite)) ids = new List<string> { Kit.Products.Suite };

        if (trial)
        {
            var without = ids.Select(id => Kit.Products.All.First(p => p.Id == id)).Where(p => !p.Trial).ToList();
            if (without.Count > 0)
                throw new HubError($"{string.Join(", ", without.Select(p => p.Name))} hat keine Testphase – " +
                                   "sonst hätte der Kunde danach schon alle Objekte der Bibliothek.");
        }
        return Kit.ActivationCode.ProductText(ids).Split(',').ToList();
    }

    /// <summary>
    /// The plug-in projects a code with these products should work in - Laser Create's two for
    /// "laser", every plug-in for "suite" - and the products whose plug-in is not a project with
    /// activation here yet (ArchLibrary before it gets one).
    /// </summary>
    public static (List<ProjectDef> Projects, List<string> NotYet) Targets(IReadOnlyList<string> products, HubConfig config)
    {
        var plugins = config.Projects.Where(p => p.IsPlugin && p.KeyFile != null).ToList();
        if (products.Contains(Kit.Products.Suite)) return (plugins, new List<string>());

        var projects = new List<ProjectDef>();
        var notYet = new List<string>();
        foreach (string id in products)
        {
            var mine = plugins.Where(p => ProductOf(p.Id) == id).ToList();
            if (mine.Count == 0) notYet.Add(Kit.Products.Name(id));
            projects.AddRange(mine);
        }
        return (projects.Distinct().ToList(), notYet);
    }

    private static string ProjectName(string id, HubConfig config) =>
        config.Projects.FirstOrDefault(p => p.Id == id)?.Name ?? id;
}
