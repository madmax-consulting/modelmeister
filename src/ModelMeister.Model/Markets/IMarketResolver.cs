using System.Reflection;

namespace ModelMeister.Model.Markets;

/// <summary>Pluggable strategy for resolving the set of markets used to fan out PerMarket fields.</summary>
public interface IMarketResolver
{
    /// <summary>Returns a map of market keys (used to compose field IDs) to display labels.</summary>
    IReadOnlyDictionary<string, string> GetMarkets();
}

/// <summary>
/// Default resolver. Discovers a concrete <see cref="MarketsCvl"/> subclass in the supplied
/// assembly and emits its CVL values as the market list.
/// </summary>
public sealed class CvlMarketResolver(Assembly modelAssembly) : IMarketResolver
{
    public IReadOnlyDictionary<string, string> GetMarkets()
    {
        var type = modelAssembly.GetTypes()
            .FirstOrDefault(t => !t.IsAbstract && typeof(MarketsCvl).IsAssignableFrom(t));

        if (type is null) return new Dictionary<string, string>(0);

        var cvl = (Cvl)Activator.CreateInstance(type)!;
        return cvl.GetValues()
            .ToDictionary(v => v.Key, v => v.Value.DefaultValue, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Marker base for the project's markets CVL — implement to opt into per-market field fan-out.</summary>
public abstract class MarketsCvl : Cvl;
