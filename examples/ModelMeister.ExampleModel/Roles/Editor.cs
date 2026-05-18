using ModelMeister.Model.Security;

namespace ModelMeister.ExampleModel.Roles;

public sealed class Editor : Role
{
    public override IReadOnlyList<Type> Permissions => new[]
    {
        typeof(StandardPermissions.View),
        typeof(StandardPermissions.UpdateEntity),
        typeof(StandardPermissions.AddLink),
        typeof(StandardPermissions.UpdateLink),
    };
}

public sealed class Reader : Role
{
    public override IReadOnlyList<Type> Permissions => new[]
    {
        typeof(StandardPermissions.View),
    };
}
