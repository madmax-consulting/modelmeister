using inRiver.Remoting.Objects;
using ModelMeister.Inriver.Mapping;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Cvl;

/// <summary>
/// CRUD over CVL definitions and their values on the connected environment, used by the CVL
/// workbench's in-place editing. CVL-level create/update/delete go straight through
/// <c>ModelService.AddCVL/UpdateCVL/DeleteCVL</c>; value-level upsert/delete reuse the surgical
/// single-value helpers on <see cref="CvlSync"/> (matched by key, parent-CVL-existence assumed).
/// </summary>
public sealed class CvlAdmin
{
    private readonly InriverClient _client;

    public CvlAdmin(InriverClient client) => _client = client;

    /// <summary>True when a CVL with this id already exists (used to block duplicate creates).</summary>
    public bool Exists(string cvlId) => _client.Read(m => m.ModelService.GetCVL(cvlId)) is not null;

    /// <summary>Create a new CVL definition.</summary>
    public Task AddCvlAsync(string id, CvlDataType dataType, string? parentId, bool customValueList, CancellationToken ct = default)
        => _client.WriteAsync(m => m.ModelService.AddCVL(new CVL
        {
            Id = id,
            DataType = DatatypeMapper.CvlToInriver(dataType),
            ParentId = string.IsNullOrEmpty(parentId) ? null : parentId,
            CustomValueList = customValueList,
        }), ct);

    /// <summary>Update an existing CVL definition's datatype / parent / custom flag in place.</summary>
    public async Task UpdateCvlAsync(string id, CvlDataType dataType, string? parentId, bool customValueList, CancellationToken ct = default)
    {
        var existing = _client.Read(m => m.ModelService.GetCVL(id))
            ?? throw new InvalidOperationException($"CVL '{id}' not found on the connected environment.");
        existing.DataType = DatatypeMapper.CvlToInriver(dataType);
        existing.ParentId = string.IsNullOrEmpty(parentId) ? null : parentId;
        existing.CustomValueList = customValueList;
        await _client.WriteAsync(m => m.ModelService.UpdateCVL(existing), ct).ConfigureAwait(false);
    }

    /// <summary>Delete a CVL definition (and, server-side, its values).</summary>
    public Task DeleteCvlAsync(string id, CancellationToken ct = default)
        => _client.WriteAsync(m => m.ModelService.DeleteCVL(id), ct);

    /// <summary>List a CVL's values in index order (snapshot DTOs).</summary>
    public IReadOnlyList<LiveCvlValue> ListValues(string cvlId)
        => _client.Read(m => m.ModelService.GetCVLValuesForCVL(cvlId, forceGet: true) ?? new List<CVLValue>())
                  .Select(CvlValueMapper.ToLive)
                  .OrderBy(v => v.Index)
                  .ToList();

    /// <summary>Add (new key) or update (existing key) a single CVL value.</summary>
    public Task UpsertValueAsync(string cvlId, LiveCvlValue value, CancellationToken ct = default)
        => CvlSync.ApplyValueAsync(_client, cvlId, value, ct);

    /// <summary>Delete a single CVL value by key. Idempotent.</summary>
    public Task DeleteValueAsync(string cvlId, string key, CancellationToken ct = default)
        => CvlSync.DeleteValueAsync(_client, cvlId, key, ct);
}
