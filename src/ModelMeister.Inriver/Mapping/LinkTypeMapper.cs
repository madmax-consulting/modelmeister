using IriverLinkType = inRiver.Remoting.Objects.LinkType;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping for link types, including inriver's default-name fallback semantics.</summary>
public static class LinkTypeMapper
{
    /// <summary>Inriver DTO -> snapshot DTO.</summary>
    public static LiveLinkType ToLive(IriverLinkType l) => new()
    {
        Id = l.Id,
        SourceEntityTypeId = l.SourceEntityTypeId,
        TargetEntityTypeId = l.TargetEntityTypeId,
        LinkEntityTypeId = string.IsNullOrEmpty(l.LinkEntityTypeId) ? null : l.LinkEntityTypeId,
        Index = l.Index,
        SourceName = LocaleStringMapper.ToTp(l.SourceName),
        TargetName = LocaleStringMapper.ToTp(l.TargetName),
    };

    /// <summary>Code-defined link type -> inriver DTO, with effective default names applied.</summary>
    public static IriverLinkType ToInriver(LoadedLinkType l) => new()
    {
        Id = l.LinkTypeId,
        SourceEntityTypeId = l.SourceEntityTypeId,
        TargetEntityTypeId = l.TargetEntityTypeId,
        LinkEntityTypeId = l.LinkEntityTypeId ?? string.Empty,
        Index = l.Index,
        SourceName = LocaleStringMapper.ToInriver(EffectiveSourceName(l)),
        TargetName = LocaleStringMapper.ToInriver(EffectiveTargetName(l)),
    };

    /// <summary>
    /// When the code-side <see cref="LoadedLinkType.SourceName"/> is empty, inriver's default
    /// label is the target entity type name. Materialising that default lets the diff treat
    /// "name not set in code" as "use the platform default" rather than as a request to clear
    /// the label.
    /// </summary>
    public static LocaleString EffectiveSourceName(LoadedLinkType l) =>
        IsEmpty(l.SourceName) ? new LocaleString(l.TargetEntityTypeId) : l.SourceName;

    /// <summary>Mirror of <see cref="EffectiveSourceName"/> — defaults to the source entity type name.</summary>
    public static LocaleString EffectiveTargetName(LoadedLinkType l) =>
        IsEmpty(l.TargetName) ? new LocaleString(l.SourceEntityTypeId) : l.TargetName;

    private static bool IsEmpty(LocaleString ls) =>
        string.IsNullOrEmpty(ls.DefaultValue) && ls.Values.Count == 0;
}
