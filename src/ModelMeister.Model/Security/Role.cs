using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Security;

/// <summary>
/// Base class for an inriver Role. Override <see cref="Permissions"/> with the CLR types of any
/// <see cref="Permission"/> subclasses that should be granted.
/// </summary>
public abstract class Role
{
    protected Role()
    {
        Name = NameHumanizer.Humanize(GetType().Name);
        Description = Name;
    }

    public string Name { get; init; }
    public string Description { get; init; }

    /// <summary>Permission CLR types granted to the role.</summary>
    public virtual IReadOnlyList<Type> Permissions => [];
}
