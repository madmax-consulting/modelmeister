using inRiver.Remoting;
using inRiver.Remoting.Query;
using IriverWorkAreaFolder = inRiver.Remoting.Objects.WorkAreaFolder;

namespace ModelMeister.Inriver.WorkAreas;

/// <summary>
/// The set of Remoting calls that differ between <b>shared</b> and <b>personal</b> work-area folders.
/// Everything else — path computation, planning, reconcile ordering, query (de)serialization — is
/// identical and lives in <see cref="WorkAreaService"/>. Implementations are thin shims over
/// <c>IUtilityService</c>; the Polly retry / write-serialisation wrapping stays in
/// <see cref="WorkAreaService"/> via <see cref="InriverClient"/> (each call is invoked inside a
/// <see cref="InriverClient.Read{T}"/>/<see cref="InriverClient.WriteAsync{T}"/> lambda).
/// </summary>
internal interface IWorkAreaScope
{
    /// <summary>Owner username for a personal scope; <c>null</c> for shared.</summary>
    string? OwnerUsername { get; }

    /// <summary>Whether this scope supports the syndication flag (shared only).</summary>
    bool SupportsSyndication { get; }

    IReadOnlyList<IriverWorkAreaFolder> GetAll(RemoteManager m);
    IriverWorkAreaFolder Add(RemoteManager m, IriverWorkAreaFolder folder);
    IriverWorkAreaFolder Rename(RemoteManager m, Guid id, string name);
    IriverWorkAreaFolder Move(RemoteManager m, Guid id, Guid newParentId, int newIndex);
    IriverWorkAreaFolder SetIndex(RemoteManager m, Guid id, int newIndex);
    IriverWorkAreaFolder SetSyndication(RemoteManager m, Guid id, bool isSyndication);
    IriverWorkAreaFolder SetQuery(RemoteManager m, Guid id, ComplexQuery query);
    bool Delete(RemoteManager m, Guid id);
}
