using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using ModelMeister.Model.Loading;

namespace ModelMeister.Loading;

/// <summary>
/// Loads a customer model from a built DLL, a csproj (auto-built on demand), or a directory
/// containing one. Each model directory gets its own collectible <see cref="AssemblyLoadContext"/>
/// so reloads after a rebuild work cleanly.
/// </summary>
public sealed class ModelAssemblyLoader
{
    // One ALC per model directory. Each reload Unload()s the prior ALC and creates a new one so
    // MSBuild can overwrite the output DLL on the next build. A cached, never-unloaded ALC would
    // hold a file lock (because LoadFromAssemblyPath memory-maps), breaking edit -> reload -> build.
    private static readonly Dictionary<string, IsolatedLoadContext> ContextByDir =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock ContextLock = new();

    /// <summary>
    /// Resolves <paramref name="path"/> to a DLL (building a csproj if needed), loads it into an
    /// isolated load context, and projects it through <see cref="ModelLoader"/>.
    /// </summary>
    /// <param name="path">A `.dll`, a `.csproj`, or a directory containing a csproj.</param>
    public LoadedModel LoadFromPath(string path)
    {
        var dllPath = Path.GetFullPath(ResolveAssemblyPath(path));
        var dir = Path.GetDirectoryName(dllPath)!;

        var ctx = SwapContext(dir);
        var asm = LoadAssemblyFromBytes(ctx, dllPath);
        return ModelLoader.LoadFromAssembly(asm);
    }

    /// <summary>
    /// Returns a fresh <see cref="IsolatedLoadContext"/> for <paramref name="dir"/>, unloading any
    /// prior context for the same directory. Callers still holding a <see cref="LoadedModel"/>
    /// from before will see their <see cref="Type"/> references invalidated — that is the
    /// intended contract for "I just rebuilt and want the new version".
    /// </summary>
    private static IsolatedLoadContext SwapContext(string dir)
    {
        lock (ContextLock)
        {
            if (ContextByDir.Remove(dir, out var prior))
                prior.Unload();

            var ctx = new IsolatedLoadContext(dir);
            ContextByDir[dir] = ctx;
            return ctx;
        }
    }

    /// <summary>
    /// Loads the assembly (and its PDB if present) as in-memory streams. We deliberately avoid
    /// <see cref="AssemblyLoadContext.LoadFromAssemblyPath"/>: it memory-maps the file, holding a
    /// Windows file lock for the ALC's lifetime, which then trips MSB3027 on the next rebuild.
    /// </summary>
    private static Assembly LoadAssemblyFromBytes(IsolatedLoadContext ctx, string dllPath)
    {
        var asmBytes = File.ReadAllBytes(dllPath);
        var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
        var pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;

        using var asmStream = new MemoryStream(asmBytes);
        if (pdbBytes is null)
            return ctx.LoadFromStream(asmStream);

        using var pdbStream = new MemoryStream(pdbBytes);
        return ctx.LoadFromStream(asmStream, pdbStream);
    }

    private static string ResolveAssemblyPath(string path) => path switch
    {
        _ when path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) => Path.GetFullPath(path),
        _ when path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) => BuildCsproj(path),
        _ when Directory.Exists(path) && Directory.EnumerateFiles(path, "*.csproj").FirstOrDefault() is { } csproj
            => BuildCsproj(csproj),
        _ => throw new FileNotFoundException($"Cannot resolve model assembly from path '{path}'.")
    };

    private static string BuildCsproj(string csproj)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(csproj);
        var modelDllPath = typeof(ModelMeister.Model.Cvl).Assembly.Location;
        var csprojToBuild = TryWriteWrapperForBrokenModelReference(csproj, modelDllPath) ?? csproj;

        RunDotnetBuild(csprojToBuild, modelDllPath);

        var binDir = Path.Combine(Path.GetDirectoryName(csprojToBuild)!, "bin", "Debug");
        return Directory.EnumerateFiles(binDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
                   .FirstOrDefault()
               ?? throw new FileNotFoundException($"Build succeeded but output DLL not found under {binDir}.");
    }

    /// <summary>
    /// Runs <c>dotnet build</c> against <paramref name="csproj"/>. Uses async stream reads to avoid
    /// the classic Process deadlock where blocking sequential reads on stdout+stderr hang once
    /// either pipe's buffer fills.
    /// </summary>
    private static void RunDotnetBuild(string csproj, string modelDllPath)
    {
        var args = new List<string> { "build", csproj, "--nologo", "-c", "Debug" };
        if (!string.IsNullOrEmpty(modelDllPath) && File.Exists(modelDllPath))
            args.Add($"-p:ModelMeisterModelDll={modelDllPath}");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Build failed:{Environment.NewLine}{stdout}{stderr}");
    }

    /// <summary>
    /// Backward-compat shim. Older scaffolded customer csprojs hard-code a relative
    /// <c>ProjectReference</c> to <c>ModelMeister.Model.csproj</c> that only resolves
    /// when the customer's directory sits in a specific spot inside the ModelMeister repo. When
    /// that path no longer exists, the build emits thousands of CS0246 errors. We detect that case
    /// and emit a wrapper csproj (in a temp dir, never inside the customer's directory) that
    /// compiles the same source files against the bundled <c>ModelMeister.Model.dll</c>.
    /// </summary>
    /// <returns>Path to the wrapper csproj, or <c>null</c> when no shim is needed.</returns>
    private static string? TryWriteWrapperForBrokenModelReference(string csproj, string modelDllPath)
    {
        if (string.IsNullOrEmpty(modelDllPath) || !File.Exists(modelDllPath))
            return null;

        XDocument xml;
        try
        {
            xml = XDocument.Load(csproj);
        }
        catch
        {
            return null;
        }

        var ns = xml.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csproj))!;

        var hasBrokenModelRef = xml.Descendants(ns + "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>()
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Replace('/', Path.DirectorySeparatorChar))
            .Where(i => i.EndsWith("ModelMeister.Model.csproj", StringComparison.OrdinalIgnoreCase))
            .Select(i => Path.GetFullPath(Path.Combine(csprojDir, i)))
            .Any(resolved => !File.Exists(resolved));

        if (!hasBrokenModelRef)
            return null;

        var asmName = xml.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value
                      ?? Path.GetFileNameWithoutExtension(csproj);
        var rootNs = xml.Descendants(ns + "RootNamespace").FirstOrDefault()?.Value
                     ?? asmName;

        var dirHash = Convert.ToHexString(BitConverter.GetBytes(csprojDir.GetHashCode()));
        var tempDir = Path.Combine(Path.GetTempPath(), "ModelMeister", $"build-{asmName}-{dirHash}");
        Directory.CreateDirectory(tempDir);

        var wrapperPath = Path.Combine(tempDir, $"{asmName}.csproj");
        File.WriteAllText(wrapperPath, BuildWrapperCsproj(asmName, rootNs, csprojDir, modelDllPath));
        return wrapperPath;
    }

    private static string BuildWrapperCsproj(string asmName, string rootNs, string csprojDir, string modelDllPath) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net9.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{rootNs}</RootNamespace>
            <AssemblyName>{asmName}</AssemblyName>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="{csprojDir}\**\*.cs" Exclude="{csprojDir}\bin\**;{csprojDir}\obj\**" />
            <Reference Include="ModelMeister.Model">
              <HintPath>{modelDllPath}</HintPath>
              <Private>false</Private>
            </Reference>
          </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Collectible <see cref="AssemblyLoadContext"/> that stream-loads dependency assemblies so
    /// they aren't memory-mapped — the bundled Model DLL sits inside the customer's
    /// <c>bin\Debug</c> tree (MSBuild copies it next to the main assembly), and a path-load would
    /// lock that file for the ALC's lifetime, blocking the next rebuild.
    /// </summary>
    private sealed class IsolatedLoadContext(string basePath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(basePath);

        protected override Assembly? Load(AssemblyName name)
        {
            var path = _resolver.ResolveAssemblyToPath(name);
            if (path is null)
                return null;

            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return LoadFromStream(stream);
        }
    }
}
