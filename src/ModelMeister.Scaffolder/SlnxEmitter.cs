namespace ModelMeister.Scaffolder;

/// <summary>
/// Emits a Visual Studio solution file in the modern XML <c>.slnx</c> format. The solution
/// lives next to the project directory and references the single scaffolded csproj.
/// </summary>
internal static class SlnxEmitter
{
    public static string Emit(string projectName) =>
        $"""
        <Solution>
          <Project Path="{projectName}/{projectName}.csproj" />
        </Solution>
        """;
}
