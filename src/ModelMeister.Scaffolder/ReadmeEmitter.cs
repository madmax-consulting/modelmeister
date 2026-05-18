using System.Globalization;

namespace ModelMeister.Scaffolder;

/// <summary>
/// Emits the <c>README.md</c> that lives at the root of a scaffolded project. Captures the
/// where/when/what so future readers don't have to ask the original author.
/// </summary>
internal static class ReadmeEmitter
{
    public static string Emit(string projectName, string? sourceLabel, DateTime generatedAtUtc, InriverModelJson model)
    {
        var when = generatedAtUtc.ToString("u", CultureInfo.InvariantCulture);
        var source = string.IsNullOrWhiteSpace(sourceLabel) ? "(unspecified)" : sourceLabel;

        var entityTypeCount = model.EntityTypes.Count;
        var fieldTypeCount = model.EntityTypes.Sum(e => e.FieldTypes?.Count ?? 0);
        var cvlCount = model.Cvls.Count;
        var categoryCount = model.Categories.Count;
        var linkTypeCount = model.LinkTypes.Count;
        var roleCount = model.Security?.Roles?.Count ?? 0;
        var langCount = model.Languages.Count;

        return $"""
            # {projectName}

            Typed C# representation of an inriver PIM model, scaffolded by ModelMeister.

            ## Generation

            | Field         | Value |
            |---------------|-------|
            | Generated at  | {when} (UTC) |
            | Source        | `{source}` |
            | Generator     | ModelMeister.Scaffolder |

            ## Contents

            | Concept       | Count |
            |---------------|-------|
            | Entity types  | {entityTypeCount} |
            | Field types   | {fieldTypeCount} |
            | CVLs          | {cvlCount} |
            | Categories    | {categoryCount} |
            | Link types    | {linkTypeCount} |
            | Roles         | {roleCount} |
            | Languages     | {langCount} |

            ## Layout

            ```
            {projectName}/
              EntityTypes/   one .cs per entity type, fields grouped by category
              Cvls/          one .cs per CVL with its value enum
              Categories/    one .cs per category
              Fieldsets/     one .cs per field set
              LinkTypes/     consolidated link-type definitions
              Roles/         role + permission definitions
              Languages.cs   language list
              lib/           bundled ModelMeister.Model.dll (so the project builds standalone)
              {projectName}.csproj
            ```

            ## Building

            ```powershell
            dotnet build {projectName}.csproj
            ```

            The DLL under `lib/` is referenced directly so no external NuGet sources are required.

            ## Re-running

            To regenerate this project from an updated source, run the scaffolder again with the
            same output directory and namespace. Hand-edited files in this directory will be
            overwritten — keep customisations in a separate project that references this one.
            """;
    }
}
