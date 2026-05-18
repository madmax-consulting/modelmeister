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

        return new LoadedModel
        {
            EntityTypes = LoadEntityTypes(types),
            Cvls = LoadCvls(types),
            Categories = LoadCategories(types),
            Fieldsets = LoadFieldsets(types),
            LinkTypes = LoadLinkTypes(types),
            Roles = LoadRoles(types),
            Permissions = LoadPermissions(types),
            CompletenessGroups = LoadCompletenessGroups(types),
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
        var name = field.Name ?? new LocaleString(prop.Name);

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
        };
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
