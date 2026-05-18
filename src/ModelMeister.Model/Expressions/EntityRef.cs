namespace ModelMeister.Model.Expressions;

/// <summary>
/// Phantom type representing an inriver entity reference returned by FIRSTLINKEDENTITY / LINKEDENTITIES / map-bound loop variables.
/// Plain integer entity IDs are explicitly disallowed by inriver Expression Engine — the DSL enforces this at compile time.
/// </summary>
public sealed record EntityRef
{
    internal EntityRef() { }
}
