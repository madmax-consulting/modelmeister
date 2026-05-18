using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Scaffolder;

namespace ModelMeister.Cli.Commands;

public static class CompareEnvsCommand
{
    /// <summary>
    /// Compare two environments. Because Remoting is a process-wide singleton, this connects to
    /// each in turn. If you supply a JSON snapshot for the left side via <paramref name="leftJson"/>,
    /// only the right side is opened on the live connection.
    /// </summary>
    public static async Task<int> RunAsync(
        string? leftJson, string? leftUrl, InriverAuth? leftAuth,
        string rightUrl, InriverAuth rightAuth,
        string outPath, string format, CancellationToken ct)
    {
        LiveModel leftLive;
        if (!string.IsNullOrEmpty(leftJson))
        {
            if (!File.Exists(leftJson))
            {
                AnsiConsole.MarkupLine($"[red]Left JSON not found: {leftJson.EscapeMarkup()}[/]");
                return ExitCodes.UsageError;
            }
            leftLive = LiveModelFromJson(InriverModelJson.Load(leftJson), leftUrl ?? leftJson);
        }
        else if (!string.IsNullOrEmpty(leftUrl) && leftAuth is not null)
        {
            using var lc = new InriverClient(leftUrl);
            var rcl = await leftAuth.ConnectAsync(lc).ConfigureAwait(false);
            if (rcl != ExitCodes.Success) return rcl;
            leftLive = new InriverSnapshot(lc).Capture();
            // Drop the singleton binding so we can connect to the right env.
            RemoteSingletonReset.Reset();
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Provide --left-json or --left-url + auth.[/]");
            return ExitCodes.UsageError;
        }

        using var rc = new InriverClient(rightUrl);
        var rcr = await rightAuth.ConnectAsync(rc).ConfigureAwait(false);
        if (rcr != ExitCodes.Success) return rcr;
        var rightLive = new InriverSnapshot(rc).Capture();

        var diff = EnvironmentComparer.Compare(leftLive, rightLive);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(diff,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            if (!string.IsNullOrEmpty(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllText(outPath, json);
                AnsiConsole.MarkupLine($"[green]Wrote {outPath.EscapeMarkup()}[/]");
            }
            else AnsiConsole.WriteLine(json);
        }
        else
        {
            RenderTextDiff(diff);
            if (!string.IsNullOrEmpty(outPath))
            {
                using var fw = new StringWriter();
                RenderTextDiffTo(diff, fw);
                File.WriteAllText(outPath, fw.ToString());
            }
        }

        return diff.TotalDifferences == 0 ? ExitCodes.Success : ExitCodes.ChangesPending;
    }

    static void RenderTextDiff(EnvironmentDiff d)
    {
        AnsiConsole.MarkupLine($"[bold]Compare:[/] {d.LeftUrl.EscapeMarkup()} [grey]vs[/] {d.RightUrl.EscapeMarkup()}");
        Section("Languages", d.Languages);
        Section("Entity types", d.EntityTypes);
        Section("CVLs", d.Cvls);
        Section("Categories", d.Categories);
        Section("Fieldsets", d.Fieldsets);
        Section("Link types", d.LinkTypes);
        Section("Roles", d.Roles);
        Section("Field types (existence)", d.FieldTypes);

        if (d.ChangedFields.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Field changes:[/]");
            foreach (var c in d.ChangedFields.Take(50))
                AnsiConsole.MarkupLine($"  ~ {c.FieldId} ({c.EntityTypeId}): {string.Join(", ", c.Differences.Select(FormatDiff)).EscapeMarkup()}");
            if (d.ChangedFields.Count > 50) AnsiConsole.MarkupLine($"  ... and {d.ChangedFields.Count - 50} more");
        }
        if (d.CvlValueChanges.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]CVL value differences:[/]");
            foreach (var c in d.CvlValueChanges)
                AnsiConsole.MarkupLine($"  {c.CvlId}: only-left {c.OnlyInLeft.Count}, only-right {c.OnlyInRight.Count}, changed {c.Changed.Count}");
        }
        AnsiConsole.MarkupLine($"[bold]Total differences: {d.TotalDifferences}[/]");
    }

    static void RenderTextDiffTo(EnvironmentDiff d, TextWriter w)
    {
        w.WriteLine($"Compare: {d.LeftUrl} vs {d.RightUrl}");
        SectionText(w, "Languages", d.Languages);
        SectionText(w, "Entity types", d.EntityTypes);
        SectionText(w, "CVLs", d.Cvls);
        SectionText(w, "Categories", d.Categories);
        SectionText(w, "Fieldsets", d.Fieldsets);
        SectionText(w, "Link types", d.LinkTypes);
        SectionText(w, "Roles", d.Roles);
        foreach (var c in d.ChangedFields) w.WriteLine($"~ field {c.FieldId} ({c.EntityTypeId}): {string.Join(", ", c.Differences.Select(FormatDiff))}");
        foreach (var c in d.CvlValueChanges)
            w.WriteLine($"~ cvl {c.CvlId}: only-left {c.OnlyInLeft.Count}, only-right {c.OnlyInRight.Count}, changed {c.Changed.Count}");
        w.WriteLine($"Total differences: {d.TotalDifferences}");
    }

    static void Section(string title, ConceptDelta<string> delta)
    {
        if (delta.Total == 0) return;
        AnsiConsole.MarkupLine($"[bold]{title}[/]:");
        foreach (var id in delta.OnlyInLeft.Take(50)) AnsiConsole.MarkupLine($"  [red]- {id.EscapeMarkup()}[/] (only in left)");
        foreach (var id in delta.OnlyInRight.Take(50)) AnsiConsole.MarkupLine($"  [green]+ {id.EscapeMarkup()}[/] (only in right)");
    }
    static void SectionText(TextWriter w, string title, ConceptDelta<string> delta)
    {
        if (delta.Total == 0) return;
        w.WriteLine($"# {title}");
        foreach (var id in delta.OnlyInLeft) w.WriteLine($"  - {id} (only in left)");
        foreach (var id in delta.OnlyInRight) w.WriteLine($"  + {id} (only in right)");
    }

    static string FormatDiff(PropertyDiff d) => $"{d.Property} {d.Left} → {d.Right}";

    static LiveModel LiveModelFromJson(InriverModelJson json, string url)
        => CvlCommand_LiveModelBridge.Build(json, url);
}

/// <summary>
/// Shared bridge: build a minimal <see cref="LiveModel"/> from a JSON snapshot for read-only
/// comparison/sync flows. Duplicated symbol intentionally avoided — referenced from CvlCommand
/// as well.
/// </summary>
internal static class CvlCommand_LiveModelBridge
{
    public static LiveModel Build(InriverModelJson json, string url)
    {
        // Pull in EnvironmentComparer + ChangeReport via the same JSON->Live conversion as CvlCommand,
        // but produce a richer LiveModel with field types and link types so the comparer can run end-to-end.

        var langs = json.Languages.Select(l => l.Name).ToList();

        var fields = new List<LiveFieldType>();
        var entities = new List<LiveEntityType>();
        foreach (var e in json.EntityTypes)
        {
            var entityFields = json.FieldTypes
                .Where(f => string.Equals(f.EntityTypeId, e.Id, StringComparison.OrdinalIgnoreCase))
                .Select(BuildField)
                .ToList();
            entities.Add(new LiveEntityType
            {
                Id = e.Id,
                Name = ToLs(e.Name),
                IsLinkEntityType = e.IsLinkEntityType,
                DisplayNameFieldId = e.GetDisplayNameFieldTypeId,
                DisplayDescriptionFieldId = e.GetDisplayDescriptionFieldTypeId,
                Fields = entityFields,
            });
            fields.AddRange(entityFields);
        }

        var cvls = new List<LiveCvl>();
        var valuesByCvl = json.CvlValues.GroupBy(v => v.CvlId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var nextValueId = 1;
        foreach (var cvl in json.Cvls)
        {
            var values = (valuesByCvl.TryGetValue(cvl.Id, out var vs) ? vs : new())
                .OrderBy(v => v.Index).ThenBy(v => v.Key)
                .Select(v => new LiveCvlValue
                {
                    Id = nextValueId++,
                    CvlId = cvl.Id,
                    Key = v.Key,
                    Value = ToLsFromValue(v.Value),
                    ParentKey = v.ParentKey,
                    Index = v.Index,
                    Deactivated = v.Deactivated,
                })
                .ToList();
            cvls.Add(new LiveCvl
            {
                Id = cvl.Id,
                DataTypeRaw = cvl.DataType,
                DataType = ParseCvlDataType(cvl.DataType),
                ParentId = cvl.ParentId,
                CustomValueList = cvl.CustomValueList,
                Values = values,
            });
        }

        return new LiveModel
        {
            EnvironmentUrl = url,
            CapturedUtc = DateTime.UtcNow,
            EntityTypes = entities,
            Cvls = cvls,
            Categories = json.Categories.Select(c => new LiveCategory { Id = c.Id, Name = ToLs(c.Name), Index = c.Index }).ToList(),
            Fieldsets = json.FieldSets.Select(fs => new LiveFieldset
            {
                Id = fs.Id,
                EntityTypeId = fs.EntityTypeId,
                Name = ToLs(fs.Name),
                Description = ToLs(fs.Description),
                FieldTypeIds = fs.FieldTypes ?? [],
            }).ToList(),
            LinkTypes = json.LinkTypes.Select(lt => new LiveLinkType
            {
                Id = lt.Id,
                SourceEntityTypeId = lt.SourceEntityTypeId,
                TargetEntityTypeId = lt.TargetEntityTypeId,
                LinkEntityTypeId = lt.LinkEntityTypeId,
                Index = lt.Index,
                SourceName = ToLs(lt.SourceName),
                TargetName = ToLs(lt.TargetName),
            }).ToList(),
            Roles = json.Security?.Roles?.Select(r => new LiveRole
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description ?? string.Empty,
                Permissions = (r.Permissions ?? new()).Select(p => new LivePermission
                { Id = p.Id, Name = p.Name, Description = p.Description ?? string.Empty }).ToList(),
            }).ToList() ?? [],
            Permissions = [],
            CompletenessDefinitions = [],
            RestrictedFieldPermissions = (json.Security?.RestrictedFieldPermissions ?? new())
                .Select(p => new LiveRestrictedFieldPermission
                {
                    Id = p.Id,
                    RoleId = p.RoleId,
                    RestrictionType = p.RestrictionType ?? string.Empty,
                    EntityTypeId = p.EntityTypeId,
                    FieldTypeId = p.FieldTypeId,
                    CategoryId = p.CategoryId,
                }).ToList(),
            Languages = langs,
        };
    }

    static LiveFieldType BuildField(JsonFieldType f) => new()
    {
        Id = f.Id,
        EntityTypeId = f.EntityTypeId,
        Name = ToLs(f.Name),
        Description = ToLs(f.Description),
        DataType = ParseDataType(f.DataType),
        Mandatory = f.Mandatory,
        Unique = f.Unique,
        ReadOnly = f.ReadOnly,
        Hidden = f.Hidden,
        MultiValue = f.Multivalue,
        TrackChanges = f.TrackChanges,
        IsDisplayName = f.IsDisplayName,
        IsDisplayDescription = f.IsDisplayDescription,
        ExcludeFromDefaultView = f.ExcludeFromDefaultView,
        ExpressionSupport = f.ExpressionSupport,
        Index = f.Index,
        CategoryId = f.CategoryId,
        CvlId = f.CvlId,
        DefaultValue = f.DefaultValue,
        Settings = f.Settings is null ? new() : new Dictionary<string, string>(f.Settings),
        Units = [],
    };

    static ModelMeister.Model.Primitives.LocaleString ToLs(JsonLocaleString? ls)
    {
        if (ls is null || ls.IsEmpty()) return new ModelMeister.Model.Primitives.LocaleString();
        var dict = ls.StringMap ?? new Dictionary<string, string>();
        var def = dict.Values.FirstOrDefault() ?? string.Empty;
        return new ModelMeister.Model.Primitives.LocaleString(def, dict);
    }
    static ModelMeister.Model.Primitives.LocaleString ToLsFromValue(System.Text.Json.JsonElement el)
    {
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty("StringMap", out var map) && map.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in map.EnumerateObject())
                if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    dict[p.Name] = p.Value.GetString() ?? string.Empty;
            var def = dict.Values.FirstOrDefault() ?? string.Empty;
            return new ModelMeister.Model.Primitives.LocaleString(def, dict);
        }
        return new ModelMeister.Model.Primitives.LocaleString(
            el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() ?? "" : el.ToString());
    }
    static ModelMeister.Model.Primitives.Datatype ParseDataType(string raw) => raw switch
    {
        "String" => ModelMeister.Model.Primitives.Datatype.String,
        "LocaleString" => ModelMeister.Model.Primitives.Datatype.LocaleString,
        "Integer" => ModelMeister.Model.Primitives.Datatype.Integer,
        "Double" => ModelMeister.Model.Primitives.Datatype.Double,
        "Boolean" => ModelMeister.Model.Primitives.Datatype.Boolean,
        "DateTime" => ModelMeister.Model.Primitives.Datatype.DateTime,
        "Xml" => ModelMeister.Model.Primitives.Datatype.Xml,
        "File" => ModelMeister.Model.Primitives.Datatype.File,
        "CVL" or "Cvl" => ModelMeister.Model.Primitives.Datatype.Cvl,
        _ => ModelMeister.Model.Primitives.Datatype.String,
    };
    static ModelMeister.Model.Primitives.CvlDataType ParseCvlDataType(string raw) => raw switch
    {
        "String" => ModelMeister.Model.Primitives.CvlDataType.String,
        "LocaleString" => ModelMeister.Model.Primitives.CvlDataType.LocaleString,
        "Integer" => ModelMeister.Model.Primitives.CvlDataType.Integer,
        "Double" => ModelMeister.Model.Primitives.CvlDataType.Double,
        "DateTime" => ModelMeister.Model.Primitives.CvlDataType.DateTime,
        _ => ModelMeister.Model.Primitives.CvlDataType.String,
    };
}

/// <summary>
/// Best-effort reset of the Remoting singleton's URL guard so two-environment flows work in a
/// single process. The Remoting client itself still mutates a process-wide singleton, but the
/// guard in <see cref="InriverClient"/> can be opened up to allow a sequential second connect.
/// </summary>
internal static class RemoteSingletonReset
{
    public static void Reset()
    {
        // The InriverClient guard is in static fields; we use reflection only as a last-resort
        // way to release them. Failure here is non-fatal — the caller surfaces the singleton error.
        try
        {
            var t = typeof(InriverClient);
            var f = t.GetField("s_activeUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            f?.SetValue(null, null);
        }
        catch { /* ignore */ }
    }
}
