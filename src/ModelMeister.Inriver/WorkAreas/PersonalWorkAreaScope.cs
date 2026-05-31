using inRiver.Remoting;
using inRiver.Remoting.Query;
using IriverWorkAreaFolder = inRiver.Remoting.Objects.WorkAreaFolder;

namespace ModelMeister.Inriver.WorkAreas;

/// <summary>
/// Binds <see cref="WorkAreaService"/> to a single user's <c>PersonalWorkAreaFolder</c> Remoting surface.
/// Personal folders are owned by a user (<see cref="IriverWorkAreaFolder.Username"/>) and have no
/// syndication concept — <see cref="SetSyndication"/> is never invoked (guarded by
/// <see cref="SupportsSyndication"/> in <see cref="WorkAreaService"/>) and throws if it ever is.
/// </summary>
internal sealed class PersonalWorkAreaScope : IWorkAreaScope
{
    private readonly string _username;

    public PersonalWorkAreaScope(string username) => _username = username;

    public string? OwnerUsername => _username;
    public bool SupportsSyndication => false;

    public IReadOnlyList<IriverWorkAreaFolder> GetAll(RemoteManager m) =>
        m.UtilityService.GetAllPersonalWorkAreaFoldersForUser(_username, includeEntities: false) ?? [];

    public IriverWorkAreaFolder? GetOne(RemoteManager m, Guid id) =>
        m.UtilityService.GetPersonalWorkAreaFolder(id);

    public IriverWorkAreaFolder Add(RemoteManager m, IriverWorkAreaFolder folder)
    {
        folder.Username = _username; // personal folders carry their owner
        return m.UtilityService.AddPersonalWorkAreaFolder(folder);
    }

    public IriverWorkAreaFolder Rename(RemoteManager m, Guid id, string name) =>
        m.UtilityService.UpdatePersonalWorkAreaFolderName(id, name);

    public IriverWorkAreaFolder Move(RemoteManager m, Guid id, Guid newParentId, int newIndex) =>
        m.UtilityService.MovePersonalWorkAreaFolder(id, newParentId, newIndex);

    public IriverWorkAreaFolder SetIndex(RemoteManager m, Guid id, int newIndex) =>
        m.UtilityService.UpdatePersonalWorkAreaFolderIndex(id, newIndex);

    public IriverWorkAreaFolder SetSyndication(RemoteManager m, Guid id, bool isSyndication) =>
        throw new NotSupportedException("Personal work-area folders have no syndication flag.");

    public IriverWorkAreaFolder SetQuery(RemoteManager m, Guid id, ComplexQuery query) =>
        m.UtilityService.UpdatePersonalWorkAreaQuery(id, query);

    public bool Delete(RemoteManager m, Guid id) =>
        m.UtilityService.DeletePersonalWorkAreaFolder(id);
}
