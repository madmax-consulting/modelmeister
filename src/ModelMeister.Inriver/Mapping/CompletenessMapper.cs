using IriverCompletenessDefinition = inRiver.Remoting.Objects.CompletenessDefinition;
using IriverCompletenessGroup = inRiver.Remoting.Objects.CompletenessGroup;
using IriverCompletenessBusinessRule = inRiver.Remoting.Objects.CompletenessBusinessRule;
using IriverCompletenessRuleSetting = inRiver.Remoting.Objects.CompletenessRuleSetting;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Inriver.Mapping;

/// <summary>
/// Inriver -> snapshot mapping for the completeness object tree
/// (Definition -> Groups -> BusinessRules -> RuleSettings).
/// </summary>
/// <remarks>
/// Apply-side completeness mapping is intentionally absent: the inriver completeness model
/// requires up-front mapping config the v1 code DSL does not yet expose. The differ
/// surfaces completeness changes as warnings.
/// </remarks>
public static class CompletenessMapper
{
    /// <summary>Map a single completeness definition together with its nested groups, rules and settings.</summary>
    public static LiveCompletenessDefinition ToLive(
        IriverCompletenessDefinition def,
        IEnumerable<IriverCompletenessGroup> groups,
        Func<int, IEnumerable<IriverCompletenessBusinessRule>> rulesForGroup,
        Func<int, IEnumerable<IriverCompletenessRuleSetting>> settingsForRule) => new()
    {
        Id = def.Id,
        Name = LocaleStringMapper.ToTp(def.Name),
        EntityTypeId = def.EntityTypeId,
        Groups = groups
            .Where(g => g.CompletenessDefinitionId == def.Id)
            .Select(g => MapGroup(g, rulesForGroup, settingsForRule))
            .ToList(),
    };

    private static LiveCompletenessGroup MapGroup(
        IriverCompletenessGroup g,
        Func<int, IEnumerable<IriverCompletenessBusinessRule>> rulesForGroup,
        Func<int, IEnumerable<IriverCompletenessRuleSetting>> settingsForRule) => new()
    {
        Id = g.Id,
        Name = LocaleStringMapper.ToTp(g.Name),
        Weight = g.Weight,
        SortOrder = g.SortOrder,
        DefinitionId = g.CompletenessDefinitionId,
        Rules = rulesForGroup(g.Id).Select(r => MapRule(r, settingsForRule)).ToList(),
    };

    private static LiveCompletenessBusinessRule MapRule(
        IriverCompletenessBusinessRule r,
        Func<int, IEnumerable<IriverCompletenessRuleSetting>> settingsForRule) => new()
    {
        Id = r.Id,
        Name = LocaleStringMapper.ToTp(r.Name),
        Type = r.Type,
        Weight = r.Weight,
        SortOrder = r.SortOrder,
        Settings = settingsForRule(r.Id).Select(MapSetting).ToList(),
    };

    private static LiveCompletenessRuleSetting MapSetting(IriverCompletenessRuleSetting s) => new()
    {
        Id = s.Id,
        BusinessRuleId = s.BusinessRuleId,
        Type = s.Type,
        Key = s.Key,
        Value = s.Value ?? string.Empty,
    };
}
