using Shouldly;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

/// <summary>
/// Pins the emitter's attribute-form output: bool/scalar field flags hoist out of the
/// <c>= new() { ... }</c> object initializer into a single attribute row above each field.
/// </summary>
public class EntityTypeEmitterAttributeFormTests
{
    private static string EmitField(JsonFieldType f) =>
        EntityTypeEmitter.Emit(
            new JsonEntityType
            {
                Id = "Product",
                FieldTypes = new List<JsonFieldType> { f },
            },
            "Acme",
            baseClass: null,
            _: new Dictionary<string, List<JsonCvlValue>>());

    [Fact]
    public void Mandatory_emits_as_attribute_not_initializer()
    {
        var src = EmitField(new JsonFieldType
        {
            Id = "ProductName",
            DataType = "String",
            EntityTypeId = "Product",
            Mandatory = true,
            TrackChanges = true, // a normal tracked field — the default, so it is not emitted
        });

        src.ShouldContain("[Mandatory]");
        src.ShouldNotContain("Mandatory = true");
        // Initializer should collapse to empty.
        src.ShouldContain("= new();");
    }

    [Fact]
    public void Multiple_flags_pack_into_single_attribute_row()
    {
        var src = EmitField(new JsonFieldType
        {
            Id = "ProductSku",
            DataType = "String",
            EntityTypeId = "Product",
            Mandatory = true,
            Unique = true,
            Hidden = true,
        });

        src.ShouldContain("[Mandatory, Unique, Hidden]");
        src.ShouldNotContain("Mandatory = true");
    }

    [Fact]
    public void ReadOnly_uses_ReadOnlyField_attribute_name()
    {
        var src = EmitField(new JsonFieldType
        {
            Id = "ProductLocked",
            DataType = "String",
            EntityTypeId = "Product",
            ReadOnly = true,
        });

        // Suffixed to disambiguate from System.ComponentModel.ReadOnlyAttribute.
        src.ShouldContain("[ReadOnlyField]");
    }

    [Fact]
    public void DisplayName_attribute_concatenates_with_flag_attributes()
    {
        var src = EmitField(new JsonFieldType
        {
            Id = "ProductTitle",
            DataType = "String",
            EntityTypeId = "Product",
            IsDisplayName = true,
            Mandatory = true,
        });

        // Flag order: data-shape first, display markers last.
        src.ShouldContain("[Mandatory, DisplayName]");
    }

    [Fact]
    public void Field_with_no_flags_has_no_attribute_row()
    {
        var src = EmitField(new JsonFieldType
        {
            Id = "ProductMemo",
            DataType = "String",
            EntityTypeId = "Product",
            TrackChanges = true, // default — not emitted
        });

        src.ShouldContain("public Field<string> Memo { get; init; } = new();");
        src.ShouldNotContain("    [");  // no attribute decoration above the field
    }

    [Fact]
    public void Index_is_not_emitted_as_attribute_to_preserve_read_through_semantics()
    {
        // Index/ExcludeFromDefaultView are nullable read-through properties — the scaffolder must
        // NOT emit them so that an unspecified code-side value continues to mean "leave inriver's
        // value alone" (see CLAUDE.md > Diff/apply contract).
        var src = EmitField(new JsonFieldType
        {
            Id = "ProductSort",
            DataType = "String",
            EntityTypeId = "Product",
            Index = 7,
            TrackChanges = true,
        });

        src.ShouldNotContain("[Index");
        src.ShouldNotContain("Index = 7");
    }

    [Fact]
    public void TrackChanges_off_emits_initializer_default_on_does_not()
    {
        // TrackChanges defaults to true (code model is authoritative). The scaffolder pins it only
        // when the source has tracking off.
        var off = EmitField(new JsonFieldType
        {
            Id = "ProductHidden",
            DataType = "String",
            EntityTypeId = "Product",
            TrackChanges = false,
        });
        off.ShouldContain("TrackChanges = false");

        var on = EmitField(new JsonFieldType
        {
            Id = "ProductVisible",
            DataType = "String",
            EntityTypeId = "Product",
            TrackChanges = true,
        });
        on.ShouldNotContain("TrackChanges");
    }
}
