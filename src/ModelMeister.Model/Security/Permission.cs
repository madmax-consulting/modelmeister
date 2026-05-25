using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Security;

/// <summary>
/// Base class for an inriver permission. The CLR type name becomes the default
/// <see cref="Name"/> and <see cref="Description"/>. See <see cref="StandardPermissions"/> for
/// the platform-shipped set.
/// </summary>
public abstract class Permission
{
    protected Permission()
    {
        Name = NameHumanizer.Humanize(GetType().Name);
        Description = Name;
    }

    public string Name { get; init; }
    public string Description { get; init; }
}
