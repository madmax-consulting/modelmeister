namespace ModelMeister.Scaffolder;

/// <summary>
/// Orchestrates JSON → C# project generation. Emits an entity-types-per-file layout
/// mirroring <c>ModelMeister.ExampleModel</c>: one file per category / CVL / entity type
/// / field set, plus consolidated link-type, role, and language files. The Model assembly is
/// bundled under <c>lib\</c> so the scaffolded project builds standalone.
/// </summary>
public sealed class ProjectScaffolder
{
    /// <summary>Scaffold from a JSON model export file.</summary>
    public ScaffoldResult Scaffold(string jsonPath, string outDir, string rootNamespace, bool detectBaseClasses = true, bool emitCvlValues = true)
        => Scaffold(InriverModelJson.Load(jsonPath), outDir, rootNamespace, detectBaseClasses, emitCvlValues, sourceLabel: jsonPath);

    /// <summary>Scaffold from an already-loaded <see cref="InriverModelJson"/>.</summary>
    public ScaffoldResult Scaffold(
        InriverModelJson model,
        string outDir,
        string rootNamespace,
        bool detectBaseClasses = true,
        bool emitCvlValues = true,
        string? sourceLabel = null,
        DateTime? generatedAt = null)
    {
        // The project root lives one level inside outDir so the .slnx + project layout looks like
        // a normal Visual Studio solution. The csproj sits at projectDir/<rootNamespace>.csproj.
        var projectDir = Path.Combine(outDir, rootNamespace);
        var dirs = new[] { "EntityTypes", "Cvls", "Categories", "Fieldsets", "LinkTypes", "Roles" };
        Directory.CreateDirectory(projectDir);
        foreach (var d in dirs) Directory.CreateDirectory(Path.Combine(projectDir, d));

        // Emit fields in the order inriver renders them (Index ascending) rather than whatever the
        // JSON export happens to serialize — typically alphabetical by id. Ties fall back to id for
        // determinism. Sorting here means both BaseClassDetector and EntityTypeEmitter see fields in
        // the same order.
        foreach (var e in model.EntityTypes)
        {
            if (e.FieldTypes is { Count: > 1 } fields)
                fields.Sort((a, b) =>
                {
                    var cmp = a.Index.CompareTo(b.Index);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
                });
        }

        var result = new ScaffoldResult();

        // Helper: write a file under projectDir and record it in the result.
        void Write(string relPath, string content)
        {
            var full = Path.Combine(projectDir, relPath);
            File.WriteAllText(full, content);
            result.Files.Add(full);
        }

        var baseClasses = detectBaseClasses ? BaseClassDetector.Detect(model) : [];

        // Categories
        foreach (var c in model.Categories)
            Write(Path.Combine("Categories", Sanitize(c.Id) + ".cs"), CategoryEmitter.Emit(c, rootNamespace));

        // CVLs (with values) — index values by CVL id once so each emit avoids a re-scan.
        var valuesByCvl = model.CvlValues
            .GroupBy(v => v.CvlId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var cvl in model.Cvls)
        {
            var values = valuesByCvl.TryGetValue(cvl.Id, out var v) ? v : [];
            Write(Path.Combine("Cvls", Sanitize(cvl.Id) + "Cvl.cs"), CvlEmitter.Emit(cvl, values, rootNamespace, emitCvlValues));
        }

        // Abstract base classes detected by BaseClassDetector
        foreach (var bc in baseClasses)
            Write(Path.Combine("EntityTypes", bc.ClassName + ".cs"), EntityTypeEmitter.EmitBase(bc, rootNamespace));

        // Sanitized entity-type names are passed to the emitter so it can detect Category /
        // EntityType name collisions (e.g. inriver has both an "ETIM" category and "ETIM" entity
        // type) and fully qualify the Category reference to disambiguate from the namespace-local
        // entity type that would otherwise win.
        var entityTypeNames = model.EntityTypes
            .Select(e => Sanitize(e.Id))
            .ToHashSet(StringComparer.Ordinal);

        // Symbol table used by ExpressionParser to emit nameof(...)-based references rather than
        // magic strings inside scaffolded DefaultExpression bodies.
        var exprContext = ExpressionContext.Build(model);

        // Entity types — pick the base class whose member set is entirely covered by this entity.
        foreach (var e in model.EntityTypes)
        {
            var bc = baseClasses.FirstOrDefault(b =>
                b.Members.All(m => e.FieldTypes?.Any(f => MatchesMember(f, m, e.Id)) == true));
            var src = EntityTypeEmitter.Emit(e, rootNamespace, bc, valuesByCvl, entityTypeNames, exprContext);
            Write(Path.Combine("EntityTypes", Sanitize(e.Id) + ".cs"), src);
            result.WarningsFromExpressions.AddRange(EntityTypeEmitter.LastEmissionWarnings);
        }

        // Field sets
        foreach (var fs in model.FieldSets)
            Write(Path.Combine("Fieldsets", Sanitize(fs.Id) + "Fieldset.cs"), FieldsetEmitter.Emit(fs, rootNamespace));

        // Link types (consolidated)
        if (model.LinkTypes.Count > 0)
            Write(Path.Combine("LinkTypes", "LinkTypes.cs"), LinkTypeEmitter.EmitAll(model.LinkTypes, rootNamespace));

        // Roles (and any custom permissions discovered on the way)
        if (model.Security?.Roles is { Count: > 0 } roles)
            EmitRoles(roles, rootNamespace, Write);

        // Languages.cs
        if (model.Languages.Count > 0)
            Write("Languages.cs", LanguagesEmitter.Emit(model.Languages, rootNamespace));

        // Bundle the Model DLL so the scaffolded project builds standalone (CLI, IDE, CI). The
        // alternative — relying on $(ModelMeisterModelDll) injected by ModelAssemblyLoader — only
        // worked when the model was built via the loader, not when the customer ran `dotnet build`
        // themselves.
        var libDir = Path.Combine(projectDir, "lib");
        Directory.CreateDirectory(libDir);
        var modelDllSrc = typeof(ModelMeister.Model.Cvl).Assembly.Location;
        var modelDllDest = Path.Combine(libDir, "ModelMeister.Model.dll");
        File.Copy(modelDllSrc, modelDllDest, overwrite: true);
        result.Files.Add(modelDllDest);

        // csproj
        Write(rootNamespace + ".csproj", CsprojEmitter.Emit(rootNamespace));

        // README — generation metadata that callers (and future readers of the scaffolded code)
        // want at a glance.
        var stamp = generatedAt ?? DateTime.UtcNow;
        Write("README.md", ReadmeEmitter.Emit(rootNamespace, sourceLabel, stamp, model));

        // .slnx sibling — sits alongside the project dir so opening outDir in an IDE picks up the
        // whole solution. .slnx is the XML solution format VS 17.10+ understands.
        var slnxPath = Path.Combine(outDir, rootNamespace + ".slnx");
        File.WriteAllText(slnxPath, SlnxEmitter.Emit(rootNamespace));
        result.Files.Add(slnxPath);

        return result;
    }

    private static void EmitRoles(List<JsonRole> roles, string ns, Action<string, string> write)
    {
        // Match standard permissions case-insensitively — inriver JSON sometimes uses
        // "inRiverPrint" while the C# type is "InRiverPrint", and we don't want to shadow a
        // standard permission with a customer-side duplicate just because of casing.
        var standardPermissionsByLowerName = typeof(ModelMeister.Model.Security.StandardPermissions)
            .GetNestedTypes()
            .ToDictionary(t => t.Name, t => t.Name, StringComparer.OrdinalIgnoreCase);

        var unknownPermissionNames = roles
            .SelectMany(r => r.Permissions ?? [])
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !standardPermissionsByLowerName.ContainsKey(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unknownPermissionNames.Count > 0)
            write(Path.Combine("Roles", "CustomPermissions.cs"),
                RoleEmitter.EmitCustomPermissions(unknownPermissionNames, ns));

        foreach (var r in roles)
            write(Path.Combine("Roles", Sanitize(r.Name) + "Role.cs"),
                RoleEmitter.Emit(r, ns, standardPermissionsByLowerName));
    }

    /// <summary>
    /// Strips characters that aren't valid in a C# identifier, then prefixes <c>_</c> if the
    /// result would be empty or start with a digit. Examples: <c>My-Specs</c> → <c>MySpecs</c>,
    /// <c>4Wheels</c> → <c>_4Wheels</c>.
    /// </summary>
    /// <remarks>
    /// The <see cref="ModelMeister.Inriver"/> project relies on this exact rule when
    /// looking up category ids by CLR type — changing the algorithm without coordinating breaks
    /// the diff/apply round-trip.
    /// </remarks>
    public static string Sanitize(string id)
    {
        var kept = new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (kept.Length == 0 || char.IsDigit(kept[0])) kept = "_" + kept;
        return kept;
    }

    /// <summary>
    /// A field belongs to a detected base member when its id has the form
    /// <c>{entityTypeId}{PropertyName}</c> and its datatype matches.
    /// </summary>
    private static bool MatchesMember(JsonFieldType f, BaseClassMember m, string entityTypeId) =>
        f.Id.Equals(entityTypeId + m.PropertyName, StringComparison.OrdinalIgnoreCase)
        && f.DataType == m.DataType;
}

/// <summary>Outcome of a scaffold run: emitted files and any expression-parser warnings.</summary>
public sealed class ScaffoldResult
{
    /// <summary>Every file the scaffolder wrote (or copied) under the output directory.</summary>
    public List<string> Files { get; } = [];

    /// <summary>Diagnostic messages produced while parsing inriver expression strings.</summary>
    public List<string> WarningsFromExpressions { get; } = [];
}
