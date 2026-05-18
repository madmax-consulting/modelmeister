using IriverCategory = inRiver.Remoting.Objects.Category;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping between inriver <see cref="IriverCategory"/> and <see cref="LiveCategory"/> / <see cref="LoadedCategory"/>.</summary>
public static class CategoryMapper
{
    /// <summary>Inriver DTO -> snapshot DTO.</summary>
    public static LiveCategory ToLive(IriverCategory c) => new()
    {
        Id = c.Id,
        Name = LocaleStringMapper.ToTp(c.Name),
        Index = c.Index,
    };

    /// <summary>Code-defined category -> inriver DTO.</summary>
    public static IriverCategory ToInriver(LoadedCategory c) => new()
    {
        Id = c.CategoryId,
        Name = LocaleStringMapper.ToInriver(c.Name),
        Index = c.Index,
    };
}
