using System.Text.Json;
using ClosedXML.Excel;
using ModelMeister.Scaffolder;

namespace ModelMeister.Excel;

/// <summary>
/// Round-trip Excel workbook for an entire inriver model. Each concept gets a sheet; locale strings
/// expand horizontally as <c>Name[en]</c>, <c>Name[sv]</c>, … columns. The shape mirrors
/// <see cref="InriverModelJson"/> exactly so a <c>Save → Load</c> cycle is value-preserving.
/// </summary>
public static class ModelWorkbook
{
    // Sheet names — kept centralised so import/export agree.
    public const string SheetLanguages = "Languages";
    public const string SheetCategories = "Categories";
    public const string SheetEntityTypes = "EntityTypes";
    public const string SheetFieldTypes = "FieldTypes";
    public const string SheetFieldSets = "FieldSets";
    public const string SheetCvls = "CVLs";
    public const string SheetCvlValues = "CvlValues";
    public const string SheetLinkTypes = "LinkTypes";
    public const string SheetRoles = "Roles";
    public const string SheetRestrictedFieldPermissions = "RestrictedPermissions";
    public const string SheetCompletenessDefinitions = "CompletenessDefinitions";
    public const string SheetCompletenessGroups = "CompletenessGroups";
    public const string SheetCompletenessRules = "CompletenessRules";
    public const string SheetCompletenessSettings = "CompletenessSettings";
    public const string SheetReadme = "_Readme";

    /// <summary>Save <paramref name="model"/> to <paramref name="path"/>.</summary>
    public static void Save(InriverModelJson model, string path)
    {
        using var wb = Build(model);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    /// <summary>Load a workbook produced by <see cref="Save"/> back into <see cref="InriverModelJson"/>.</summary>
    public static InriverModelJson Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    /// <summary>Build the workbook in-memory (caller is responsible for disposing/saving).</summary>
    public static XLWorkbook Build(InriverModelJson model)
    {
        var wb = new XLWorkbook();
        var languages = model.Languages.Select(l => l.Name).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (languages.Count == 0) languages.Add("en");

        WriteReadme(wb, model);
        WriteLanguages(wb, model);
        WriteCategories(wb, model, languages);
        WriteEntityTypes(wb, model, languages);
        WriteFieldTypes(wb, model, languages);
        WriteFieldSets(wb, model, languages);
        WriteCvls(wb, model);
        WriteCvlValues(wb, model, languages);
        WriteLinkTypes(wb, model, languages);
        WriteRoles(wb, model);
        WriteRestrictedFieldPermissions(wb, model);
        WriteCompleteness(wb, model, languages);

        foreach (var ws in wb.Worksheets)
        {
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, 50, 8, 60);
            if (ws.Name == SheetReadme) continue;
            StyleAsTable(ws);
        }
        return wb;
    }

    /// <summary>
    /// Wraps the sheet's used range as an Excel Table so the freshly-opened workbook has
    /// AutoFilter dropdowns and banded-row styling on every data sheet. Header-only sheets
    /// fall back to plain AutoFilter — ClosedXML's XLTable needs at least one data row.
    /// </summary>
    static void StyleAsTable(IXLWorksheet ws)
    {
        var range = ws.RangeUsed();
        if (range is null) return;
        if (range.RowCount() < 2)
        {
            range.SetAutoFilter();
            return;
        }
        var tableName = "tbl_" + new string(ws.Name.Where(char.IsLetterOrDigit).ToArray());
        var table = range.CreateTable(tableName);
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;
    }

    static void WriteReadme(IXLWorkbook wb, InriverModelJson model)
    {
        var ws = wb.AddWorksheet(SheetReadme);
        ws.Cell("A1").Value = "ModelMeister model workbook";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;

        var rows = new (string K, string V)[]
        {
            ("Customer", model.CustomerName ?? ""),
            ("Version", model.Version ?? ""),
            ("Db version", model.DbVersion ?? ""),
            ("Generated", DateTime.UtcNow.ToString("u")),
            ("", ""),
            ("Languages", string.Join(", ", model.Languages.Select(l => l.Name))),
            ("Entity types", model.EntityTypes.Count.ToString()),
            ("Field types", model.FieldTypes.Count.ToString()),
            ("Field sets", model.FieldSets.Count.ToString()),
            ("Categories", model.Categories.Count.ToString()),
            ("CVLs", model.Cvls.Count.ToString()),
            ("CVL values", model.CvlValues.Count.ToString()),
            ("Link types", model.LinkTypes.Count.ToString()),
            ("Roles", (model.Security?.Roles?.Count ?? 0).ToString()),
            ("", ""),
            ("How to edit", "Each sheet maps to one concept. Locale strings expand as Name[en], Name[sv], etc."),
            ("How to filter", "Every data sheet is an Excel Table — use the dropdowns in row 1 to filter and sort."),
            ("How to import", "modelmeister excel scaffold --excel <path>  OR  Tools → From workbook in the UI."),
            ("CVL value sheets", "See the dedicated CVL values workbook produced by 'modelmeister cvl export' for per-CVL editing."),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 3, 1).Value = rows[i].K;
            ws.Cell(i + 3, 1).Style.Font.Bold = true;
            ws.Cell(i + 3, 2).Value = rows[i].V;
        }
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 80;
    }

    // ---------------------------------------------------------- write helpers

    static void WriteLanguages(IXLWorkbook wb, InriverModelJson model)
    {
        var ws = wb.AddWorksheet(SheetLanguages);
        ws.Cell(1, 1).Value = "IsoCode";
        XlIo.StyleHeader(ws.Row(1));
        var r = 2;
        foreach (var l in model.Languages) ws.Cell(r++, 1).Value = l.Name;
    }

    static void WriteCategories(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var ws = wb.AddWorksheet(SheetCategories);
        var cols = new List<string> { "Id", "Index" };
        cols.AddRange(langs.Select(l => $"Name[{l}]"));
        WriteHeader(ws, cols);
        var r = 2;
        foreach (var c in model.Categories.OrderBy(x => x.Index).ThenBy(x => x.Id))
        {
            var col = 1;
            ws.Cell(r, col++).Value = c.Id;
            ws.Cell(r, col++).Value = c.Index;
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(c.Name, l);
            r++;
        }
    }

    static void WriteEntityTypes(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var ws = wb.AddWorksheet(SheetEntityTypes);
        var cols = new List<string> { "Id", "IsLinkEntityType", "DisplayNameFieldId", "DisplayDescriptionFieldId" };
        cols.AddRange(langs.Select(l => $"Name[{l}]"));
        WriteHeader(ws, cols);
        var r = 2;
        foreach (var e in model.EntityTypes.OrderBy(x => x.Id))
        {
            var col = 1;
            ws.Cell(r, col++).Value = e.Id;
            ws.Cell(r, col++).Value = e.IsLinkEntityType;
            ws.Cell(r, col++).Value = e.GetDisplayNameFieldTypeId ?? string.Empty;
            ws.Cell(r, col++).Value = e.GetDisplayDescriptionFieldTypeId ?? string.Empty;
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(e.Name, l);
            r++;
        }
    }

    static void WriteFieldTypes(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var ws = wb.AddWorksheet(SheetFieldTypes);
        var cols = new List<string>
        {
            "Id", "EntityTypeId", "DataType", "Index", "CategoryId", "CvlId",
            "Mandatory", "Unique", "Multivalue", "Hidden", "ReadOnly",
            "IsDisplayName", "IsDisplayDescription", "TrackChanges", "ExcludeFromDefaultView",
            "ExpressionSupport", "DefaultValue", "Settings",
        };
        cols.AddRange(langs.Select(l => $"Name[{l}]"));
        cols.AddRange(langs.Select(l => $"Description[{l}]"));
        WriteHeader(ws, cols);

        var r = 2;
        foreach (var f in model.FieldTypes.OrderBy(x => x.EntityTypeId).ThenBy(x => x.Index).ThenBy(x => x.Id))
        {
            var col = 1;
            ws.Cell(r, col++).Value = f.Id;
            ws.Cell(r, col++).Value = f.EntityTypeId;
            ws.Cell(r, col++).Value = f.DataType;
            ws.Cell(r, col++).Value = f.Index;
            ws.Cell(r, col++).Value = f.CategoryId ?? string.Empty;
            ws.Cell(r, col++).Value = f.CvlId ?? string.Empty;
            ws.Cell(r, col++).Value = f.Mandatory;
            ws.Cell(r, col++).Value = f.Unique;
            ws.Cell(r, col++).Value = f.Multivalue;
            ws.Cell(r, col++).Value = f.Hidden;
            ws.Cell(r, col++).Value = f.ReadOnly;
            ws.Cell(r, col++).Value = f.IsDisplayName;
            ws.Cell(r, col++).Value = f.IsDisplayDescription;
            ws.Cell(r, col++).Value = f.TrackChanges;
            ws.Cell(r, col++).Value = f.ExcludeFromDefaultView;
            ws.Cell(r, col++).Value = f.ExpressionSupport;
            ws.Cell(r, col++).Value = f.DefaultValue ?? string.Empty;
            ws.Cell(r, col++).Value = EncodeSettings(f.Settings);
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(f.Name, l);
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(f.Description, l);
            r++;
        }
    }

    static void WriteFieldSets(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var ws = wb.AddWorksheet(SheetFieldSets);
        var cols = new List<string> { "Id", "EntityTypeId", "FieldTypes" };
        cols.AddRange(langs.Select(l => $"Name[{l}]"));
        cols.AddRange(langs.Select(l => $"Description[{l}]"));
        WriteHeader(ws, cols);
        var r = 2;
        foreach (var fs in model.FieldSets.OrderBy(x => x.EntityTypeId).ThenBy(x => x.Id))
        {
            var col = 1;
            ws.Cell(r, col++).Value = fs.Id;
            ws.Cell(r, col++).Value = fs.EntityTypeId;
            ws.Cell(r, col++).Value = fs.FieldTypes is null ? string.Empty : string.Join(";", fs.FieldTypes);
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(fs.Name, l);
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(fs.Description, l);
            r++;
        }
    }

    static void WriteCvls(IXLWorkbook wb, InriverModelJson model)
    {
        var ws = wb.AddWorksheet(SheetCvls);
        WriteHeader(ws, new[] { "Id", "DataType", "ParentId", "CustomValueList", "Activated" });
        var r = 2;
        foreach (var c in model.Cvls.OrderBy(x => x.Id))
        {
            ws.Cell(r, 1).Value = c.Id;
            ws.Cell(r, 2).Value = c.DataType;
            ws.Cell(r, 3).Value = c.ParentId ?? string.Empty;
            ws.Cell(r, 4).Value = c.CustomValueList;
            ws.Cell(r, 5).Value = c.Activated ?? false;
            r++;
        }
    }

    static void WriteCvlValues(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var ws = wb.AddWorksheet(SheetCvlValues);
        var cols = new List<string> { "CvlId", "Key", "Index", "ParentKey", "Deactivated", "Value" };
        cols.AddRange(langs.Select(l => $"Value[{l}]"));
        WriteHeader(ws, cols);

        var r = 2;
        foreach (var v in model.CvlValues.OrderBy(x => x.CvlId).ThenBy(x => x.Index).ThenBy(x => x.Key))
        {
            var col = 1;
            ws.Cell(r, col++).Value = v.CvlId;
            ws.Cell(r, col++).Value = v.Key;
            ws.Cell(r, col++).Value = v.Index;
            ws.Cell(r, col++).Value = v.ParentKey ?? string.Empty;
            ws.Cell(r, col++).Value = v.Deactivated;
            ws.Cell(r, col++).Value = ScalarCvlValue(v.Value);
            foreach (var l in langs) ws.Cell(r, col++).Value = LocCvlValue(v.Value, l);
            r++;
        }
    }

    static void WriteLinkTypes(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var ws = wb.AddWorksheet(SheetLinkTypes);
        var cols = new List<string>
        {
            "Id", "SourceEntityTypeId", "TargetEntityTypeId", "LinkEntityTypeId", "Index",
        };
        cols.AddRange(langs.Select(l => $"SourceName[{l}]"));
        cols.AddRange(langs.Select(l => $"TargetName[{l}]"));
        WriteHeader(ws, cols);

        var r = 2;
        foreach (var lt in model.LinkTypes.OrderBy(x => x.Id))
        {
            var col = 1;
            ws.Cell(r, col++).Value = lt.Id;
            ws.Cell(r, col++).Value = lt.SourceEntityTypeId;
            ws.Cell(r, col++).Value = lt.TargetEntityTypeId;
            ws.Cell(r, col++).Value = lt.LinkEntityTypeId ?? string.Empty;
            ws.Cell(r, col++).Value = lt.Index;
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(lt.SourceName, l);
            foreach (var l in langs) ws.Cell(r, col++).Value = LocStr(lt.TargetName, l);
            r++;
        }
    }

    static void WriteRoles(IXLWorkbook wb, InriverModelJson model)
    {
        var ws = wb.AddWorksheet(SheetRoles);
        WriteHeader(ws, new[] { "Id", "Name", "Description", "Permissions" });
        var r = 2;
        foreach (var role in model.Security?.Roles ?? new List<JsonRole>())
        {
            ws.Cell(r, 1).Value = role.Id;
            ws.Cell(r, 2).Value = role.Name;
            ws.Cell(r, 3).Value = role.Description ?? string.Empty;
            ws.Cell(r, 4).Value = role.Permissions is null
                ? string.Empty
                : string.Join(";", role.Permissions.Select(p => p.Name));
            r++;
        }
    }

    static void WriteRestrictedFieldPermissions(IXLWorkbook wb, InriverModelJson model)
    {
        var ws = wb.AddWorksheet(SheetRestrictedFieldPermissions);
        WriteHeader(ws, new[] { "Id", "RoleId", "RestrictionType", "EntityTypeId", "FieldTypeId", "CategoryId" });
        var r = 2;
        foreach (var p in model.Security?.RestrictedFieldPermissions ?? new List<JsonRestrictedFieldPermission>())
        {
            ws.Cell(r, 1).Value = p.Id;
            ws.Cell(r, 2).Value = p.RoleId;
            ws.Cell(r, 3).Value = p.RestrictionType ?? string.Empty;
            ws.Cell(r, 4).Value = p.EntityTypeId ?? string.Empty;
            ws.Cell(r, 5).Value = p.FieldTypeId ?? string.Empty;
            ws.Cell(r, 6).Value = p.CategoryId ?? string.Empty;
            r++;
        }
    }

    static void WriteCompleteness(IXLWorkbook wb, InriverModelJson model, IReadOnlyList<string> langs)
    {
        var c = model.Completeness ?? new JsonCompleteness();

        var defs = wb.AddWorksheet(SheetCompletenessDefinitions);
        var defCols = new List<string> { "Id", "EntityTypeId", "GroupIds" };
        defCols.AddRange(langs.Select(l => $"Name[{l}]"));
        WriteHeader(defs, defCols);
        var r = 2;
        foreach (var d in c.CompletenessDefinitions ?? new List<JsonCompletenessDefinition>())
        {
            var col = 1;
            defs.Cell(r, col++).Value = d.Id;
            defs.Cell(r, col++).Value = d.EntityTypeId;
            defs.Cell(r, col++).Value = d.GroupIds is null ? string.Empty : string.Join(";", d.GroupIds);
            foreach (var l in langs) defs.Cell(r, col++).Value = LocStr(d.Name, l);
            r++;
        }

        var groups = wb.AddWorksheet(SheetCompletenessGroups);
        var gCols = new List<string> { "Id", "DefinitionId", "Weight", "SortOrder", "RuleIds" };
        gCols.AddRange(langs.Select(l => $"Name[{l}]"));
        WriteHeader(groups, gCols);
        r = 2;
        foreach (var g in c.CompletenessGroups ?? new List<JsonCompletenessGroup>())
        {
            var col = 1;
            groups.Cell(r, col++).Value = g.Id;
            groups.Cell(r, col++).Value = g.CompletenessDefinitionId;
            groups.Cell(r, col++).Value = g.Weight;
            groups.Cell(r, col++).Value = g.SortOrder;
            groups.Cell(r, col++).Value = g.RuleIds is null ? string.Empty : string.Join(";", g.RuleIds);
            foreach (var l in langs) groups.Cell(r, col++).Value = LocStr(g.Name, l);
            r++;
        }

        var rules = wb.AddWorksheet(SheetCompletenessRules);
        var rCols = new List<string> { "Id", "Type", "Weight", "SortOrder", "GroupIds" };
        rCols.AddRange(langs.Select(l => $"Name[{l}]"));
        WriteHeader(rules, rCols);
        r = 2;
        foreach (var rule in c.CompletenessBusinessRules ?? new List<JsonCompletenessBusinessRule>())
        {
            var col = 1;
            rules.Cell(r, col++).Value = rule.Id;
            rules.Cell(r, col++).Value = rule.Type;
            rules.Cell(r, col++).Value = rule.Weight;
            rules.Cell(r, col++).Value = rule.SortOrder;
            rules.Cell(r, col++).Value = rule.GroupIds is null ? string.Empty : string.Join(";", rule.GroupIds);
            foreach (var l in langs) rules.Cell(r, col++).Value = LocStr(rule.Name, l);
            r++;
        }

        var settings = wb.AddWorksheet(SheetCompletenessSettings);
        WriteHeader(settings, new[] { "Id", "BusinessRuleId", "Type", "Key", "Value" });
        r = 2;
        foreach (var rule in c.CompletenessBusinessRules ?? new List<JsonCompletenessBusinessRule>())
            foreach (var s in rule.RuleSettings ?? new List<JsonCompletenessRuleSetting>())
            {
                settings.Cell(r, 1).Value = s.Id;
                settings.Cell(r, 2).Value = s.BusinessRuleId;
                settings.Cell(r, 3).Value = s.Type;
                settings.Cell(r, 4).Value = s.Key;
                settings.Cell(r, 5).Value = s.Value ?? string.Empty;
                r++;
            }
    }

    // ---------------------------------------------------------- parse

    public static InriverModelJson Parse(IXLWorkbook wb)
    {
        var model = new InriverModelJson();
        var languages = ReadColumnList(wb, SheetLanguages, "IsoCode");
        model.Languages = languages.Select(l => new JsonLanguage { Name = l }).ToList();
        if (model.Languages.Count == 0) model.Languages.Add(new JsonLanguage { Name = "en" });

        ReadCategories(wb, model);
        ReadEntityTypes(wb, model);
        ReadFieldTypes(wb, model);
        ReadFieldSets(wb, model);
        // Mirror flat lists into the per-EntityType nested lists the scaffolder consumes.
        var fieldsByEntity = model.FieldTypes.GroupBy(f => f.EntityTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var fsByEntity = model.FieldSets.GroupBy(fs => fs.EntityTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var e in model.EntityTypes)
        {
            if (fieldsByEntity.TryGetValue(e.Id, out var fs)) e.FieldTypes = fs;
            if (fsByEntity.TryGetValue(e.Id, out var sets)) e.FieldSets = sets;
        }
        ReadCvls(wb, model);
        ReadCvlValues(wb, model);
        ReadLinkTypes(wb, model);
        ReadRoles(wb, model);
        ReadRestrictedFieldPermissions(wb, model);
        ReadCompleteness(wb, model);

        return model;
    }

    static void ReadCategories(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetCategories, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        foreach (var row in DataRows(ws))
        {
            var id = XlIo.ReadString(row, hdr, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            model.Categories.Add(new JsonCategory
            {
                Id = id,
                Index = XlIo.ReadInt(row, hdr, "Index"),
                Name = ReadLocale(row, hdr, "Name"),
            });
        }
    }

    static void ReadEntityTypes(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetEntityTypes, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        foreach (var row in DataRows(ws))
        {
            var id = XlIo.ReadString(row, hdr, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            model.EntityTypes.Add(new JsonEntityType
            {
                Id = id,
                IsLinkEntityType = XlIo.ReadBool(row, hdr, "IsLinkEntityType"),
                GetDisplayNameFieldTypeId = NullIfEmpty(XlIo.ReadString(row, hdr, "DisplayNameFieldId")),
                GetDisplayDescriptionFieldTypeId = NullIfEmpty(XlIo.ReadString(row, hdr, "DisplayDescriptionFieldId")),
                Name = ReadLocale(row, hdr, "Name"),
            });
        }
    }

    static void ReadFieldTypes(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetFieldTypes, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        foreach (var row in DataRows(ws))
        {
            var id = XlIo.ReadString(row, hdr, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            model.FieldTypes.Add(new JsonFieldType
            {
                Id = id,
                EntityTypeId = XlIo.ReadString(row, hdr, "EntityTypeId"),
                DataType = XlIo.ReadString(row, hdr, "DataType"),
                Index = XlIo.ReadInt(row, hdr, "Index"),
                CategoryId = NullIfEmpty(XlIo.ReadString(row, hdr, "CategoryId")),
                CvlId = NullIfEmpty(XlIo.ReadString(row, hdr, "CvlId")),
                Mandatory = XlIo.ReadBool(row, hdr, "Mandatory"),
                Unique = XlIo.ReadBool(row, hdr, "Unique"),
                Multivalue = XlIo.ReadBool(row, hdr, "Multivalue"),
                Hidden = XlIo.ReadBool(row, hdr, "Hidden"),
                ReadOnly = XlIo.ReadBool(row, hdr, "ReadOnly"),
                IsDisplayName = XlIo.ReadBool(row, hdr, "IsDisplayName"),
                IsDisplayDescription = XlIo.ReadBool(row, hdr, "IsDisplayDescription"),
                TrackChanges = XlIo.ReadBool(row, hdr, "TrackChanges"),
                ExcludeFromDefaultView = XlIo.ReadBool(row, hdr, "ExcludeFromDefaultView"),
                ExpressionSupport = XlIo.ReadBool(row, hdr, "ExpressionSupport"),
                DefaultValue = NullIfEmpty(XlIo.ReadString(row, hdr, "DefaultValue")),
                Settings = DecodeSettings(XlIo.ReadString(row, hdr, "Settings")),
                Name = ReadLocale(row, hdr, "Name"),
                Description = ReadLocale(row, hdr, "Description"),
            });
        }
    }

    static void ReadFieldSets(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetFieldSets, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        foreach (var row in DataRows(ws))
        {
            var id = XlIo.ReadString(row, hdr, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            var fields = XlIo.ReadString(row, hdr, "FieldTypes");
            model.FieldSets.Add(new JsonFieldSet
            {
                Id = id,
                EntityTypeId = XlIo.ReadString(row, hdr, "EntityTypeId"),
                FieldTypes = SplitSemiList(fields),
                Name = ReadLocale(row, hdr, "Name"),
                Description = ReadLocale(row, hdr, "Description"),
            });
        }
    }

    static void ReadCvls(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetCvls, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        foreach (var row in DataRows(ws))
        {
            var id = XlIo.ReadString(row, hdr, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            model.Cvls.Add(new JsonCvl
            {
                Id = id,
                DataType = XlIo.ReadString(row, hdr, "DataType"),
                ParentId = NullIfEmpty(XlIo.ReadString(row, hdr, "ParentId")),
                CustomValueList = XlIo.ReadBool(row, hdr, "CustomValueList"),
                Activated = XlIo.ReadBool(row, hdr, "Activated"),
            });
        }
    }

    static void ReadCvlValues(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetCvlValues, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        var id = 1;
        foreach (var row in DataRows(ws))
        {
            var cvlId = XlIo.ReadString(row, hdr, "CvlId");
            var key = XlIo.ReadString(row, hdr, "Key");
            if (string.IsNullOrEmpty(cvlId) || string.IsNullOrEmpty(key)) continue;

            var locale = ReadLocale(row, hdr, "Value");
            var scalar = XlIo.ReadString(row, hdr, "Value");
            var value = locale is not null && !locale.IsEmpty()
                ? JsonSerializer.SerializeToElement(locale, InriverModelJson.Options)
                : JsonSerializer.SerializeToElement(scalar);

            model.CvlValues.Add(new JsonCvlValue
            {
                Id = id++,
                CvlId = cvlId,
                Key = key,
                Index = XlIo.ReadInt(row, hdr, "Index"),
                ParentKey = NullIfEmpty(XlIo.ReadString(row, hdr, "ParentKey")),
                Deactivated = XlIo.ReadBool(row, hdr, "Deactivated"),
                Value = value,
            });
        }
    }

    static void ReadLinkTypes(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetLinkTypes, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        foreach (var row in DataRows(ws))
        {
            var id = XlIo.ReadString(row, hdr, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            model.LinkTypes.Add(new JsonLinkType
            {
                Id = id,
                SourceEntityTypeId = XlIo.ReadString(row, hdr, "SourceEntityTypeId"),
                TargetEntityTypeId = XlIo.ReadString(row, hdr, "TargetEntityTypeId"),
                LinkEntityTypeId = NullIfEmpty(XlIo.ReadString(row, hdr, "LinkEntityTypeId")),
                Index = XlIo.ReadInt(row, hdr, "Index"),
                SourceName = ReadLocale(row, hdr, "SourceName"),
                TargetName = ReadLocale(row, hdr, "TargetName"),
            });
        }
    }

    static void ReadRoles(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetRoles, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        var sec = model.Security ??= new JsonSecurity();
        sec.Roles ??= new List<JsonRole>();
        foreach (var row in DataRows(ws))
        {
            var name = XlIo.ReadString(row, hdr, "Name");
            if (string.IsNullOrEmpty(name)) continue;
            var perms = SplitSemiList(XlIo.ReadString(row, hdr, "Permissions"));
            sec.Roles.Add(new JsonRole
            {
                Id = XlIo.ReadInt(row, hdr, "Id"),
                Name = name,
                Description = NullIfEmpty(XlIo.ReadString(row, hdr, "Description")),
                Permissions = perms?.Select(p => new JsonPermission { Name = p }).ToList(),
            });
        }
    }

    static void ReadRestrictedFieldPermissions(IXLWorkbook wb, InriverModelJson model)
    {
        if (!TryGetSheet(wb, SheetRestrictedFieldPermissions, out var ws)) return;
        var hdr = XlIo.HeaderMap(ws);
        var sec = model.Security ??= new JsonSecurity();
        sec.RestrictedFieldPermissions ??= new List<JsonRestrictedFieldPermission>();
        foreach (var row in DataRows(ws))
        {
            if (XlIo.ReadInt(row, hdr, "RoleId") == 0 && string.IsNullOrEmpty(XlIo.ReadString(row, hdr, "FieldTypeId"))) continue;
            sec.RestrictedFieldPermissions.Add(new JsonRestrictedFieldPermission
            {
                Id = XlIo.ReadInt(row, hdr, "Id"),
                RoleId = XlIo.ReadInt(row, hdr, "RoleId"),
                RestrictionType = NullIfEmpty(XlIo.ReadString(row, hdr, "RestrictionType")),
                EntityTypeId = NullIfEmpty(XlIo.ReadString(row, hdr, "EntityTypeId")),
                FieldTypeId = NullIfEmpty(XlIo.ReadString(row, hdr, "FieldTypeId")),
                CategoryId = NullIfEmpty(XlIo.ReadString(row, hdr, "CategoryId")),
            });
        }
    }

    static void ReadCompleteness(IXLWorkbook wb, InriverModelJson model)
    {
        var c = model.Completeness ??= new JsonCompleteness();
        c.CompletenessDefinitions ??= new();
        c.CompletenessGroups ??= new();
        c.CompletenessBusinessRules ??= new();

        if (TryGetSheet(wb, SheetCompletenessDefinitions, out var defs))
        {
            var hdr = XlIo.HeaderMap(defs);
            foreach (var row in DataRows(defs))
            {
                var id = XlIo.ReadInt(row, hdr, "Id");
                if (id == 0) continue;
                c.CompletenessDefinitions.Add(new JsonCompletenessDefinition
                {
                    Id = id,
                    EntityTypeId = XlIo.ReadString(row, hdr, "EntityTypeId"),
                    GroupIds = SplitSemiList(XlIo.ReadString(row, hdr, "GroupIds"))?.Select(int.Parse).ToList(),
                    Name = ReadLocale(row, hdr, "Name"),
                });
            }
        }
        if (TryGetSheet(wb, SheetCompletenessGroups, out var groups))
        {
            var hdr = XlIo.HeaderMap(groups);
            foreach (var row in DataRows(groups))
            {
                var id = XlIo.ReadInt(row, hdr, "Id");
                if (id == 0) continue;
                c.CompletenessGroups.Add(new JsonCompletenessGroup
                {
                    Id = id,
                    CompletenessDefinitionId = XlIo.ReadInt(row, hdr, "DefinitionId"),
                    Weight = XlIo.ReadInt(row, hdr, "Weight"),
                    SortOrder = XlIo.ReadInt(row, hdr, "SortOrder"),
                    RuleIds = SplitSemiList(XlIo.ReadString(row, hdr, "RuleIds"))?.Select(int.Parse).ToList(),
                    Name = ReadLocale(row, hdr, "Name"),
                });
            }
        }
        Dictionary<int, JsonCompletenessBusinessRule>? rulesById = null;
        if (TryGetSheet(wb, SheetCompletenessRules, out var rules))
        {
            var hdr = XlIo.HeaderMap(rules);
            rulesById = new Dictionary<int, JsonCompletenessBusinessRule>();
            foreach (var row in DataRows(rules))
            {
                var id = XlIo.ReadInt(row, hdr, "Id");
                if (id == 0) continue;
                var rule = new JsonCompletenessBusinessRule
                {
                    Id = id,
                    Type = XlIo.ReadString(row, hdr, "Type"),
                    Weight = XlIo.ReadInt(row, hdr, "Weight"),
                    SortOrder = XlIo.ReadInt(row, hdr, "SortOrder"),
                    GroupIds = SplitSemiList(XlIo.ReadString(row, hdr, "GroupIds"))?.Select(int.Parse).ToList(),
                    Name = ReadLocale(row, hdr, "Name"),
                    RuleSettings = new(),
                };
                c.CompletenessBusinessRules.Add(rule);
                rulesById[id] = rule;
            }
        }
        if (TryGetSheet(wb, SheetCompletenessSettings, out var settings) && rulesById is not null)
        {
            var hdr = XlIo.HeaderMap(settings);
            foreach (var row in DataRows(settings))
            {
                var ruleId = XlIo.ReadInt(row, hdr, "BusinessRuleId");
                if (!rulesById.TryGetValue(ruleId, out var rule)) continue;
                rule.RuleSettings!.Add(new JsonCompletenessRuleSetting
                {
                    Id = XlIo.ReadInt(row, hdr, "Id"),
                    BusinessRuleId = ruleId,
                    Type = XlIo.ReadString(row, hdr, "Type"),
                    Key = XlIo.ReadString(row, hdr, "Key"),
                    Value = NullIfEmpty(XlIo.ReadString(row, hdr, "Value")),
                });
            }
        }
    }

    // ---------------------------------------------------------- shared utilities

    private static void WriteHeader(IXLWorksheet ws, IReadOnlyList<string> cols)
    {
        for (var i = 0; i < cols.Count; i++) ws.Cell(1, i + 1).Value = cols[i];
        XlIo.StyleHeader(ws.Row(1));
    }

    private static IEnumerable<IXLRow> DataRows(IXLWorksheet ws)
    {
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++) yield return ws.Row(r);
    }

    private static bool TryGetSheet(IXLWorkbook wb, string name, out IXLWorksheet ws)
    {
        if (wb.TryGetWorksheet(name, out ws!)) return true;
        ws = null!;
        return false;
    }

    private static JsonLocaleString? ReadLocale(IXLRow row, IReadOnlyDictionary<string, int> hdr, string baseKey)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in hdr)
        {
            if (!kvp.Key.StartsWith(baseKey + "[", StringComparison.OrdinalIgnoreCase) || !kvp.Key.EndsWith("]")) continue;
            var lang = kvp.Key[(baseKey.Length + 1)..^1];
            var val = row.Cell(kvp.Value).GetString();
            if (!string.IsNullOrEmpty(val)) map[lang] = val;
        }
        return map.Count == 0 ? null : new JsonLocaleString { StringMap = map };
    }

    static string LocStr(JsonLocaleString? s, string lang)
        => s?.StringMap is { } m && m.TryGetValue(lang, out var v) ? v : string.Empty;

    static string ScalarCvlValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => string.Empty,
        };
    }

    static string LocCvlValue(JsonElement value, string lang)
    {
        if (value.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!value.TryGetProperty("StringMap", out var map) || map.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var prop in map.EnumerateObject())
            if (string.Equals(prop.Name, lang, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString() ?? string.Empty;
        return string.Empty;
    }

    static IReadOnlyList<string> ReadColumnList(IXLWorkbook wb, string sheet, string column)
    {
        if (!TryGetSheet(wb, sheet, out var ws)) return Array.Empty<string>();
        var hdr = XlIo.HeaderMap(ws);
        if (!hdr.TryGetValue(column, out var col)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var row in DataRows(ws))
        {
            var v = row.Cell(col).GetString().Trim();
            if (!string.IsNullOrEmpty(v)) list.Add(v);
        }
        return list;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    static List<string>? SplitSemiList(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    static string EncodeSettings(Dictionary<string, string>? settings)
    {
        if (settings is null || settings.Count == 0) return string.Empty;
        return string.Join("\n", settings.OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }

    static Dictionary<string, string>? DecodeSettings(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in s.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            d[raw[..eq].Trim()] = raw[(eq + 1)..].Trim();
        }
        return d.Count == 0 ? null : d;
    }
}
