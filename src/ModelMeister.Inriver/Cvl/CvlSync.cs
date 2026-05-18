using inRiver.Remoting.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelMeister.Inriver.Snapshot;
using LocaleString = ModelMeister.Model.Primitives.LocaleString;

namespace ModelMeister.Inriver.Cvl;

/// <summary>
/// Sync CVL values from a previously-captured source <see cref="LiveModel"/> into a connected
/// target environment. The source must already have been snapshotted (export, JSON, or from the UI's
/// dual-snapshot capture) — this respects the inriver Remoting process-singleton constraint.
/// </summary>
/// <remarks>
/// To capture the source: connect to it, call <c>InriverSnapshot.Capture()</c>, disconnect, then
/// connect to the target and call this sync. The CLI command <c>cvl sync</c> wraps the whole flow
/// (it shells out to a separate process if needed).
/// </remarks>
public sealed class CvlSync
{
    /// <summary>Per-CVL plan produced by <see cref="PlanFor"/>: keys to add, update, deactivate.</summary>
    public sealed record Plan(string CvlId, IReadOnlyList<string> Add, IReadOnlyList<string> Update, IReadOnlyList<string> Deactivate)
    {
        public int Total => Add.Count + Update.Count + Deactivate.Count;
    }

    /// <summary>Outcome of <see cref="ApplyAsync"/>.</summary>
    public sealed record Result(string CvlId, int Added, int Updated, int Deactivated, IReadOnlyList<string> Errors);

    /// <summary>Behaviour flags for sync. Defaults are non-destructive (no deactivation, dry-run off, overwrite on).</summary>
    public sealed record Options(
        bool AllowDeactivate = false,
        bool OverwriteValues = true,
        bool DryRun = false);

    private readonly LiveModel _source;
    private readonly InriverClient _target;
    private readonly ILogger _log;

    public CvlSync(LiveModel source, InriverClient target, ILogger<CvlSync>? log = null)
    {
        _source = source;
        _target = target;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Compute the diff for one CVL without writing anything.</summary>
    public Plan PlanFor(string cvlId, Options? opts = null)
    {
        opts ??= new Options();
        var srcCvl = _source.Cvls.FirstOrDefault(c => c.Id.Equals(cvlId, StringComparison.OrdinalIgnoreCase));
        if (srcCvl is null) return new Plan(cvlId, [], [], []);

        var tgtList = _target.Read(m => m.ModelService.GetCVLValuesForCVL(cvlId, forceGet: true) ?? []);
        var src = srcCvl.Values.ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase);
        var tgt = tgtList.ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase);

        var add = src.Keys.Except(tgt.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var update = new List<string>();
        if (opts.OverwriteValues)
        {
            foreach (var key in src.Keys.Intersect(tgt.Keys, StringComparer.OrdinalIgnoreCase))
            {
                if (!ValuesEqual(src[key], tgt[key])) update.Add(key);
            }
        }
        var deactivate = opts.AllowDeactivate
            ? tgt.Where(kv => !src.ContainsKey(kv.Key) && !kv.Value.Deactivated)
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        return new Plan(cvlId, add, update, deactivate);
    }

    /// <summary>Apply a previously-computed <see cref="Plan"/> to the target environment.</summary>
    public async Task<Result> ApplyAsync(Plan plan, Options? opts = null, CancellationToken ct = default)
    {
        opts ??= new Options();
        var errors = new List<string>();
        int added = 0, updated = 0, deactivated = 0;

        if (opts.DryRun)
            return new Result(plan.CvlId, plan.Add.Count, plan.Update.Count, plan.Deactivate.Count, errors);

        var srcCvl = _source.Cvls.FirstOrDefault(c => c.Id.Equals(plan.CvlId, StringComparison.OrdinalIgnoreCase));
        if (srcCvl is null) return new Result(plan.CvlId, 0, 0, 0, [$"CVL '{plan.CvlId}' not in source."]);

        var src = srcCvl.Values.ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase);
        var tgtValues = _target.Read(m => m.ModelService.GetCVLValuesForCVL(plan.CvlId, forceGet: true) ?? [])
            .ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var key in plan.Add)
        {
            if (!src.TryGetValue(key, out var s)) continue;
            try
            {
                await _target.WriteAsync(m => m.ModelService.AddCVLValue(BuildCvlValue(plan.CvlId, s)), ct).ConfigureAwait(false);
                added++;
            }
            catch (Exception ex) { errors.Add($"Add {key}: {ex.Message}"); _log.LogWarning(ex, "Add CVL value failed: {Key}", key); }
        }

        foreach (var key in plan.Update)
        {
            if (!src.TryGetValue(key, out var s) || !tgtValues.TryGetValue(key, out var tgt)) continue;
            try
            {
                tgt.Value = ToRemotingValue(s.Value);
                tgt.ParentKey = s.ParentKey;
                tgt.Index = s.Index;
                tgt.Deactivated = s.Deactivated;
                await _target.WriteAsync(m => m.ModelService.UpdateCVLValue(tgt), ct).ConfigureAwait(false);
                updated++;
            }
            catch (Exception ex) { errors.Add($"Update {key}: {ex.Message}"); _log.LogWarning(ex, "Update CVL value failed: {Key}", key); }
        }

        foreach (var key in plan.Deactivate)
        {
            if (!tgtValues.TryGetValue(key, out var tgt)) continue;
            try
            {
                tgt.Deactivated = true;
                await _target.WriteAsync(m => m.ModelService.UpdateCVLValue(tgt), ct).ConfigureAwait(false);
                deactivated++;
            }
            catch (Exception ex) { errors.Add($"Deactivate {key}: {ex.Message}"); _log.LogWarning(ex, "Deactivate failed: {Key}", key); }
        }

        return new Result(plan.CvlId, added, updated, deactivated, errors);
    }

    /// <summary>
    /// Surgical single-value upsert: Add when the key is new on the target, Update otherwise. Used
    /// by the Compare UI's per-row promote when the caller already knows which one value needs to
    /// move and doesn't want to re-sync the whole CVL.
    /// </summary>
    /// <remarks>
    /// Caller must ensure the parent CVL exists on the target first — this method does not
    /// auto-create it.
    /// </remarks>
    public static async Task ApplyValueAsync(InriverClient target, string cvlId, Snapshot.LiveCvlValue source, CancellationToken ct = default)
    {
        var existing = (target.Read(m => m.ModelService.GetCVLValuesForCVL(cvlId, forceGet: true) ?? new List<CVLValue>()))
            .FirstOrDefault(v => string.Equals(v.Key, source.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            await target.WriteAsync(m => m.ModelService.AddCVLValue(BuildCvlValue(cvlId, source)), ct).ConfigureAwait(false);
            return;
        }
        existing.Value = ToRemotingValue(source.Value);
        existing.ParentKey = source.ParentKey;
        existing.Index = source.Index;
        existing.Deactivated = source.Deactivated;
        await target.WriteAsync(m => m.ModelService.UpdateCVLValue(existing), ct).ConfigureAwait(false);
    }

    /// <summary>Surgical single-value delete by key. Idempotent — no-op when the key is already absent.</summary>
    public static async Task DeleteValueAsync(InriverClient target, string cvlId, string key, CancellationToken ct = default)
    {
        var existing = (target.Read(m => m.ModelService.GetCVLValuesForCVL(cvlId, forceGet: true) ?? new List<CVLValue>()))
            .FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        await target.WriteAsync(m => m.ModelService.DeleteCVLValue(existing.Id), ct).ConfigureAwait(false);
    }

    /// <summary>True when the parent CVL exists on the target env. Used as a pre-check for <see cref="ApplyValueAsync"/>.</summary>
    public static bool TargetHasCvl(InriverClient target, string cvlId) =>
        target.Read(m => m.ModelService.GetCVL(cvlId)) is not null;

    private static CVLValue BuildCvlValue(string cvlId, Snapshot.LiveCvlValue s) => new CVLValue
    {
        CVLId = cvlId,
        Key = s.Key,
        Value = ToRemotingValue(s.Value),
        Index = s.Index,
        ParentKey = s.ParentKey,
        Deactivated = s.Deactivated,
    };

    private static object ToRemotingValue(LocaleString? ls)
    {
        if (ls is null) return string.Empty;
        if (ls.Values.Count > 0) return ls;
        return ls.DefaultValue ?? string.Empty;
    }

    private static bool ValuesEqual(Snapshot.LiveCvlValue source, CVLValue target)
    {
        if (source.Deactivated != target.Deactivated) return false;
        if (!string.Equals(source.ParentKey ?? "", target.ParentKey ?? "", StringComparison.Ordinal)) return false;
        return ValueObjectEquals(source.Value, target.Value);
    }

    private static bool ValueObjectEquals(LocaleString? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        var aText = a.DefaultValue ?? "";
        var bText = b is LocaleString lb ? (lb.DefaultValue ?? "") : (b.ToString() ?? "");
        if (!string.Equals(aText, bText, StringComparison.Ordinal)) return false;
        if (b is LocaleString lb2)
        {
            if (a.Values.Count != lb2.Values.Count) return false;
            foreach (var (k, v) in a.Values)
                if (!lb2.Values.TryGetValue(k, out var ov) || !string.Equals(v ?? "", ov ?? "", StringComparison.Ordinal))
                    return false;
        }
        return true;
    }
}
