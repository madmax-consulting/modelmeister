using Shouldly;
using ModelMeister.Model;
using Xunit;

namespace ModelMeister.Model.Tests;

public class CallerInfoTests
{
    [Fact]
    public void Field_captures_source_file_and_line_at_construction()
    {
        var field = new Field<int>();
        field.SourceFile.ShouldNotBeNullOrEmpty();
        field.SourceFile!.ShouldEndWith("CallerInfoTests.cs");
        field.SourceLine.ShouldBeGreaterThan(0);
        field.SourceLocation.ShouldNotBeNullOrEmpty();
        field.SourceLocation!.ShouldContain(":");
    }

    [Fact]
    public void Field_caller_info_round_trips_through_with_expression()
    {
        var original = new Field<string>();
        var modified = original with { Mandatory = true };
        modified.SourceFile.ShouldBe(original.SourceFile);
        modified.SourceLine.ShouldBe(original.SourceLine);
    }

    [Fact]
    public void Field_caller_info_works_for_Field_with_TCvl()
    {
        var field = new Field<CvlKey, MyCvl>();
        field.SourceFile.ShouldNotBeNullOrEmpty();
        field.SourceLine.ShouldBeGreaterThan(0);
    }

    private sealed class MyCvl : Cvl
    {
        public override Primitives.CvlDataType DataType => Primitives.CvlDataType.String;
        public override IEnumerable<CvlValue> GetValues() => Array.Empty<CvlValue>();
    }
}
