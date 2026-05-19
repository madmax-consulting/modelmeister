using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>Plain entity type, no inheritance — proves the non-inherited path still works.</summary>
public sealed class Supplier : EntityType
{
    [DisplayName, Unique]
    public Field<string> SupplierCode { get; init; } = new();
    [Mandatory]
    public Field<LocaleString> CompanyName { get; init; } = new();
    [RegExp(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    public Field<string> ContactEmail { get; init; } = new();
}
