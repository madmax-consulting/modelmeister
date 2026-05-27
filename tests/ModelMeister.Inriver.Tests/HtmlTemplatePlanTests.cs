using System.Collections.Generic;
using System.Linq;
using Shouldly;
using ModelMeister.Inriver.HtmlTemplates;
using Xunit;
using IriverHtmlTemplate = inRiver.Remoting.Objects.HtmlTemplate;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins the pure HTML-template reconcile planner the import workflow drives per row: create when absent,
/// update when the body differs, unchanged when identical (matched by name + type), and delete for live
/// templates absent from the desired set when allowed. The legacy <c>ApplyAsync</c> wraps the same planner.
/// </summary>
public class HtmlTemplatePlanTests
{
    private static IriverHtmlTemplate Live(int id, string name, string type, string content) =>
        new() { Id = id, Name = name, TemplateType = type, Content = content, Properties = "" };

    private static HtmlTemplateDto Dto(string name, string type, string content) =>
        new() { Name = name, TemplateType = type, Content = content, Properties = "" };

    [Fact]
    public void Classifies_create_update_and_unchanged()
    {
        var live = new[]
        {
            Live(1, "Same", "print", "BODY"),
            Live(2, "Changed", "print", "OLD"),
        };
        var desired = new[]
        {
            Dto("Same", "print", "BODY"),       // identical → Unchanged
            Dto("Changed", "print", "NEW"),     // body differs → Update (keeps live id)
            Dto("Brand New", "print", "X"),     // absent → Create
        };

        var actions = HtmlTemplateService.BuildPlan(live, desired, allowDeletes: false);

        actions.Single(a => a.Name == "Same").Kind.ShouldBe(HtmlTemplateActionKind.Unchanged);
        var upd = actions.Single(a => a.Name == "Changed");
        upd.Kind.ShouldBe(HtmlTemplateActionKind.Update);
        upd.LiveId.ShouldBe(2);
        var create = actions.Single(a => a.Name == "Brand New");
        create.Kind.ShouldBe(HtmlTemplateActionKind.Create);
        create.LiveId.ShouldBe(0);
    }

    [Fact]
    public void Name_plus_type_is_the_identity()
    {
        var live = new[] { Live(1, "T", "print", "BODY") };
        // Same name, different type → a different identity → Create, not Update.
        var desired = new[] { Dto("T", "contentstore", "BODY") };

        var actions = HtmlTemplateService.BuildPlan(live, desired, allowDeletes: false);

        actions.Single().Kind.ShouldBe(HtmlTemplateActionKind.Create);
    }

    [Fact]
    public void Deletes_live_templates_absent_from_desired_when_allowed()
    {
        var live = new[]
        {
            Live(1, "Keep", "print", "BODY"),
            Live(2, "Drop", "print", "BODY"),
        };
        var desired = new[] { Dto("Keep", "print", "BODY") };

        var withDeletes = HtmlTemplateService.BuildPlan(live, desired, allowDeletes: true);
        var drop = withDeletes.Single(a => a.Name == "Drop");
        drop.Kind.ShouldBe(HtmlTemplateActionKind.Delete);
        drop.LiveId.ShouldBe(2);

        // With deletes off, the extra live template is simply left alone.
        var noDeletes = HtmlTemplateService.BuildPlan(live, desired, allowDeletes: false);
        noDeletes.ShouldNotContain(a => a.Kind == HtmlTemplateActionKind.Delete);
    }
}
