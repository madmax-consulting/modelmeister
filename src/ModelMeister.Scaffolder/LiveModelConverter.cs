using System.Text.Json;
using ModelMeister.Inriver.Mapping;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Scaffolder;

/// <summary>
/// Adapts a <see cref="LiveModel"/> captured from inriver into the <see cref="InriverModelJson"/>
/// shape consumed by <see cref="ProjectScaffolder"/>. Lets the scaffolder operate on a live
/// environment without going through the export-JSON file format.
/// </summary>
public static class LiveModelConverter
{
    /// <summary>
    /// Project a live snapshot into the JSON DTO shape expected by <see cref="ProjectScaffolder"/>.
    /// The first configured language is treated as the master locale; the rest follow in their
    /// original order. Empty locale strings are dropped to keep generated source clean.
    /// </summary>
    public static InriverModelJson ToJsonModel(LiveModel live)
    {
        var languages = live.Languages;
        // Always seed an "en" fallback when the snapshot has no languages — keeps localised text
        // round-tripping through a sensible default rather than dropping it on the floor.
        var langOrder = languages.Count > 0 ? languages : ["en"];

        var result = new InriverModelJson
        {
            Languages = languages.Select(iso => new JsonLanguage
            {
                Name = iso,
            }).ToList(),
            Categories = live.Categories.Select(c => new JsonCategory
            {
                Id = c.Id,
                Name = LocaleStringToJson(c.Name, langOrder),
                Index = c.Index,
            }).ToList(),
            LinkTypes = live.LinkTypes.Select(l => new JsonLinkType
            {
                Id = l.Id,
                SourceEntityTypeId = l.SourceEntityTypeId,
                TargetEntityTypeId = l.TargetEntityTypeId,
                LinkEntityTypeId = l.LinkEntityTypeId,
                Index = l.Index,
                SourceName = LocaleStringToJson(l.SourceName, langOrder),
                TargetName = LocaleStringToJson(l.TargetName, langOrder),
            }).ToList(),
            FieldSets = live.Fieldsets.Select(f => new JsonFieldSet
            {
                Id = f.Id,
                EntityTypeId = f.EntityTypeId,
                Name = LocaleStringToJson(f.Name, langOrder),
                Description = LocaleStringToJson(f.Description, langOrder),
                FieldTypes = f.FieldTypeIds.ToList(),
            }).ToList(),
            EntityTypes = live.EntityTypes.Select(e => new JsonEntityType
            {
                Id = e.Id,
                Name = LocaleStringToJson(e.Name, langOrder),
                IsLinkEntityType = e.IsLinkEntityType,
                GetDisplayNameFieldTypeId = e.DisplayNameFieldId,
                GetDisplayDescriptionFieldTypeId = e.DisplayDescriptionFieldId,
                FieldTypes = e.Fields.Select(f => FieldTypeToJson(f, langOrder)).ToList(),
            }).ToList(),
            FieldTypes = live.EntityTypes
                .SelectMany(e => e.Fields)
                .Select(f => FieldTypeToJson(f, langOrder))
                .ToList(),
            Cvls = live.Cvls.Select(c => new JsonCvl
            {
                Id = c.Id,
                DataType = !string.IsNullOrEmpty(c.DataTypeRaw) ? c.DataTypeRaw : DatatypeMapper.CvlToInriver(c.DataType),
                ParentId = c.ParentId,
                CustomValueList = c.CustomValueList,
                Activated = true,
            }).ToList(),
            CvlValues = live.Cvls
                .SelectMany(c => c.Values.Select(v => new JsonCvlValue
                {
                    Id = v.Id,
                    CvlId = v.CvlId,
                    Key = v.Key,
                    Value = CvlValueToJsonElement(v.Value, langOrder),
                    Index = v.Index,
                    ParentKey = v.ParentKey,
                    Deactivated = v.Deactivated,
                }))
                .ToList(),
            Security = (live.Roles.Count > 0 || live.RestrictedFieldPermissions.Count > 0)
                ? new JsonSecurity
                {
                    Roles = live.Roles.Select(r => new JsonRole
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Description = r.Description,
                        Permissions = r.Permissions.Select(p => new JsonPermission
                        {
                            Id = p.Id,
                            Name = p.Name,
                            Description = p.Description,
                        }).ToList(),
                    }).ToList(),
                    RestrictedFieldPermissions = live.RestrictedFieldPermissions.Select(rp => new JsonRestrictedFieldPermission
                    {
                        Id = rp.Id,
                        RoleId = rp.RoleId,
                        RestrictionType = rp.RestrictionType,
                        EntityTypeId = rp.EntityTypeId,
                        FieldTypeId = rp.FieldTypeId,
                        CategoryId = rp.CategoryId,
                    }).ToList(),
                }
                : null,
            Completeness = live.CompletenessDefinitions.Count == 0 ? null : new JsonCompleteness
            {
                CompletenessDefinitions = live.CompletenessDefinitions.Select(d => new JsonCompletenessDefinition
                {
                    Id = d.Id,
                    Name = LocaleStringToJson(d.Name, langOrder),
                    EntityTypeId = d.EntityTypeId,
                    GroupIds = d.Groups.Select(g => g.Id).ToList(),
                }).ToList(),
                CompletenessGroups = live.CompletenessDefinitions
                    .SelectMany(d => d.Groups.Select(g => new JsonCompletenessGroup
                    {
                        Id = g.Id,
                        Name = LocaleStringToJson(g.Name, langOrder),
                        Weight = g.Weight,
                        SortOrder = g.SortOrder,
                        CompletenessDefinitionId = g.DefinitionId,
                        RuleIds = g.Rules.Select(r => r.Id).ToList(),
                    }))
                    .ToList(),
                CompletenessBusinessRules = live.CompletenessDefinitions
                    .SelectMany(d => d.Groups.SelectMany(g => g.Rules.Select(r => new JsonCompletenessBusinessRule
                    {
                        Id = r.Id,
                        Name = LocaleStringToJson(r.Name, langOrder),
                        Type = r.Type,
                        Weight = r.Weight,
                        SortOrder = r.SortOrder,
                        GroupIds = [g.Id],
                        RuleSettings = r.Settings.Select(s => new JsonCompletenessRuleSetting
                        {
                            Id = s.Id,
                            BusinessRuleId = s.BusinessRuleId,
                            Type = s.Type,
                            Key = s.Key,
                            Value = s.Value,
                        }).ToList(),
                    })))
                    .ToList(),
            },
        };

        return result;
    }

    private static JsonFieldType FieldTypeToJson(LiveFieldType f, IReadOnlyList<string> langOrder) => new()
    {
        Id = f.Id,
        EntityTypeId = f.EntityTypeId,
        Name = LocaleStringToJson(f.Name, langOrder),
        Description = LocaleStringToJson(f.Description, langOrder),
        DataType = DatatypeMapper.ToInriver(f.DataType),
        Mandatory = f.Mandatory,
        Unique = f.Unique,
        Index = f.Index,
        CategoryId = f.CategoryId,
        DefaultValue = f.DefaultValue,
        Hidden = f.Hidden,
        ReadOnly = f.ReadOnly,
        IsDisplayName = f.IsDisplayName,
        IsDisplayDescription = f.IsDisplayDescription,
        Settings = f.Settings.Count == 0 ? null : new Dictionary<string, string>(f.Settings, StringComparer.Ordinal),
        ExcludeFromDefaultView = f.ExcludeFromDefaultView,
        CvlId = f.CvlId,
        Multivalue = f.MultiValue,
        TrackChanges = f.TrackChanges,
        ExpressionSupport = f.ExpressionSupport,
    };

    private static JsonLocaleString? LocaleStringToJson(LocaleString? ls, IReadOnlyList<string> langOrder)
    {
        if (ls is null) return null;

        // Use an ordered dictionary: insert configured locales first (in master-first order), then
        // any extra locales the LiveModel happens to carry that langOrder didn't enumerate.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var iso in langOrder)
        {
            if (ls.Values.TryGetValue(iso, out var v) && !string.IsNullOrEmpty(v))
                map[iso] = v;
        }
        foreach (var kvp in ls.Values.Where(kvp => !string.IsNullOrEmpty(kvp.Value) && !map.ContainsKey(kvp.Key)))
        {
            map[kvp.Key] = kvp.Value;
        }

        if (map.Count == 0)
        {
            // Nothing matched the requested order — fall back to the LocaleString's default text
            // attributed to the master language (or "en" when langOrder is empty).
            if (string.IsNullOrEmpty(ls.DefaultValue)) return null;
            var master = langOrder.Count > 0 ? langOrder[0] : "en";
            map[master] = ls.DefaultValue;
        }

        return new JsonLocaleString { StringMap = map };
    }

    private static JsonElement CvlValueToJsonElement(LocaleString? ls, IReadOnlyList<string> langOrder)
    {
        var json = LocaleStringToJson(ls, langOrder);
        if (json is null) return JsonSerializer.SerializeToElement(new { StringMap = new Dictionary<string, string>() });
        return JsonSerializer.SerializeToElement(json);
    }

}
