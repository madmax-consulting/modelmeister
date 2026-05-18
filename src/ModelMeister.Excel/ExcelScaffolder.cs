using ModelMeister.Scaffolder;

namespace ModelMeister.Excel;

/// <summary>
/// Convenience wrapper that scaffolds a C# model project directly from an Excel workbook. Loads
/// the workbook with <see cref="ModelWorkbook"/>, then hands off to <see cref="ProjectScaffolder"/>.
/// </summary>
public static class ExcelScaffolder
{
    public static ScaffoldResult ScaffoldFromExcel(
        string xlsxPath,
        string outDir,
        string rootNamespace,
        bool detectBaseClasses = true,
        bool emitCvlValues = true)
    {
        var model = ModelWorkbook.Load(xlsxPath);
        return new ProjectScaffolder().Scaffold(model, outDir, rootNamespace, detectBaseClasses, emitCvlValues);
    }
}
