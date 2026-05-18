using System.Globalization;
using inRiver.Remoting;
using IriverCvl = inRiver.Remoting.Objects.CVL;
using IriverCvlValue = inRiver.Remoting.Objects.CVLValue;
using IriverEntityType = inRiver.Remoting.Objects.EntityType;
using IriverFieldSet = inRiver.Remoting.Objects.FieldSet;
using IriverLinkType = inRiver.Remoting.Objects.LinkType;
using IriverCategory = inRiver.Remoting.Objects.Category;
using IriverRole = inRiver.Remoting.Objects.Role;
using IriverPermission = inRiver.Remoting.Objects.Permission;
using IriverCompletenessDefinition = inRiver.Remoting.Objects.CompletenessDefinition;
using IriverCompletenessGroup = inRiver.Remoting.Objects.CompletenessGroup;
using IriverCompletenessBusinessRule = inRiver.Remoting.Objects.CompletenessBusinessRule;
using IriverCompletenessRuleSetting = inRiver.Remoting.Objects.CompletenessRuleSetting;
using IriverRestricted = inRiver.Remoting.Objects.RestrictedFieldPermission;
using ModelMeister.Inriver.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Snapshot;

/// <summary>
/// Reads the full inriver model surface into a <see cref="LiveModel"/>. Pure-read; safe to call repeatedly.
/// </summary>
public sealed class InriverSnapshot
{
    private readonly InriverClient _client;
    private readonly ILogger _log;

    /// <summary>Build a snapshot reader bound to an existing (already-connected) client.</summary>
    public InriverSnapshot(InriverClient client, ILogger<InriverSnapshot>? log = null)
    {
        _client = client;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Capture the live model. Top-level reads run in parallel; Polly retry wraps each call.</summary>
    public LiveModel Capture()
    {
        _log.LogInformation("Capturing inriver model snapshot from {Url}", _client.Url);

        // Top-level reads are independent — parallelise. Each ReadAsync<T> goes through the
        // client's retry pipeline.
        var tEntityTypes = ReadAsync(m => m.ModelService.GetAllEntityTypes() ?? []);
        var tCategories = ReadAsync(m => m.ModelService.GetAllCategories() ?? []);
        var tLinkTypes = ReadAsync(m => m.ModelService.GetAllLinkTypes() ?? []);
        var tFieldSets = ReadAsync(m => m.ModelService.GetAllFieldSets() ?? []);
        var tCvls = ReadAsync(m => m.ModelService.GetAllCVLs() ?? []);
        var tCvlValues = ReadAsync(m => m.ModelService.GetAllCVLValues() ?? []);
        var tRoles = ReadAsync(m => m.UserService.GetAllRoles() ?? []);
        var tPermissions = ReadAsync(m => m.UserService.GetAllPermissions() ?? []);
        var tRestricted = ReadAsync(m => m.UserService.GetAllRestrictedFieldPermissions() ?? []);
        var tLanguages = ReadAsync(m => m.UtilityService.GetAllLanguages() ?? []);
        var tCompletenessDefs = ReadAsync(m => m.ModelService.GetAllCompletenessDefinitions() ?? []);
        // Bulk completeness rule fetch — kept for future direct-id lookups; we still need per-group
        // associations because the bulk DTO doesn't carry the owning group id directly.
        var tAllRules = ReadAsync(m => m.ModelService.GetAllCompletenessBusinessRules() ?? []);

        Task.WaitAll(
            tEntityTypes, tCategories, tLinkTypes, tFieldSets, tCvls, tCvlValues,
            tRoles, tPermissions, tRestricted, tLanguages, tCompletenessDefs, tAllRules);

        var entityTypes = tEntityTypes.Result;
        var categories = tCategories.Result;
        var linkTypes = tLinkTypes.Result;
        var fieldSets = tFieldSets.Result;
        var cvls = tCvls.Result;
        var cvlValues = tCvlValues.Result;
        var roles = tRoles.Result;
        var permissions = tPermissions.Result;
        var restricted = tRestricted.Result;
        var languages = tLanguages.Result;
        var completenessDefinitions = tCompletenessDefs.Result;
        _ = tAllRules.Result; // see comment above — kept for future use.

        // Completeness fan-outs. No bulk-by-definition / bulk-by-group endpoints, so parallelise per id.
        var groupsByDef = ParallelFetch(
            completenessDefinitions.Select(d => d.Id),
            id => _client.Read(m => m.ModelService.GetCompletenessGroupForDefinition(id) ?? []).ToList());

        var allGroups = groupsByDef.Values.SelectMany(v => v).ToList();
        var rulesByGroup = ParallelFetch(
            allGroups.Select(g => g.Id),
            id => _client.Read(m => m.ModelService.GetCompletenessBusinessRulesForGroup(id) ?? []).ToList());

        var ruleIds = rulesByGroup.Values.SelectMany(v => v).Select(r => r.Id).Distinct();
        var settingsByRule = ParallelFetch(
            ruleIds,
            id => _client.Read(m => m.ModelService.GetAllCompletenessRuleSettingsForBusinessRule(id) ?? []).ToList());

        var cvlValuesByCvl = cvlValues
            .GroupBy(v => v.CVLId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return new LiveModel
        {
            EnvironmentUrl = _client.Url,
            CapturedUtc = DateTime.UtcNow,
            EntityTypes = entityTypes.Select(EntityTypeMapper.ToLive).ToList(),
            Cvls = cvls.Select(c => CvlMapper.ToLive(
                c,
                cvlValuesByCvl.GetValueOrDefault(c.Id) ?? [])).ToList(),
            Categories = categories.Select(CategoryMapper.ToLive).ToList(),
            Fieldsets = fieldSets.Select(FieldsetMapper.ToLive).ToList(),
            LinkTypes = linkTypes.Select(LinkTypeMapper.ToLive).ToList(),
            Roles = roles.Select(RoleMapper.ToLive).ToList(),
            Permissions = permissions.Select(PermissionMapper.ToLive).ToList(),
            RestrictedFieldPermissions = restricted.Select(RestrictedFieldPermissionMapper.ToLive).ToList(),
            CompletenessDefinitions = completenessDefinitions.Select(d => CompletenessMapper.ToLive(
                d,
                groupsByDef.GetValueOrDefault(d.Id) ?? [],
                gid => rulesByGroup.GetValueOrDefault(gid) ?? [],
                rid => settingsByRule.GetValueOrDefault(rid) ?? [])).ToList(),
            Languages = languages.Select(c => c.Name).ToList(),
        };
    }

    private Task<T> ReadAsync<T>(Func<RemoteManager, T> call) => Task.Run(() => _client.Read(call));

    /// <summary>Run <paramref name="fetch"/> in parallel for each id and return a dictionary keyed by id.</summary>
    private static Dictionary<int, List<T>> ParallelFetch<T>(IEnumerable<int> ids, Func<int, List<T>> fetch)
    {
        var tasks = ids
            .Distinct()
            .Select(id => Task.Run(() => (Id: id, Result: fetch(id))))
            .ToArray();
        Task.WaitAll(tasks);
        return tasks.ToDictionary(t => t.Result.Id, t => t.Result.Result);
    }
}
