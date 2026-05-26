using System.Globalization;
using IriverCompletenessDefinition = inRiver.Remoting.Objects.CompletenessDefinition;
using IriverCompletenessGroup = inRiver.Remoting.Objects.CompletenessGroup;
using IriverCompletenessBusinessRule = inRiver.Remoting.Objects.CompletenessBusinessRule;
using IriverCompletenessRuleSetting = inRiver.Remoting.Objects.CompletenessRuleSetting;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Completeness;
using ModelMeister.Model.Loading;
using TpLocaleString = ModelMeister.Model.Primitives.LocaleString;

namespace ModelMeister.Inriver.Mapping;

/// <summary>
/// Mapping for the completeness object tree (Definition -> Groups -> BusinessRules -> RuleSettings),
/// both inriver -> snapshot (<see cref="ToLive"/>) and code -> inriver (the <c>ToInriver*</c> builders
/// used by the applier). The inriver rule <c>Type</c> strings + setting keys come from
/// <see cref="CompletenessRuleVocabulary"/> — the single place to reconcile with a real environment.
/// </summary>
public static class CompletenessMapper
{
    // ---------------------------------------------------------------- inriver -> snapshot

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

    // ---------------------------------------------------------------- code -> inriver (apply)

    /// <summary>Build the inriver definition DTO. inriver names a definition; we default it to the entity type id.</summary>
    public static IriverCompletenessDefinition ToInriverDefinition(LoadedCompletenessDefinition def, int id = 0) => new()
    {
        Id = id,
        EntityTypeId = def.EntityTypeId,
        Name = LocaleStringMapper.ToInriver(new TpLocaleString(def.EntityTypeId)),
    };

    /// <summary>Build the inriver group DTO under <paramref name="definitionId"/>.</summary>
    public static IriverCompletenessGroup ToInriverGroup(LoadedCompletenessGroupInstance g, int definitionId, int id = 0) => new()
    {
        Id = id,
        CompletenessDefinitionId = definitionId,
        Name = LocaleStringMapper.ToInriver(g.Name),
        Weight = g.Weight,
        SortOrder = g.SortOrder,
    };

    /// <summary>Build the inriver business-rule DTO under <paramref name="groupId"/> (settings set separately).</summary>
    public static IriverCompletenessBusinessRule ToInriverRule(LoadedCompletenessRule r, int groupId, int id = 0) => new()
    {
        Id = id,
        GroupIds = [groupId],
        Name = LocaleStringMapper.ToInriver(r.Name ?? new TpLocaleString()),
        Type = CompletenessRuleVocabulary.InriverType(r.Kind),
        Weight = r.Weight,
        SortOrder = r.Index,
    };

    /// <summary>Build the inriver rule settings for <paramref name="r"/>, stamped with <paramref name="businessRuleId"/>.</summary>
    public static List<IriverCompletenessRuleSetting> ToInriverSettings(LoadedCompletenessRule r, int businessRuleId)
    {
        var list = new List<IriverCompletenessRuleSetting>();
        void Add(string key, string? value) => list.Add(new IriverCompletenessRuleSetting
        {
            BusinessRuleId = businessRuleId,
            Type = CompletenessRuleVocabulary.SettingType,
            Key = key,
            Value = value ?? string.Empty,
        });

        // Every rule records the field it is attached to.
        Add(CompletenessRuleVocabulary.FieldTypeIdKey, r.FieldId);
        switch (r.Kind)
        {
            case CompletenessRuleKind.ContainsValue:
            case CompletenessRuleKind.ExactMatch:
                Add(CompletenessRuleVocabulary.ValueKey, r.Value);
                break;
            case CompletenessRuleKind.LinkTypeExists:
                Add(CompletenessRuleVocabulary.LinkTypeIdKey, r.LinkTypeId);
                break;
            case CompletenessRuleKind.NumberEvaluation:
                Add(CompletenessRuleVocabulary.OperatorKey, r.Operator?.ToString());
                Add(CompletenessRuleVocabulary.ValueKey, r.Number?.ToString(CultureInfo.InvariantCulture));
                break;
        }
        return list;
    }
}
