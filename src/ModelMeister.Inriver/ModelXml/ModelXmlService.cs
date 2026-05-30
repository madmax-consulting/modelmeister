using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.ModelXml;

/// <summary>
/// Wraps inriver's native model XML round-trip (<c>ExportModelAsXmlString</c> /
/// <c>ImportModelFromXmlString</c>). This is the format inriver itself uses to move a model between
/// environments, so it is the most faithful "lift and shift" of an entire model — every concept the
/// platform knows about, in one document — independent of the typed C# model the rest of the toolkit
/// builds on. Export is a pure read; import mutates the connected env's model wholesale.
/// </summary>
public sealed class ModelXmlService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public ModelXmlService(InriverClient remoting, ILogger<ModelXmlService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Export the connected env's whole model as inriver-native XML.</summary>
    /// <param name="includeCvlValues">When true, CVL value items are embedded (larger, fully portable).</param>
    public string Export(bool includeCvlValues)
    {
        var xml = _remoting.Read(m => m.ModelService.ExportModelAsXmlString(includeCvlValues)) ?? string.Empty;
        _log.LogInformation("Exported model XML ({Length} chars, CVL values {Inc})", xml.Length, includeCvlValues);
        return xml;
    }

    /// <summary>
    /// Import an inriver-native model XML document into the connected env. inriver merges the document
    /// into the existing model (adding/updating concepts); it is a write and cannot be dry-run, so
    /// callers must confirm and back up first. Returns inriver's success flag.
    /// </summary>
    public async Task<bool> ImportAsync(string xml, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("Model XML is empty.", nameof(xml));
        _log.LogInformation("Importing model XML ({Length} chars)", xml.Length);
        return await _remoting.WriteAsync(m => m.ModelService.ImportModelFromXmlString(xml), ct).ConfigureAwait(false);
    }
}
