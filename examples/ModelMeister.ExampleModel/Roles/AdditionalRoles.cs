using ModelMeister.Model.Security;

namespace ModelMeister.ExampleModel.Roles;

public sealed class Translator : Role
{
    public override IReadOnlyList<Type> Permissions => new[]
    {
        typeof(StandardPermissions.View),
        typeof(StandardPermissions.UpdateEntity),
        typeof(StandardPermissions.AddComments),
        typeof(StandardPermissions.DeleteComments),
    };
}

public sealed class LegalReviewer : Role
{
    public override IReadOnlyList<Type> Permissions => new[]
    {
        typeof(StandardPermissions.View),
        typeof(StandardPermissions.LockEntity),
        typeof(StandardPermissions.AddComments),
        typeof(StandardPermissions.ManageLinkRules),
        typeof(StandardPermissions.AdministerSpecificationTemplates),
    };
}

/// <summary>
/// Touches every remaining standard permission so the role round-trips exercise the full set
/// (also handy as a smoke test against the live permission catalogue on apply).
/// </summary>
public sealed class Admin : Role
{
    public override IReadOnlyList<Type> Permissions => new[]
    {
        typeof(StandardPermissions.View),
        typeof(StandardPermissions.AddEntity),
        typeof(StandardPermissions.UpdateEntity),
        typeof(StandardPermissions.DeleteEntity),
        typeof(StandardPermissions.AddLink),
        typeof(StandardPermissions.UpdateLink),
        typeof(StandardPermissions.DeleteLink),
        typeof(StandardPermissions.UpdateCVL),
        typeof(StandardPermissions.LockEntity),
        typeof(StandardPermissions.AddFile),
        typeof(StandardPermissions.AddComments),
        typeof(StandardPermissions.DeleteComments),
        typeof(StandardPermissions.PublishChannel),
        typeof(StandardPermissions.ManageLinkRules),
        typeof(StandardPermissions.ContentStore),
        typeof(StandardPermissions.CopyEntity),
        typeof(StandardPermissions.InRiverPlanAndRelease),
        typeof(StandardPermissions.InRiverEnrich),
        typeof(StandardPermissions.InRiverSupply),
        typeof(StandardPermissions.InRiverPublish),
        typeof(StandardPermissions.InRiverPrint),
        typeof(StandardPermissions.InRiverCampaignPlanner),
        typeof(StandardPermissions.Syndicate),
        typeof(StandardPermissions.SupplierOnboarding),
        typeof(StandardPermissions.SharePlannerViews),
        typeof(StandardPermissions.AdministerSpecificationTemplates),
        typeof(StandardPermissions.ChangeEntitySegment),
        typeof(StandardPermissions.ChangeFieldSet),
        typeof(StandardPermissions.ImportEntitySettings),
    };
}
