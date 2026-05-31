using inRiver.Remoting;
using inRiver.Remoting.Query;
using IriverWorkAreaFolder = inRiver.Remoting.Objects.WorkAreaFolder;

namespace ModelMeister.Inriver.WorkAreas;

/// <summary>Binds <see cref="WorkAreaService"/> to the <c>SharedWorkAreaFolder</c> Remoting surface.</summary>
internal sealed class SharedWorkAreaScope : IWorkAreaScope
{
    public string? OwnerUsername => null;
    public bool SupportsSyndication => true;

    public IReadOnlyList<IriverWorkAreaFolder> GetAll(RemoteManager m) =>
        m.UtilityService.GetAllSharedWorkAreaFolders(includeEntities: false) ?? [];

    public IriverWorkAreaFolder? GetOne(RemoteManager m, Guid id) =>
        m.UtilityService.GetSharedWorkAreaFolder(id);

    public IriverWorkAreaFolder Add(RemoteManager m, IriverWorkAreaFolder folder) =>
        m.UtilityService.AddSharedWorkAreaFolder(folder);

    public IriverWorkAreaFolder Rename(RemoteManager m, Guid id, string name) =>
        m.UtilityService.UpdateSharedWorkAreaFolderName(id, name);

    public IriverWorkAreaFolder Move(RemoteManager m, Guid id, Guid newParentId, int newIndex) =>
        m.UtilityService.MoveSharedWorkAreaFolder(id, newParentId, newIndex);

    public IriverWorkAreaFolder SetIndex(RemoteManager m, Guid id, int newIndex) =>
        m.UtilityService.UpdateSharedWorkAreaFolderIndex(id, newIndex);

    public IriverWorkAreaFolder SetSyndication(RemoteManager m, Guid id, bool isSyndication) =>
        m.UtilityService.UpdateSharedWorkAreaSyndication(id, isSyndication);

    public IriverWorkAreaFolder SetQuery(RemoteManager m, Guid id, ComplexQuery query) =>
        m.UtilityService.UpdateSharedWorkAreaQuery(id, query);

    public bool Delete(RemoteManager m, Guid id) =>
        m.UtilityService.DeleteSharedWorkAreaFolder(id);
}
