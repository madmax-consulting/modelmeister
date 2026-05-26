using System.Reflection;
using ModelMeister.Model.Completeness;
using ModelMeister.Model.Lifecycle;
using ModelMeister.Model.Primitives;
using ModelMeister.Model.Security;

namespace ModelMeister.Model.Loading;

/// <summary>
/// Reflects over a model assembly and produces a <see cref="LoadedModel"/> — the in-memory
/// representation consumed by the differ, validator and downstream tooling.
/// </summary>
public static class ModelLoader
{
    /// <summary>
    /// Reflects over an assembly and produces a fully stamped <see cref="LoadedModel"/>.
    /// Abstract base classes are field-mixins only; only concrete subclasses are registered.
    /// Field IDs are stamped with the concrete entity-type's ID, not the declaring base's.
    /// </summary>
    public static LoadedModel LoadFromAssembly(Assembly assembly)
    {
        var types = assembly.GetTypes();

        var entityTypes = LoadEntityTypes(types);
        var linkTypes = LoadLinkTypes(types);
        var completenessGroups = LoadCompletenessGroups(types);

        return new LoadedModel
        {
            EntityTypes = entityTypes,
            Cvls = LoadCvls(types),
            Categories = LoadCategories(types),
            Fieldsets = LoadFieldsets(types),
            LinkTypes = linkTypes,
            Roles = LoadRoles(types),
            Permissions = LoadPermissions(types),
            CompletenessGroups = completenessGroups,
            CompletenessDefinitions = BuildCompletenessDefinitions(entityTypes, completenessGroups, linkTypes),
            SpecificationTemplates = LoadSpecificationTemplates(types),
            Languages = LoadLanguages(assembly),
        };
    }

    /// <summary>Concrete (non-abstract, non-open-generic) subclasses of <typeparamref name="T"/> in <paramref name="types"/>.</summary>
    private static IEnumerable<Type> ConcreteSubclassesOf<T>(IEnumerable<Type> types) =>
        types.Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(T).IsAssignableFrom(t));

    /// <summary>Instantiates <paramref name="type"/> via its parameterless constructor and casts to <typeparamref name="T"/>.</summary>
    private static T Instantiate<T>(Type type) => (T)Activator.CreateInstance(type)!;

    private static List<LoadedEntityType> LoadEntityTypes(Type[] types) =>
        ConcreteSubclassesOf<EntityType>(types)
            .Select(t =>
            {
                var instance = Instantiate<EntityType>(t);
                return new LoadedEntityType
                {
                    ClrType = t,
                    EntityTypeId = instance.EntityTypeId,
                    Name = instance.EntityTypeName,
                    Description = instance.EntityTypeDescription,
                    IsLinkEntityType = instance.IsLinkEntityType,
                    Icon = instance.Icon,
                    Settings = new Dictionary<string, string>(instance.Settings),
                    Fields = LoadFieldsForEntity(instance, t),
                    MarkedForDeletion = t.GetCustomAttribute<DeletedAttribute>() is not null,
                };
            })
            .ToList();

    private static List<LoadedField> LoadFieldsForEntity(EntityType entity, Type entityClrType) =>
        entityClrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => typeof(Field).IsAssignableFrom(p.PropertyType))
            // Derived-class redeclarations supersede the base — pick the deepest declarer per property name.
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Select(g => g.MaxBy(p => DepthOfDeclaration(p, entityClrType))!)
            .Select(prop => BuildLoadedField(entity, prop))
            .OfType<LoadedField>()
            .ToList();

    private static LoadedField? BuildLoadedField(EntityType entity, PropertyInfo prop)
    {
        if (prop.GetValue(entity) is not Field field) return null;

        var id = field.Id ?? $"{entity.EntityTypeId}{prop.Name}";
        var name = field.Name ?? new LocaleString(NameHumanizer.Humanize(prop.Name));

        // Stamp metadata onto the field instance via reflection (init-only setters are normal IL setters).
        SetInit(field, nameof(Field.Id), id);
        SetInit(field, nameof(Field.EntityTypeId), entity.EntityTypeId);
        SetInit(field, nameof(Field.PropertyName), prop.Name);
        SetInit(field, nameof(Field.Name), name);

        var attrs = prop.GetCustomAttributes(inherit: true).Cast<Attribute>().ToArray();

        // [DisplayName] / [DisplayDescription] attributes are authoritative when present —
        // they let the field declaration stay focused on data and put the "this is the display
        // name" decision next to the property. The bool initialiser path is kept for back-compat.
        if (attrs.OfType<DisplayNameAttribute>().Any() && !field.IsDisplayName)
            SetInit(field, nameof(Field.IsDisplayName), true);
        if (attrs.OfType<DisplayDescriptionAttribute>().Any() && !field.IsDisplayDescription)
            SetInit(field, nameof(Field.IsDisplayDescription), true);

        var duplicates = ApplyFieldOptionAttributes(field, attrs);

        // TrackChanges is on by default — the code model is authoritative. An unset value
        // (no [TrackChanges] attribute, no initializer) means "track"; opt out explicitly with
        // an object-initializer `TrackChanges = false`. Stamped after the attribute pass so a
        // false initializer survives.
        if (field.TrackChanges is null)
            SetInit(field, nameof(Field.TrackChanges), true);

        return new LoadedField
        {
            Field = field,
            Id = id,
            EntityTypeId = entity.EntityTypeId,
            PropertyName = prop.Name,
            Name = name,
            DataType = field.DataType,
            Attributes = attrs,
            MarkedForDeletion = attrs.OfType<DeletedAttribute>().Any(),
            DuplicateAttributeFlags = duplicates,
        };
    }

    /// <summary>
    /// Stamps any <c>FieldOptionAttributes</c> present on the property onto the field instance,
    /// returning the names of properties that were already set by the object initializer
    /// (so a validator can warn — see MM012). Attribute wins at runtime in either case.
    /// </summary>
    private static IReadOnlyList<string> ApplyFieldOptionAttributes(Field field, Attribute[] attrs)
    {
        var duplicates = new List<string>();

        // Boolean markers — the initializer-side property is non-nullable, so "already true" is the
        // only signal that both forms were used.
        ApplyBool<MandatoryAttribute>(field, nameof(Field.Mandatory), duplicates);
        ApplyBool<UniqueAttribute>(field, nameof(Field.Unique), duplicates);
        ApplyBool<ReadOnlyFieldAttribute>(field, nameof(Field.ReadOnly), duplicates);
        ApplyBool<HiddenAttribute>(field, nameof(Field.Hidden), duplicates);
        ApplyBool<MultiValueAttribute>(field, nameof(Field.MultiValue), duplicates);
        ApplyBool<PerMarketAttribute>(field, nameof(Field.PerMarket), duplicates);
        ApplyBool<SupportsExpressionAttribute>(field, nameof(Field.SupportsExpression), duplicates);
        ApplyBool<ShowInEntityOverviewAttribute>(field, nameof(Field.ShowInEntityOverview), duplicates);
        ApplyBool<IgnoreFieldInEpiserverExportAttribute>(field, nameof(Field.IgnoreFieldInEpiserverExport), duplicates);

        // Nullable bool markers — only collide when the initializer also set the same value (true).
        ApplyNullableBoolTrue<TrackChangesAttribute>(field, nameof(Field.TrackChanges), duplicates);
        ApplyNullableBoolTrue<ExcludeFromDefaultViewAttribute>(field, nameof(Field.ExcludeFromDefaultView), duplicates);

        // Scalar parameterized attributes.
        if (attrs.OfType<IndexAttribute>().FirstOrDefault() is { } ix)
        {
            if (field.Index is not null) duplicates.Add(nameof(Field.Index));
            SetInit(field, nameof(Field.Index), ix.Value);
        }
        if (attrs.OfType<NumberOfRowsAttribute>().FirstOrDefault() is { } nr)
        {
            // Default in the type is 1 — treat anything else as initializer-set for conflict detection.
            if (field.NumberOfRows != 1) duplicates.Add(nameof(Field.NumberOfRows));
            SetInit(field, nameof(Field.NumberOfRows), nr.Value);
        }
        if (attrs.OfType<RegExpAttribute>().FirstOrDefault() is { } rx)
        {
            if (!string.IsNullOrEmpty(field.RegExp)) duplicates.Add(nameof(Field.RegExp));
            SetInit(field, nameof(Field.RegExp), rx.Pattern);
        }
        if (attrs.OfType<FieldCategoryAttribute>().FirstOrDefault() is { } cat)
        {
            if (field.Category is not null) duplicates.Add(nameof(Field.Category));
            SetInit(field, nameof(Field.Category), cat.Category);
        }

        // [Fieldset] is AllowMultiple — collect all instances and merge with any initializer-set
        // Fieldsets. The init-only Fieldset (singular) setter rewrites Fieldsets to a one-element
        // list, so reading Field.Fieldsets is the authoritative source.
        var fieldsetAttrs = attrs.OfType<FieldsetAttribute>().ToArray();
        if (fieldsetAttrs.Length > 0)
        {
            if (field.Fieldsets.Count > 0) duplicates.Add(nameof(Field.Fieldsets));
            var merged = field.Fieldsets.Concat(fieldsetAttrs.Select(fs => fs.Fieldset)).Distinct().ToArray();
            SetInit(field, nameof(Field.Fieldsets), (IReadOnlyList<Type>)merged);
        }

        return duplicates;

        void ApplyBool<TAttr>(Field f, string propName, List<string> dups) where TAttr : Attribute
        {
            if (!attrs.OfType<TAttr>().Any()) return;
            var current = (bool)f.GetType().GetProperty(propName)!.GetValue(f)!;
            if (current) dups.Add(propName);
            else SetInit(f, propName, true);
        }

        void ApplyNullableBoolTrue<TAttr>(Field f, string propName, List<string> dups) where TAttr : Attribute
        {
            if (!attrs.OfType<TAttr>().Any()) return;
            var current = (bool?)f.GetType().GetProperty(propName)!.GetValue(f);
            if (current == true) dups.Add(propName);
            else SetInit(f, propName, true);
        }
    }

    /// <summary>Number of base steps from <paramref name="concrete"/> down to the property's declaring type.</summary>
    private static int DepthOfDeclaration(PropertyInfo prop, Type concrete)
    {
        var declaring = prop.DeclaringType!;
        var depth = 0;
        for (var t = concrete; t is not null && t != declaring; t = t.BaseType)
            depth++;
        return depth;
    }

    private static void SetInit(object target, string propertyName, object? value)
    {
        var prop = target.GetType().GetProperty(propertyName,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   ?? throw new InvalidOperationException(
                       $"Property {propertyName} not found on {target.GetType()}");
        prop.SetValue(target, value);
    }

    private static List<LoadedCvl> LoadCvls(Type[] types)
    {
        var concrete = ConcreteSubclassesOf<Cvl>(types).ToArray();
        var byClrType = concrete.ToDictionary(t => t, Instantiate<Cvl>);

        return concrete
            .Select(t =>
            {
                var c = byClrType[t];
                var parent = c.ParentCvl is null ? null : byClrType.GetValueOrDefault(c.ParentCvl);
                var entity = c.EntityType is null ? null : Instantiate<EntityType>(c.EntityType);

                return new LoadedCvl
                {
                    ClrType = t,
                    CvlId = c.CvlId,
                    DataType = c.DataType,
                    ParentCvlClrType = c.ParentCvl,
                    ParentCvlId = parent?.CvlId,
                    CustomValueList = c.CustomValueList,
                    EntityTypeClrType = c.EntityType,
                    EntityTypeId = entity?.EntityTypeId,
                    Values = c.GetValues().ToArray(),
                };
            })
            .ToList();
    }

    private static readonly HashSet<Type> ReservedCategoryTypes =
        [typeof(Categories.General), typeof(Categories.FileInformation)];

    private static List<LoadedCategory> LoadCategories(Type[] types) =>
        ConcreteSubclassesOf<Category>(types)
            .Select(t =>
            {
                var c = Instantiate<Category>(t);
                return new LoadedCategory
                {
                    ClrType = t,
                    CategoryId = c.CategoryId,
                    Name = c.Name,
                    Index = c.Index,
                    OrderByName = c.OrderByName,
                    IsReserved = ReservedCategoryTypes.Contains(t),
                };
            })
            .ToList();

    private static List<LoadedFieldset> LoadFieldsets(Type[] types) =>
        ConcreteSubclassesOf<Fieldset>(types)
            .Select(t =>
            {
                var f = Instantiate<Fieldset>(t);
                var entity = Instantiate<EntityType>(f.EntityType);
                return new LoadedFieldset
                {
                    ClrType = t,
                    FieldsetId = f.FieldsetId,
                    Name = f.Name,
                    Description = f.Description,
                    EntityTypeId = entity.EntityTypeId,
                    Index = f.Index,
                };
            })
            .ToList();

    private static List<LoadedLinkType> LoadLinkTypes(Type[] types) =>
        ConcreteSubclassesOf<LinkType>(types)
            .Select(t =>
            {
                var l = Instantiate<LinkType>(t);
                var src = Instantiate<EntityType>(l.Source);
                var tgt = Instantiate<EntityType>(l.Target);
                var linkEntity = l.LinkEntityType is null ? null : Instantiate<EntityType>(l.LinkEntityType);

                return new LoadedLinkType
                {
                    ClrType = t,
                    // Fall back to the CLR type name (unique within the assembly) rather than
                    // source+target, which collides when multiple link types share endpoints
                    // (e.g. ProductAccessoriesProduct + ProductRelatedProduct, both Product<->Product).
                    LinkTypeId = l.LinkTypeId ?? t.Name,
                    SourceEntityTypeId = src.EntityTypeId,
                    TargetEntityTypeId = tgt.EntityTypeId,
                    LinkEntityTypeId = linkEntity?.EntityTypeId,
                    Index = l.Index,
                    SourceName = l.SourceName,
                    TargetName = l.TargetName,
                    Settings = new Dictionary<string, string>(l.Settings),
                };
            })
            .ToList();

    private static List<LoadedRole> LoadRoles(Type[] types)
    {
        var permissionsByClr = ConcreteSubclassesOf<Permission>(types)
            .ToDictionary(t => t, Instantiate<Permission>);

        return ConcreteSubclassesOf<Role>(types)
            .Select(t =>
            {
                var r = Instantiate<Role>(t);
                var names = r.Permissions
                    .Select(pt => permissionsByClr.TryGetValue(pt, out var p) ? p.Name : pt.Name)
                    .ToArray();
                return new LoadedRole
                {
                    ClrType = t,
                    Name = r.Name,
                    Description = r.Description,
                    PermissionClrTypes = r.Permissions.ToArray(),
                    PermissionNames = names,
                };
            })
            .ToList();
    }

    private static List<LoadedPermission> LoadPermissions(Type[] types) =>
        ConcreteSubclassesOf<Permission>(types)
            .Select(t =>
            {
                var p = Instantiate<Permission>(t);
                return new LoadedPermission
                {
                    ClrType = t,
                    Name = p.Name,
                    Description = p.Description,
                };
            })
            .ToList();

    private static List<LoadedCompletenessGroup> LoadCompletenessGroups(Type[] types) =>
        ConcreteSubclassesOf<CompletenessGroup>(types)
            .Select(t =>
            {
                var g = Instantiate<CompletenessGroup>(t);
                return new LoadedCompletenessGroup
                {
                    ClrType = t,
                    Name = g.Name,
                    Weight = g.Weight,
                    SortOrder = g.SortOrder,
                };
            })
            .ToList();

    /// <summary>
    /// Assembles the completeness model from the per-field <c>CompletenessRuleAttribute</c>s already
    /// captured on the loaded entity types: one <see cref="LoadedCompletenessDefinition"/> per entity
    /// type that declares any rule, with rules bucketed into their target group (identity resolved from
    /// the <see cref="LoadedCompletenessGroup"/> classes).
    /// </summary>
    private static List<LoadedCompletenessDefinition> BuildCompletenessDefinitions(
        List<LoadedEntityType> entityTypes,
        List<LoadedCompletenessGroup> groups,
        List<LoadedLinkType> linkTypes)
    {
        var groupByClr = groups.ToDictionary(g => g.ClrType);
        var linkIdByClr = linkTypes
            .GroupBy(l => l.ClrType)
            .ToDictionary(g => g.Key, g => g.First().LinkTypeId);

        var definitions = new List<LoadedCompletenessDefinition>();

        foreach (var et in entityTypes.OrderBy(e => e.EntityTypeId, StringComparer.Ordinal))
        {
            var byGroup = new Dictionary<Type, List<LoadedCompletenessRule>>();
            foreach (var field in et.Fields)
                foreach (var attr in field.Attributes.OfType<CompletenessRuleAttribute>())
                {
                    var rule = BuildCompletenessRule(et.EntityTypeId, field.Id, attr, linkIdByClr);
                    if (!byGroup.TryGetValue(attr.Group, out var list))
                        byGroup[attr.Group] = list = [];
                    list.Add(rule);
                }

            if (byGroup.Count == 0) continue;

            var groupInstances = byGroup
                .Select(kv =>
                {
                    groupByClr.TryGetValue(kv.Key, out var meta);
                    return new LoadedCompletenessGroupInstance
                    {
                        GroupClrType = kv.Key,
                        Name = meta?.Name ?? new LocaleString(NameHumanizer.Humanize(kv.Key.Name)),
                        Weight = meta?.Weight ?? 0,
                        SortOrder = meta?.SortOrder ?? 0,
                        Rules = kv.Value
                            .OrderBy(r => r.Index)
                            .ThenBy(r => r.FieldId, StringComparer.Ordinal)
                            .ThenBy(r => r.Kind)
                            .ToList(),
                    };
                })
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.GroupClrType.Name, StringComparer.Ordinal)
                .ToList();

            definitions.Add(new LoadedCompletenessDefinition
            {
                EntityTypeId = et.EntityTypeId,
                Groups = groupInstances,
            });
        }

        return definitions;
    }

    private static LoadedCompletenessRule BuildCompletenessRule(
        string entityTypeId, string fieldId, CompletenessRuleAttribute attr,
        IReadOnlyDictionary<Type, string> linkIdByClr) => new()
        {
            EntityTypeId = entityTypeId,
            FieldId = fieldId,
            Kind = attr.Kind,
            Weight = attr.Weight,
            Index = attr.Index,
            Name = attr.Name is null ? null : new LocaleString(attr.Name),
            Value = attr switch
            {
                ContainsValueAttribute c => c.Value,
                ExactMatchAttribute e => e.Expected,
                _ => null,
            },
            LinkTypeId = attr is LinkTypeExistsAttribute l
                ? linkIdByClr.GetValueOrDefault(l.LinkType, l.LinkType.Name)
                : null,
            Operator = attr is NumberEvaluationAttribute n ? n.Operator : null,
            Number = (attr as NumberEvaluationAttribute)?.Value,
        };

    private static List<LoadedSpecificationTemplate> LoadSpecificationTemplates(Type[] types) =>
        ConcreteSubclassesOf<SpecificationTemplate>(types)
            .Select(t =>
            {
                var s = Instantiate<SpecificationTemplate>(t);
                return new LoadedSpecificationTemplate
                {
                    ClrType = t,
                    TemplateId = s.TemplateId,
                    Name = s.Name,
                    Description = s.Description,
                    CategoryClrTypes = s.Categories.ToArray(),
                    EntityTypeClrTypes = s.EntityTypes.ToArray(),
                };
            })
            .ToList();

    /// <summary>
    /// Scans <paramref name="assembly"/> for any public static property or field exposing an
    /// <see cref="IEnumerable{Language}"/> (conventionally <c>Languages.All</c>). The scaffolder
    /// emits a <c>public static readonly Language[] All</c>, so both members are inspected.
    /// </summary>
    private static List<Language> LoadLanguages(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        var fromProperties = assembly.GetTypes()
            .SelectMany(t => t.GetProperties(flags))
            .Where(p => typeof(IEnumerable<Language>).IsAssignableFrom(p.PropertyType))
            .Select(p => p.GetValue(null) as IEnumerable<Language>);

        var fromFields = assembly.GetTypes()
            .SelectMany(t => t.GetFields(flags))
            .Where(f => typeof(IEnumerable<Language>).IsAssignableFrom(f.FieldType))
            .Select(f => f.GetValue(null) as IEnumerable<Language>);

        return fromProperties.Concat(fromFields).FirstOrDefault(seq => seq is not null)?.ToList()
               ?? [];
    }
}
