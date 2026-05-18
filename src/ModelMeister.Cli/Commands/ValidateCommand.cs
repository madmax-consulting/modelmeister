using System.Text.Json;
using Spectre.Console;
using ModelMeister.Loading;
using ModelMeister.Model.Cvls;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Validation;
using ValidationResult = ModelMeister.Model.Validation.ValidationResult;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Statically validates a code-defined model assembly, producing either a Spectre rendering
/// or machine-readable JSON for CI consumption.
/// </summary>
public static class ValidateCommand
{
    /// <summary>Runs validation for <paramref name="modelPath"/> (a DLL or csproj).</summary>
    public static int Run(string modelPath, bool json)
    {
        var loader = new ModelAssemblyLoader();
        LoadedModel? model = null;
        ValidationResult result;

        try
        {
            model = loader.LoadFromPath(modelPath);
            result = ModelValidator.Validate(model);
        }
        catch (System.Reflection.TargetInvocationException tex) when (tex.InnerException is CvlSourceMissingException cvl)
        {
            // Surface MM076 cleanly as a validation issue rather than a stack trace.
            result = SyntheticIssue(cvl);
        }
        catch (CvlSourceMissingException cvl)
        {
            result = SyntheticIssue(cvl);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load model:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }

        return Render(model, result, json);
    }

    private static ValidationResult SyntheticIssue(CvlSourceMissingException cvl)
    {
        var synthetic = new ValidationResult();
        synthetic.Error(CvlSourceMissingException.Code, cvl.Message);
        return synthetic;
    }

    private static int Render(LoadedModel? model, ValidationResult result, bool json)
    {
        if (json)
        {
            var payload = new
            {
                Valid = !result.HasErrors,
                ErrorCount = result.Issues.Count(i => i.Severity == Severity.Error),
                WarningCount = result.Issues.Count(i => i.Severity == Severity.Warning),
                EntityTypes = model?.EntityTypes.Count ?? 0,
                Cvls = model?.Cvls.Count ?? 0,
                LinkTypes = model?.LinkTypes.Count ?? 0,
                Issues = result.Issues.Select(i => new { Severity = i.Severity.ToString(), i.Code, i.Message }),
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            // Validation errors get their own dedicated exit code so CI can distinguish them
            // from operational failures (transport, IO, ...).
            return result.HasErrors ? ExitCodes.ValidationFailed : ExitCodes.Success;
        }

        foreach (var issue in result.Issues)
        {
            var tag = issue.Severity is Severity.Error ? "[red]error[/]" : "[yellow]warn[/]";
            var src = string.IsNullOrEmpty(issue.Source)
                ? string.Empty
                : $" [grey](at {issue.Source.EscapeMarkup()})[/]";
            AnsiConsole.MarkupLine($"  {tag} [grey]{issue.Code}[/] {issue.Message.EscapeMarkup()}{src}");
        }

        if (!result.HasErrors && model is not null)
        {
            AnsiConsole.MarkupLine(
                $"[green]✓ Model is valid.[/] [grey]({model.EntityTypes.Count} entity types, " +
                $"{model.Cvls.Count} CVLs, {model.LinkTypes.Count} link types)[/]");
        }

        return result.HasErrors ? ExitCodes.ValidationFailed : ExitCodes.Success;
    }
}
