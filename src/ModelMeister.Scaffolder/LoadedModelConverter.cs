using System.Text.Json;
using ModelMeister.Inriver.Mapping;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Scaffolder;

/// <summary>
/// Adapts a reflection-derived <see cref="LoadedModel"/> into the <see cref="InriverModelJson"/>
/// shape so the same downstream tooling (workbook export, JSON serialization) that runs against a
/// live snapshot also runs against the C# model project. Sibling of <see cref="LiveModelConverter"/>.
/// </summary>
public static class LoadedModelConverter
{
    public static InriverModelJson ToJsonModel(LoadedModel loaded)
    {
        var langOrder = loaded.Languages.Count > 0
            ? loaded.Languages.Select(l => l.IsoCode).ToList()
            : new List<string> { "en" };

        var cvlIdByClrType = loaded.Cvls.ToDictionary(c => c.ClrType, c => c.CvlId);
        var categoryIdByClrType = loaded.Categories.ToDictionary(c => c.ClrType, c => c.CategoryId);

        var entityTypes = loaded.EntityTypes.Select(e => new JsonEntityType
        {
            Id = e.EntityTypeId,
            Name = LocaleStringToJson(e.Name, langOrder),
            IsLinkEntityType = e.IsLinkEntityType,
            GetDisplayNameFieldTypeId = e.Fields.FirstOrDefault(f => f.Field.IsDisplayName)?.Id,
            GetDisplayDescriptionFieldTypeId = e.Fields.FirstOrDefault(f => f.Field.IsDisplayDescription)?.Id,
            FieldTypes = e.Fields.Select(f => FieldTypeToJson(f, langOrder, cvlIdByClrType, categoryIdByClrType)).ToList(),
        }).ToList();

        return new InriverModelJson
        {
            Languages = langOrder.Select(iso => new JsonLanguage { Name = iso }).ToList(),
            Categories = loaded.Categories.Select(c => new JsonCategory
            {
                Id = c.CategoryId,
                Name = LocaleStringToJson(c.Name, langOrder),
                Index = c.Index,
            }).ToList(),
            LinkTypes = loaded.LinkTypes.Select(l => new JsonLinkType
            {
                Id = l.LinkTypeId,
                SourceEntityTypeId = l.SourceEntityTypeId,
                TargetEntityTypeId = l.TargetEntityTypeId,
                LinkEntityTypeId = l.LinkEntityTypeId,
                Index = l.Index,
                SourceName = LocaleStringToJson(l.SourceName, langOrder),
                TargetName = LocaleStringToJson(l.TargetName, langOrder),
            }).ToList(),
            FieldSets = loaded.Fieldsets.Select(f => new JsonFieldSet
            {
                Id = f.FieldsetId,
                EntityTypeId = f.EntityTypeId,
                Name = LocaleStringToJson(f.Name, langOrder),
                Description = LocaleStringToJson(f.Description, langOrder),
                FieldTypes = FieldsetMembers(loaded, f),
            }).ToList(),
            EntityTypes = entityTypes,
            FieldTypes = entityTypes.SelectMany(e => e.FieldTypes ?? []).ToList(),
            Cvls = loaded.Cvls.Select(c => new JsonCvl
            {
                Id = c.CvlId,
                DataType = DatatypeMapper.CvlToInriver(c.DataType),
                ParentId = c.ParentCvlClrType is not null && cvlIdByClrType.TryGetValue(c.ParentCvlClrType, out var pid)
                    ? pid
                    : c.ParentCvlId,
                CustomValueList = c.CustomValueList,
                Activated = true,
            }).ToList(),
            CvlValues = loaded.Cvls
                .SelectMany(c => c.Values.Select((v, i) => new JsonCvlValue
                {
                    Id = i,
                    CvlId = c.CvlId,
                    Key = v.Key,
                    Value = CvlValueToJsonElement(v.Value, langOrder),
                    Index = v.Index,
                    ParentKey = v.Parent,
                    Deactivated = v.Deactivated,
                }))
                .ToList(),
            Security = loaded.Roles.Count == 0 ? null : new JsonSecurity
            {
                Roles = loaded.Roles.Select((r, i) => new JsonRole
                {
                    Id = i,
                    Name = r.Name,
                    Description = r.Description,
                    Permissions = r.PermissionNames.Select((n, j) => new JsonPermission
                    {
                        Id = j,
                        Name = n,
                    }).ToList(),
                }).ToList(),
            },
        };
    }

    private static List<string> FieldsetMembers(LoadedModel loaded, LoadedFieldset fs)
    {
        return loaded.EntityTypes
            .Where(e => e.EntityTypeId == fs.EntityTypeId)
            .SelectMany(e => e.Fields)
            .Where(f => f.Field.Fieldsets.Any(t => t == fs.ClrType))
            .Select(f => f.Id)
            .ToList();
    }

    private static JsonFieldType FieldTypeToJson(
        LoadedField lf,
        IReadOnlyList<string> langOrder,
        IReadOnlyDictionary<Type, string> cvlIdByClrType,
        IReadOnlyDictionary<Type, string> categoryIdByClrType)
    {
        var ff = lf.Field;
        var cvlId = ff.Cvl is not null && cvlIdByClrType.TryGetValue(ff.Cvl, out var cid) ? cid : null;
        var categoryId = ff.Category is not null && categoryIdByClrType.TryGetValue(ff.Category, out var aid) ? aid : null;

        return new JsonFieldType
        {
            Id = lf.Id,
            EntityTypeId = lf.EntityTypeId,
            Name = LocaleStringToJson(lf.Name, langOrder),
            Description = LocaleStringToJson(ff.Description, langOrder),
            DataType = DatatypeMapper.ToInriver(lf.DataType),
            Mandatory = ff.Mandatory,
            Unique = ff.Unique,
            Index = ff.Index ?? 0,
            CategoryId = categoryId,
            DefaultValue = ff.DefaultValue?.ToString(),
            Hidden = ff.Hidden,
            ReadOnly = ff.ReadOnly,
            IsDisplayName = ff.IsDisplayName,
            IsDisplayDescription = ff.IsDisplayDescription,
            Settings = ff.Settings.Count == 0 ? null : new Dictionary<string, string>(ff.Settings, StringComparer.Ordinal),
            ExcludeFromDefaultView = ff.ExcludeFromDefaultView ?? false,
            CvlId = cvlId,
            Multivalue = ff.MultiValue,
            TrackChanges = ff.TrackChanges ?? true,
            ExpressionSupport = ff.SupportsExpression,
        };
    }

    private static JsonLocaleString? LocaleStringToJson(LocaleString? ls, IReadOnlyList<string> langOrder)
    {
        if (ls is null) return null;

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
