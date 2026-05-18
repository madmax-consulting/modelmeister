namespace ModelMeister.Model.Security;

/// <summary>
/// Every platform permission supported by inriver Remoting 8.21. To grant one to a role, list the
/// concrete permission Type in <see cref="Role.Permissions"/>. Verify against the installed
/// inRiver.Remoting.iPMC assembly at impl time and add anything that's been introduced since.
/// </summary>
public static class StandardPermissions
{
    public sealed class View : Permission;
    public sealed class AddEntity : Permission;
    public sealed class UpdateEntity : Permission;
    public sealed class DeleteEntity : Permission;
    public sealed class AddLink : Permission;
    public sealed class UpdateLink : Permission;
    public sealed class DeleteLink : Permission;
    public sealed class UpdateCVL : Permission;
    public sealed class LockEntity : Permission;
    public sealed class AddFile : Permission;
    public sealed class AddComments : Permission;
    public sealed class DeleteComments : Permission;
    public sealed class PublishChannel : Permission;
    public sealed class ManageLinkRules : Permission;
    public sealed class ContentStore : Permission;
    public sealed class CopyEntity : Permission;
    public sealed class InRiverPlanAndRelease : Permission;
    public sealed class InRiverEnrich : Permission;
    public sealed class InRiverSupply : Permission;
    public sealed class InRiverPublish : Permission;
    public sealed class InRiverPrint : Permission;
    public sealed class InRiverCampaignPlanner : Permission;
    public sealed class Syndicate : Permission;
    public sealed class SupplierOnboarding : Permission;
    public sealed class SharePlannerViews : Permission;
    public sealed class AdministerSpecificationTemplates : Permission;
    public sealed class ChangeEntitySegment : Permission;
    public sealed class ChangeFieldSet : Permission;
    public sealed class ImportEntitySettings : Permission;
}
