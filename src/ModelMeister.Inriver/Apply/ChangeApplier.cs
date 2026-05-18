using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Mapping;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Apply;

/// <summary>
/// Applies a <see cref="ModelChangeSet"/> against a live inriver environment in topological order.
/// The <see cref="ApplyOrder"/> map enforces creation-before-dependency and deletion-after-dependency
/// — change types not listed there are appended at the end.
/// </summary>
public sealed class ChangeApplier
{
    /// <summary>
    /// The strict topological order in which change kinds are applied. Adds run before dependent adds;
    /// deletes run in reverse dependency order at the tail.
    /// </summary>
    /// <remarks>This ordering is a hard contract. If you don't deeply understand a position, leave it alone.</remarks>
    private static readonly Type[] ApplyOrder =
    [
        typeof(AddLanguage),
        typeof(AddCategory),
        typeof(UpdateCategory),
        typeof(AddCvl),
        typeof(UpdateCvl),
        typeof(AddCvlValue),
        typeof(UpdateCvlValue),
        typeof(AddEntityType),
        typeof(UpdateEntityType),
        typeof(AddFieldset),
        typeof(UpdateFieldset),
        typeof(AddFieldType),
        typeof(UpdateFieldType),
        typeof(ChangeFieldDatatype),
        typeof(AddFieldToFieldset),
        typeof(RemoveFieldFromFieldset),
        typeof(AddLinkType),
        typeof(UpdateLinkType),
        typeof(AddRole),
        typeof(UpdateRole),
        typeof(AddPermissionToRole),
        typeof(RemovePermissionFromRole),
        typeof(AddRestrictedFieldPermission),
        typeof(RemoveRestrictedFieldPermission),
        typeof(AddCompletenessGroup),
        typeof(UpdateCompletenessGroup),
        // Deletes — reverse dependency order.
        typeof(DeactivateCvlValue),
        typeof(DeleteFieldType),
        typeof(DeleteFieldset),
        typeof(DeleteLinkType),
        typeof(DeleteEntityType),
        typeof(DeleteCvl),
        typeof(DeleteCategory),
        typeof(DeleteRole),
    ];

    private static readonly IReadOnlyDictionary<Type, int> ApplyOrderIndex =
        ApplyOrder
            .Select((t, i) => (t, i))
            .ToDictionary(p => p.t, p => p.i);

    private readonly InriverClient _client;
    private readonly ILogger _log;

    /// <summary>Build an applier bound to an already-connected client.</summary>
    public ChangeApplier(InriverClient client, ILogger<ChangeApplier>? log = null)
    {
        _client = client;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Apply the change set. See the parameterised overload for the full contract.</summary>
    public Task<ChangeReceipt> ApplyAsync(
        ModelChangeSet changeSet,
        LoadedModel codeModel,
        LiveModel liveSnapshot,
        bool dryRun,
        string? backupPath,
        CancellationToken ct = default) =>
        ApplyAsync(changeSet, codeModel, liveSnapshot, dryRun, backupPath, progress: null, ct);

    /// <summary>
    /// Apply the change set, sequentially in <see cref="ApplyOrder"/>. On <paramref name="dryRun"/>
    /// nothing is sent to inriver but the receipt is fully populated. <paramref name="progress"/>
    /// receives one entry per change after that change has been attempted.
    /// </summary>
    public async Task<ChangeReceipt> ApplyAsync(
        ModelChangeSet changeSet,
        LoadedModel codeModel,
        LiveModel liveSnapshot,
        bool dryRun,
        string? backupPath,
        IProgress<ChangeReceiptEntry>? progress,
        CancellationToken ct = default)
    {
        var startedUtc = DateTime.UtcNow;
        var receipt = new ChangeReceipt
        {
            EnvironmentUrl = _client.Url,
            StartedUtc = startedUtc,
            FinishedUtc = startedUtc,
            DryRun = dryRun,
            BackupFile = backupPath,
        };

        var context = BuildContext(codeModel, liveSnapshot);

        var orderedChanges = changeSet.Changes
            .OrderBy(c => ApplyOrderIndex.GetValueOrDefault(c.GetType(), int.MaxValue))
            .ToList();

        foreach (var change in orderedChanges)
        {
            ct.ThrowIfCancellationRequested();
            var entry = await ApplyAndRecord(change, context, dryRun, ct).ConfigureAwait(false);
            receipt.Entries.Add(entry);
            progress?.Report(entry);

            // Dry-run never hits an async I/O boundary, so the loop runs synchronously and the
            // UI thread can't drain the IProgress callbacks (which marshal to the captured
            // SynchronizationContext) until the entire batch finishes. Yielding here forces a
            // dispatcher tick per change so progress + cancel stay responsive on large change sets.
            if (dryRun) await Task.Yield();
        }

        receipt.FinishedUtc = DateTime.UtcNow;
        return receipt;
    }

    private async Task<ChangeReceiptEntry> ApplyAndRecord(ModelChange change, ApplyContext context, bool dryRun, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string? error = null;
        var success = true;

        if (dryRun)
        {
            _log.LogInformation("DRY-RUN: {Change}", change.Describe());
        }
        else
        {
            try
            {
                await ApplyOne(change, context, ct).ConfigureAwait(false);
                _log.LogInformation("Applied: {Change}", change.Describe());
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
                _log.LogError(ex, "Failed: {Change}", change.Describe());
            }
        }
        sw.Stop();
        return new ChangeReceiptEntry
        {
            Kind = change.GetType().Name,
            Description = change.Describe(),
            Succeeded = success,
            Error = error,
            DurationMs = sw.ElapsedMilliseconds,
        };
    }

    private static ApplyContext BuildContext(LoadedModel codeModel, LiveModel liveSnapshot)
    {
        // Index live fields for read-through preservation on Update (units, nullable derived defaults).
        var liveFieldsByKey = liveSnapshot.EntityTypes
            .SelectMany(e => e.Fields.Select(f => (Key: FieldKey(e.Id, f.Id), Field: f)))
            .ToDictionary(p => p.Key, p => p.Field, StringComparer.OrdinalIgnoreCase);

        return new ApplyContext(
            PermissionIdByName: liveSnapshot.Permissions.ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase),
            RoleIdByName: liveSnapshot.Roles.ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase),
            // Category id is keyed by CLR type, NOT Type.Name — a sanitized class name does not
            // generally equal the inriver id. See FieldTypeMapper.ResolveCategoryId.
            CvlIdByClrType: codeModel.Cvls.ToDictionary(c => c.ClrType, c => c.CvlId),
            CategoryIdByClrType: codeModel.Categories.ToDictionary(c => c.ClrType, c => c.CategoryId),
            LiveFieldsByKey: liveFieldsByKey,
            LiveFieldsetsById: liveSnapshot.Fieldsets.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase),
            LiveRolesByName: liveSnapshot.Roles.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static string FieldKey(string entityTypeId, string fieldId) => $"{entityTypeId}::{fieldId}";

    private async Task ApplyOne(ModelChange change, ApplyContext ctx, CancellationToken ct)
    {
        switch (change)
        {
            case AddLanguage al:
                await _client.WriteAsync(m => m.UtilityService.AddLanguage(CultureInfo.GetCultureInfo(al.IsoCode)), ct).ConfigureAwait(false);
                break;

            // Categories
            case AddCategory ac:
                await _client.WriteAsync(m => m.ModelService.AddCategory(CategoryMapper.ToInriver(ac.Category)), ct).ConfigureAwait(false);
                break;
            case UpdateCategory uc:
                await _client.WriteAsync(m => m.ModelService.UpdateCategory(CategoryMapper.ToInriver(uc.Category)), ct).ConfigureAwait(false);
                break;
            case DeleteCategory dc:
                await _client.WriteAsync(m => m.ModelService.DeleteCategory(dc.Id), ct).ConfigureAwait(false);
                break;

            // CVLs
            case AddCvl acvl:
                await _client.WriteAsync(m => m.ModelService.AddCVL(CvlMapper.ToInriver(acvl.Cvl)), ct).ConfigureAwait(false);
                break;
            case UpdateCvl ucvl:
                await _client.WriteAsync(m => m.ModelService.UpdateCVL(CvlMapper.ToInriver(ucvl.Cvl)), ct).ConfigureAwait(false);
                break;
            case DeleteCvl delCvl:
                await _client.WriteAsync(m => m.ModelService.DeleteCVL(delCvl.Id), ct).ConfigureAwait(false);
                break;

            // CVL values
            case AddCvlValue acv:
                await _client.WriteAsync(m => m.ModelService.AddCVLValue(CvlValueMapper.ToInriver(acv.Value, acv.CvlId)), ct).ConfigureAwait(false);
                break;
            case UpdateCvlValue ucv:
                {
                    var dto = CvlValueMapper.ToInriver(ucv.Value, ucv.CvlId);
                    dto.Id = ucv.LiveId;
                    await _client.WriteAsync(m => m.ModelService.UpdateCVLValue(dto), ct).ConfigureAwait(false);
                    break;
                }
            case DeactivateCvlValue dcv:
                await _client.WriteAsync(m =>
                {
                    var v = m.ModelService.GetCVLValue(dcv.LiveId);
                    if (v is null) return false;
                    v.Deactivated = true;
                    m.ModelService.UpdateCVLValue(v);
                    return true;
                }, ct).ConfigureAwait(false);
                break;

            // Entity types
            case AddEntityType ae:
                await _client.WriteAsync(m => m.ModelService.AddEntityType(EntityTypeMapper.ToInriver(ae.EntityType, ae.EntityType.Fields)), ct).ConfigureAwait(false);
                break;
            case UpdateEntityType ue:
                await _client.WriteAsync(m => m.ModelService.UpdateEntityType(EntityTypeMapper.ToInriver(ue.EntityType, ue.EntityType.Fields)), ct).ConfigureAwait(false);
                break;
            case DeleteEntityType de:
                await _client.WriteAsync(m => m.ModelService.DeleteEntityType(de.Id), ct).ConfigureAwait(false);
                break;

            // Field types
            case AddFieldType af:
                await _client.WriteAsync(m => m.ModelService.AddFieldType(
                    FieldTypeMapper.ToInriver(af.Field, af.Owner, ctx.CvlIdByClrType, live: null, ctx.CategoryIdByClrType)), ct).ConfigureAwait(false);
                break;
            case UpdateFieldType uf:
                {
                    // Pass the live field through so read-through nullables (units, default value, index) are preserved.
                    ctx.LiveFieldsByKey.TryGetValue(FieldKey(uf.Owner.EntityTypeId, uf.Field.Id), out var liveFt);
                    await _client.WriteAsync(m => m.ModelService.UpdateFieldType(
                        FieldTypeMapper.ToInriver(uf.Field, uf.Owner, ctx.CvlIdByClrType, liveFt, ctx.CategoryIdByClrType)), ct).ConfigureAwait(false);
                    break;
                }
            case ChangeFieldDatatype cfd:
                {
                    ctx.LiveFieldsByKey.TryGetValue(FieldKey(cfd.Owner.EntityTypeId, cfd.Field.Id), out var liveFt);
                    await _client.WriteAsync(m => m.ModelService.UpdateFieldType(
                        FieldTypeMapper.ToInriver(cfd.Field, cfd.Owner, ctx.CvlIdByClrType, liveFt, ctx.CategoryIdByClrType)), ct).ConfigureAwait(false);
                    break;
                }
            case DeleteFieldType df:
                await _client.WriteAsync(m => m.ModelService.DeleteFieldType(df.Id), ct).ConfigureAwait(false);
                break;

            // Fieldsets
            case AddFieldset afs:
                await _client.WriteAsync(m => m.ModelService.AddFieldSet(FieldsetMapper.ToInriver(afs.Fieldset)), ct).ConfigureAwait(false);
                break;
            case UpdateFieldset ufs:
                {
                    ctx.LiveFieldsetsById.TryGetValue(ufs.Fieldset.FieldsetId, out var liveFs);
                    await _client.WriteAsync(m => m.ModelService.UpdateFieldSet(FieldsetMapper.ToInriver(ufs.Fieldset, liveFs)), ct).ConfigureAwait(false);
                    break;
                }
            case DeleteFieldset dfs:
                await _client.WriteAsync(m => m.ModelService.DeleteFieldSet(dfs.Id), ct).ConfigureAwait(false);
                break;
            case AddFieldToFieldset aff:
                await _client.WriteAsync(m => m.ModelService.AddFieldTypeToFieldSet(aff.FieldsetId, aff.FieldTypeId), ct).ConfigureAwait(false);
                break;
            case RemoveFieldFromFieldset rff:
                await _client.WriteAsync(m => m.ModelService.DeleteFieldTypeFromFieldSet(rff.FieldsetId, rff.FieldTypeId), ct).ConfigureAwait(false);
                break;

            // Link types
            case AddLinkType al:
                await _client.WriteAsync(m => m.ModelService.AddLinkType(LinkTypeMapper.ToInriver(al.LinkType)), ct).ConfigureAwait(false);
                break;
            case UpdateLinkType ul:
                await _client.WriteAsync(m => m.ModelService.UpdateLinkType(LinkTypeMapper.ToInriver(ul.LinkType)), ct).ConfigureAwait(false);
                break;
            case DeleteLinkType dl:
                await _client.WriteAsync(m => m.ModelService.DeleteLinkType(dl.Id), ct).ConfigureAwait(false);
                break;

            // Roles & permissions
            case AddRole ar:
                await _client.WriteAsync(m => m.UserService.AddRole(RoleMapper.ToInriver(ar.Role)), ct).ConfigureAwait(false);
                break;
            case UpdateRole ur:
                await _client.WriteAsync(m =>
                {
                    if (!ctx.LiveRolesByName.TryGetValue(ur.Role.Name, out var liveRole))
                    {
                        // No live counterpart — fall back to Add-shaped DTO (applier shouldn't normally reach here).
                        var dto = RoleMapper.ToInriver(ur.Role);
                        if (ctx.RoleIdByName.TryGetValue(ur.Role.Name, out var rid)) dto.Id = rid;
                        return m.UserService.UpdateRole(dto);
                    }
                    return m.UserService.UpdateRole(RoleMapper.ToInriverForUpdate(ur.Role, liveRole));
                }, ct).ConfigureAwait(false);
                break;
            case DeleteRole drole:
                await _client.WriteAsync(m => m.UserService.DeleteRole(drole.LiveId), ct).ConfigureAwait(false);
                break;

            case AddPermissionToRole apr:
                await _client.WriteAsync(m =>
                {
                    // Permissions are platform-managed concepts; resolve by name lazily if not in the snapshot.
                    if (!ctx.PermissionIdByName.TryGetValue(apr.PermissionName, out var pid))
                    {
                        var existing = m.UserService.GetPermissionByName(apr.PermissionName);
                        if (existing is null) return false;
                        pid = existing.Id;
                    }
                    var rid = apr.RoleId != 0
                        ? apr.RoleId
                        : m.UserService.GetRoleByName(apr.RoleName)?.Id ?? 0;
                    if (rid == 0) return false;
                    return m.UserService.AddPermissionToRole(pid, rid);
                }, ct).ConfigureAwait(false);
                break;
            case RemovePermissionFromRole rpr:
                await _client.WriteAsync(m => m.UserService.RemovePermissionFromRole(rpr.PermissionId, rpr.RoleId), ct).ConfigureAwait(false);
                break;

            // Restricted field permissions
            case AddRestrictedFieldPermission arfp:
                await _client.WriteAsync(m => m.UserService.AddRestrictedFieldPermission(RestrictedFieldPermissionMapper.ToInriver(arfp.Desired)), ct).ConfigureAwait(false);
                break;
            case RemoveRestrictedFieldPermission rrfp:
                await _client.WriteAsync(m => m.UserService.DeleteRestrictedFieldPermission(rrfp.LiveId), ct).ConfigureAwait(false);
                break;

            // Completeness add/update intentionally not auto-applied: the 3-tier shape
            // (Definition/Group/Rule) requires up-front mapping config the v1 model layer
            // does not yet expose. Surfaced as a warning by the differ instead.
            case AddCompletenessGroup:
            case UpdateCompletenessGroup:
                _log.LogWarning("Completeness apply not yet implemented for {Kind}", change.GetType().Name);
                break;

            default:
                _log.LogWarning("No applier registered for {Kind}", change.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// Snapshot of all the per-apply lookup tables — kept on the stack to avoid threading nine
    /// parameters through every <see cref="ApplyOne"/> branch.
    /// </summary>
    private sealed record ApplyContext(
        Dictionary<string, int> PermissionIdByName,
        Dictionary<string, int> RoleIdByName,
        Dictionary<Type, string> CvlIdByClrType,
        Dictionary<Type, string> CategoryIdByClrType,
        Dictionary<string, LiveFieldType> LiveFieldsByKey,
        Dictionary<string, LiveFieldset> LiveFieldsetsById,
        Dictionary<string, LiveRole> LiveRolesByName);
}
