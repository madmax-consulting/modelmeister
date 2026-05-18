namespace ModelMeister.Ui.Services;

/// <summary>How a FeaturePage's data participates in the backup system.</summary>
public enum BackupScope
{
    /// <summary>Page does not produce backups (e.g., write-only or read-only system pages).</summary>
    None,
    Full,
    Model,
    Cvls,
    Categories,
    Entities,
    Fieldsets,
    LinkTypes,
    Users,
    ServerSettings,
    Extensions,
}

/// <summary>Excel button set the page should expose on the FeaturePage toolbar.</summary>
public enum ExcelCapability
{
    /// <summary>No Excel buttons.</summary>
    None,
    /// <summary>Export only.</summary>
    Export,
    /// <summary>Export and Import.</summary>
    ExportImport,
}
