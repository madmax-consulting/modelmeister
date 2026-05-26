using System.Reflection;
using Shouldly;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Coverage gate: each Remoting service method must either appear in a Mapper/Snapshot/Applier,
/// or be on the known-out-of-scope allowlist (notifications, planner views, syndication, etc.).
/// </summary>
public class RemotingSurfaceCoverageTests
{
    private static readonly HashSet<string> OutOfScope = new(StringComparer.Ordinal)
    {
        // Singular getters — superseded by GetAll* + in-memory lookups in the snapshot reader.
        "GetEntityType","GetLinkType","GetCategory","GetCVL","GetCVLValuesForCVL","GetFieldSet","GetFieldType","GetAllFieldTypes",
        // Permission concept management — pre-existing in inriver; we manage only role-permission bindings.
        "AddPermission","DeletePermission","GetPermission","UpdatePermission",
        // Per-key/convenience overloads — we use the int-id path.
        "DeleteCVLValue","DeleteRestrictedFieldPermissionByFieldTypeId","DeleteAllFiles",

        // Channels / Print / Connect / Syndication / Plan-and-release surface — not part of model mgmt.
        "GetAllEntityTypeStatistics","ExportModelAsXmlString","ImportModelFromXmlString",
        "DeleteAllEntityTypes","DeleteAllCategories","DeleteAllCVLs","DeleteAllCVLValues",
        "DeleteAllLinkTypes","DeleteAllFieldTypes","DeleteAllFieldSets","DeleteAllFieldViews",
        "DeleteAllCompletenessDefinitions","DeleteAllCompletenessGroups","DeleteAllCompletenessBusinessRule",
        "DeleteAllRoles","DeleteAllPermissions","DeleteAllLanguages","DeleteAllinRiverData",
        "GetAllFieldViews","AddFieldView","UpdateFieldView","GetFieldView","DeleteFieldView",
        "AddFieldTypeToFieldView","DeleteFieldTypeFromFieldView","GetFieldViewsForEntityType",
        "GetFieldTypesForFieldView","GetFieldViewsForFieldType",
        "GetCategoriesForEntityType","GetLinkTypesForEntityType","GetFieldSetsForEntityType",
        "GetFieldTypesForFieldSet","GetFieldSetsForFieldType","GetFieldTypesForEntityTypeAndCategory",
        "GetCVLCount","GetCVLValue","GetCVLValueByKey","GetCVLValueForLanguage","DeleteAllCVLValuesForCVL",
        "GetEnvironmentLatestChanges","GetAllSegments",
        // Completeness apply uses Add/Delete Definition, Add Group, Add BusinessRule,
        // SetCompletenessBusinessRuleSettings and DeleteAllCompletenessGroupsForDefinition (see
        // ChangeApplier). The remaining completeness methods — granular updates, actions, criteria
        // introspection — are not used; the applier regenerates the group/rule tree on update instead.
        "UpdateCompletenessDefinition","GetCompletenessDefinition","ReCalculateCompletenessForDefinition",
        "UpdateCompletenessGroup","GetCompletenessGroup","DeleteCompletenessGroup",
        "GetCompletenessBusinessRulesByGroupAndRule","ConnectExistingCompletenessBusinessRuleToNewGroup",
        "UpdateCompletenessBusinessRuleForGroup","GetAllCompletenessBusinessRules",
        "DeleteCompletenessBusinessRuleForGroup","GetAllCompletenessCriteras","GetCompletenessCriteraByType",
        "UpdatedCompletenessRuleSetting","UpdateCompletenessActions",
        "GetCompletenessActionsByDefinitionIdRuleIdAndTrigger","GetCompletenessActionsByDefinitionIdGroupIdAndTrigger",
        "DeleteCompletenessAction","GetCompletenessAction","AddCompletenessAction","UpdateCompletenessAction",
        // IUtilityService: server settings, files, connectors, notifications, etc.
        "SetServerSetting","GetServerSetting","GetServerSettings","DeleteServerSetting","GetAllServerSettings",
        "DeleteLanguage","AddFile","AddFileFromUrl","DeleteFile","GetFile","GetAllImageConfigurations",
        "ClearImageCache","GetBaseAssetUrlAsync","AddResourceFile","UpdateResourceFile","GetResourceFile",
        "DeleteResourceFile","GetFileMetaData","GetAllConnectors","AddConnector","GetConnector","DeleteConnector",
        "SetConnectorSetting","DeleteConnectorSetting","WriteConnectorEvent","GetConnectorEvents",
        "SetConnectorStarted","GetLatestConnectorEvents","GetAllUIPhrases","GetAllUIPhrasesForLanguage",
        "AddUIPhrase","DeleteUIPhrase","UpdateUIPhrase","GetAllUILanguages","GetSmallIconForEntityType",
        "GetLargeIconForEntityType","GetAllEntityIcons","SendMail","GetPersonalWorkAreaRootFolder",
        "GetPersonalWorkAreaFolder","AddPersonalWorkAreaFolder","DeletePersonalWorkAreaFolder",
        "UpdatePersonalWorkAreaFolderName","MovePersonalWorkAreaFolder","UpdatePersonalWorkAreaQuery",
        "GetAllPersonalWorkAreaFoldersForUser","UpdatePersonalWorkAreaFolderIndex","GetSharedWorkAreaFolder",
        "AddSharedWorkAreaFolder","DeleteSharedWorkAreaFolder","UpdateSharedWorkAreaSyndication",
        "UpdateSharedWorkAreaFolderName","MoveSharedWorkAreaFolder","UpdateSharedWorkAreaQuery",
        "GetAllSharedWorkAreaFolders","UpdateSharedWorkAreaFolderIndex","AddEntitiesToWorkAreaFolder",
        "RemoveEntitiesFromWorkAreaFolder","AddNotification","UpdateNotificaton","GetNotification",
        "DeleteNotification","GetAllNotifications","GetAllNotificationsForUser","GetAllActiveNotifications",
        "AddFileImportMapping","GetFileImportMapping","UpdateFileImportMapping","GetAllFileImportMappings",
        "GetAllFileImportMappingsByType","DeleteFileImportMapping","GetAllImageServiceConfigurations",
        "DeleteImageServiceConfiguration","AddImageServiceConfiguration","UpdateImageServiceConfiguration",
        "GetImageServiceConfiguration","GetAllPlannerViews","GetAllPlannerViewsForUser","GetPlannerView",
        "AddPlannerView","UpdatePlannerView","DeletePlannerView","UpdateCalendarUrlForPlannerView",
        "GetCalendarUrlForPlannerView","GetPlannerViewByCalendarUrl","GetAllConnectorStates",
        "GetAllConnectorStatesForConnector","AddConnectorState","UpdateConnectorState","DeleteConnectorState",
        "DeleteConnectorStates","DeleteAllConnectorStates","DeleteAllHtmlTemplates","GetHtmlTemplate",
        "GetAllHtmlTemplates","GetHtmlTemplatesByTypes","AddHtmlTemplate","UpdateHtmlTemplate","DeleteHtmlTemplate",
        "GetAllSyndications","RunSyndicate","RebuildQuickSearchIndex","GetEnvironmentContextAsync",
        // IUserService: user mgmt, settings — orthogonal to model mgmt.
        "GetUser","GetUserByUsername","GetAllUsers","SetUserSetting","DeleteUserSetting",
        "DeleteAllUserSettings","GetAllUserSettings","GetUserSetting","GetRolesForUser","GetPermissionsForUser",
        "GenerateUserRestApiKey","RemoveRestApiKeyByUsername","UpdateUserSegmentRoles",
        "AddUserToRole","RemoveUserFromRole","GetRole","GetRestrictedFieldPermission",
    };

    [Fact]
    public void Every_model_service_method_is_covered_or_explicitly_excluded()
    {
        var asmPath = typeof(inRiver.Remoting.RemoteManager).Assembly.Location;
        var asm = Assembly.LoadFrom(asmPath);
        var services = new[]
        {
            asm.GetType("inRiver.Remoting.IModelService")!,
            asm.GetType("inRiver.Remoting.IUtilityService")!,
            asm.GetType("inRiver.Remoting.IUserService")!,
        };

        var usedNames = ScanReferencedSymbols(typeof(InriverClient).Assembly);
        var uncovered = new List<string>();
        foreach (var svc in services)
        {
            foreach (var m in svc.GetMethods())
            {
                if (m.IsSpecialName) continue;
                if (OutOfScope.Contains(m.Name)) continue;
                if (!usedNames.Contains(m.Name)) uncovered.Add($"{svc.Name}.{m.Name}");
            }
        }
        uncovered.ShouldBeEmpty(string.Join(", ", uncovered));
    }

    private static HashSet<string> ScanReferencedSymbols(Assembly assembly)
    {
        // Heuristic: scan IL via Mono.Cecil isn't available, so look at metadata + use string match on source.
        var result = new HashSet<string>(StringComparer.Ordinal);
        var srcRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
        if (!Directory.Exists(srcRoot)) return result;
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var name in new[]
            {
                "GetAllEntityTypes","GetEntityType","AddEntityType","UpdateEntityType","DeleteEntityType",
                "GetAllFieldTypes","GetFieldType","AddFieldType","UpdateFieldType","DeleteFieldType",
                "GetAllCVLs","GetCVL","AddCVL","UpdateCVL","DeleteCVL",
                "GetCVLValuesForCVL","AddCVLValue","UpdateCVLValue","DeleteCVLValue","GetAllCVLValues",
                "GetAllCategories","GetCategory","AddCategory","UpdateCategory","DeleteCategory",
                "GetAllLinkTypes","GetLinkType","AddLinkType","UpdateLinkType","DeleteLinkType",
                "GetAllFieldSets","GetFieldSet","AddFieldSet","UpdateFieldSet","DeleteFieldSet",
                "AddFieldTypeToFieldSet","DeleteFieldTypeFromFieldSet",
                "GetAllLanguages","AddLanguage",
                "GetAllRoles","GetRoleByName","AddRole","UpdateRole","DeleteRole",
                "GetAllPermissions","GetPermissionByName","AddPermission","UpdatePermission","DeletePermission",
                "AddPermissionToRole","RemovePermissionFromRole",
                "AddRestrictedFieldPermission","DeleteRestrictedFieldPermission","DeleteRestrictedFieldPermissionByFieldTypeId","GetAllRestrictedFieldPermissions",
                "GetAllCompletenessDefinitions","GetCompletenessGroupForDefinition","GetCompletenessBusinessRulesForGroup","GetAllCompletenessRuleSettingsForBusinessRule",
                "AddCompletenessDefinition","DeleteCompletenessDefinition","DeleteAllCompletenessGroupsForDefinition",
                "AddCompletenessGroup","AddCompletenessBusinessRule","SetCompletenessBusinessRuleSettings",
            })
            {
                if (text.Contains("." + name + "(", StringComparison.Ordinal)) result.Add(name);
            }
        }
        return result;
    }
}
