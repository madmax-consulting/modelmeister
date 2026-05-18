using ModelMeister.Model.Cvls;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>
/// CVL whose values are loaded from a JSON file beside the assembly — exercises
/// <see cref="CvlFromFile"/>. The csproj copies <c>colours.json</c> to the build output.
/// FilePath resolves to an absolute path next to this assembly so the lookup works regardless of
/// what AppContext.BaseDirectory points at when the model is loaded by an external tool (the CLI).
/// </summary>
public sealed class ColoursCvl : CvlFromFile
{
    public override string FilePath => Path.Combine(
        Path.GetDirectoryName(typeof(ColoursCvl).Assembly.Location)!,
        "Cvls",
        "colours.json");
}
