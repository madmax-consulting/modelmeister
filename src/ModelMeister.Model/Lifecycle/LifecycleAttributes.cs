namespace ModelMeister.Model.Lifecycle;

/// <summary>
/// Marks an entity type, field, CVL, link type, etc. as scheduled for deletion in inriver.
/// Diff/apply will emit a Delete change record only when --allow-deletes is passed; otherwise warns and skips.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false)]
public sealed class DeletedAttribute : Attribute;

/// <summary>
/// Skip the field from datatype-migration logic. Use when a property's DataType is changing and
/// you intend to migrate data outside the tool. The applier will not attempt the destructive datatype change.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class IgnoreMigrationAttribute : Attribute;
