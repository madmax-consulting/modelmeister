using System.Linq.Expressions;
using System.Reflection;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Expressions;

/// <summary>
/// Static factory exposing every inriver Expression Engine function with strong typing.
/// One method per function listed in <c>docs/expression-engine.txt</c>. The helpers compose
/// <see cref="FunctionExpr{T}"/>/<see cref="LiteralExpr{T}"/> nodes; rendering happens in
/// <see cref="Expr.RenderTopLevel"/>.
/// </summary>
public static class Ex
{
    // -------- Helpers --------

    private static Expr Lit<T>(T v) => new LiteralExpr<T>(v);

    private static Expr<TResult> Call<TResult>(string name, params Expr[] args) =>
        new FunctionExpr<TResult>(name, args);

    /// <summary>Builds a function call whose first arg is required and trailing args are optional.</summary>
    private static Expr<TResult> CallOptional<TResult>(string name, Expr required, Expr? optional) =>
        optional is null ? Call<TResult>(name, required) : Call<TResult>(name, required, optional);

    // -------- Math --------

    public static Expr<double> Abs(Expr<double> x) => Call<double>("ABS", x);
    public static Expr<double> Average(params Expr<double>[] values) => Call<double>("AVERAGE", values);
    public static Expr<double> Ceiling(Expr<double> x, Expr<double> multiple) => Call<double>("CEILING", x, multiple);

    private static readonly HashSet<string> SupportedUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "m", "in", "ft", "yd", "mi",
        "g", "grain", "ozm", "lbm", "stone", "ton",
        "l", "oz", "pt", "qt", "gal",
    };

    public static Expr<double> Convert(Expr<double> value, string fromUnit, string toUnit)
    {
        if (!SupportedUnits.Contains(fromUnit))
            throw new ArgumentException($"Unsupported CONVERT unit '{fromUnit}'.", nameof(fromUnit));
        if (!SupportedUnits.Contains(toUnit))
            throw new ArgumentException($"Unsupported CONVERT unit '{toUnit}'.", nameof(toUnit));

        return Call<double>("CONVERT", value, Lit(fromUnit), Lit(toUnit));
    }

    public static Expr<double> Floor(Expr<double> x, Expr<double> multiple) => Call<double>("FLOOR", x, multiple);
    public static Expr<double> Max(params Expr<double>[] values) => Call<double>("MAX", values);
    public static Expr<double> Min(params Expr<double>[] values) => Call<double>("MIN", values);
    public static Expr<double> Pi { get; } = Call<double>("PI");
    public static Expr<double> Power(Expr<double> baseValue, Expr<double> exponent) => Call<double>("POWER", baseValue, exponent);
    public static Expr<double> Rand() => Call<double>("RAND");
    public static Expr<double> RandBetween(Expr<double> min, Expr<double> max) => Call<double>("RANDBETWEEN", min, max);
    public static Expr<double> Round(Expr<double> x, Expr<int> decimals) => Call<double>("ROUND", x, decimals);
    public static Expr<double> RoundUp(Expr<double> x, Expr<int> decimals) => Call<double>("ROUNDUP", x, decimals);
    public static Expr<double> RoundDown(Expr<double> x, Expr<int> decimals) => Call<double>("ROUNDDOWN", x, decimals);
    public static Expr<double> Sqrt(Expr<double> x) => Call<double>("SQRT", x);

    // -------- DateTime --------

    public static Expr<DateTime> DateTime(int year, int? month = null, int? day = null,
        int? hour = null, int? minute = null, int? second = null)
    {
        var parts = new int?[] { year, month, day, hour, minute, second };
        var args = parts
            .TakeWhile(p => p.HasValue)
            .Select(p => Lit(p!.Value))
            .ToArray();
        return Call<DateTime>("DATETIME", args);
    }

    public static Expr<double> DateTimeDif(Expr<DateTime> a, Expr<DateTime> b, DateTimeUnit unit) =>
        Call<double>("DATETIMEDIF", a, b, Lit(UnitToken(unit)));

    public static Expr<DateTime> DateTimeAdd(Expr<DateTime> dt, DateTimeUnit unit, Expr<int> amount) =>
        Call<DateTime>("DATETIMEADD", dt, Lit(UnitToken(unit)), amount);

    public static Expr<DateTime> EvaluationDateTime(string? timezone = null) =>
        timezone is null
            ? Call<DateTime>("EVALUATIONDATETIME")
            : Call<DateTime>("EVALUATIONDATETIME", Lit(timezone));

    private static string UnitToken(DateTimeUnit unit) => unit switch
    {
        DateTimeUnit.Years => "Y",
        DateTimeUnit.Months => "M",
        DateTimeUnit.Days => "D",
        DateTimeUnit.Hours => "h",
        DateTimeUnit.Minutes => "m",
        DateTimeUnit.Seconds => "s",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    // -------- LocaleString --------

    public static Expr<LocaleString> LocaleString(params (string lang, string text)[] pairs)
    {
        var args = pairs
            .SelectMany(p => new Expr[] { Lit(p.lang), Lit(p.text) })
            .ToArray();
        return Call<LocaleString>("LOCALESTRING", args);
    }

    public static Expr<string> LocaleStringValue(string fieldTypeId, string languageCode, Expr<EntityRef>? entity = null)
    {
        Expr[] args = entity is null
            ? [Lit(fieldTypeId), Lit(languageCode)]
            : [Lit(fieldTypeId), Lit(languageCode), entity];
        return Call<string>("LOCALESTRINGVALUE", args);
    }

    public static Expr<LocaleString> LsConcatenate(params Expr[] args) => Call<LocaleString>("LSCONCATENATE", args);
    public static Expr<LocaleString> LsLeft(Expr<LocaleString> ls, Expr<int> count) => Call<LocaleString>("LSLEFT", ls, count);
    public static Expr<LocaleString> LsRight(Expr<LocaleString> ls, Expr<int> count) => Call<LocaleString>("LSRIGHT", ls, count);
    public static Expr<LocaleString> LsUpper(Expr<LocaleString> ls) => Call<LocaleString>("LSUPPER", ls);
    public static Expr<LocaleString> LsLower(Expr<LocaleString> ls) => Call<LocaleString>("LSLOWER", ls);

    /// <summary>LSGENERATE with closure-scoped <c>$LANG</c>. Returns a LocaleString built per-language.</summary>
    public static Expr<LocaleString> LsGenerate(Func<Expr<string>, Expr<LocaleString>> body)
    {
        var lang = new VarExpr<string>("$LANG");
        return Call<LocaleString>("LSGENERATE", body(lang));
    }

    public static Expr<string> LsExtract(Expr<LocaleString> ls, string langCode) =>
        Call<string>("LSEXTRACT", ls, Lit(langCode));

    public static Expr<LocaleString> LsRegexReplace(Expr<LocaleString> original, string pattern, string replacement) =>
        Call<LocaleString>("LSREGEXREPLACE", original, Lit(pattern), Lit(replacement));

    // -------- String --------

    public static Expr<string> Char(Expr<int> code) => Call<string>("CHAR", code);
    public static Expr<string> Concatenate(params Expr<string>[] parts) => Call<string>("CONCATENATE", parts);

    /// <summary>Lifts a plain string into <c>Expr&lt;string&gt;</c>. Use when implicit conversion is ambiguous.</summary>
    public static Expr<string> S(string value) => new LiteralExpr<string>(value);

    public static Expr<int> Len(Expr<string> s) => Call<int>("LEN", s);
    public static Expr<string> Left(Expr<string> s, Expr<int>? count = null) => CallOptional<string>("LEFT", s, count);
    public static Expr<string> Right(Expr<string> s, Expr<int>? count = null) => CallOptional<string>("RIGHT", s, count);
    public static Expr<double> Number(Expr<string> s) => Call<double>("NUMBER", s);
    public static Expr<string> Text(Expr value) => Call<string>("TEXT", value);

    public static Expr<string> TextJoin(string delimiter, bool ignoreEmpty, params Expr[] inputs)
    {
        var args = new Expr[] { Lit(delimiter), Lit(ignoreEmpty) }.Concat(inputs).ToArray();
        return Call<string>("TEXTJOIN", args);
    }

    public static Expr<List<string>> TextSplit(Expr<string> input, string delimiter, bool ignoreEmpty = false) =>
        Call<List<string>>("TEXTSPLIT", input, Lit(delimiter), Lit(ignoreEmpty));

    public static Expr<bool> RegexTest(Expr<string> text, string pattern) => Call<bool>("REGEXTEST", text, Lit(pattern));

    public static Expr<string> RegexExtract(Expr<string> text, string pattern, int returnMode = 0) =>
        Call<string>("REGEXEXTRACT", text, Lit(pattern), Lit(returnMode));

    public static Expr<string> RegexReplace(Expr<string> text, string pattern, string replacement) =>
        Call<string>("REGEXREPLACE", text, Lit(pattern), Lit(replacement));

    public static Expr<string> Replace(Expr<string> text, Expr<int> start, Expr<int> count, Expr<string> newText) =>
        Call<string>("REPLACE", text, start, count, newText);

    public static Expr<int> Search(Expr<string> needle, Expr<string> haystack) =>
        Call<int>("SEARCH", needle, haystack);

    public static Expr<string> Substitute(Expr<string> text, Expr<string> oldText, Expr<string> newText, Expr<int>? instance = null) =>
        instance is null
            ? Call<string>("SUBSTITUTE", text, oldText, newText)
            : Call<string>("SUBSTITUTE", text, oldText, newText, instance);

    public static Expr<string> Trim(Expr<string> s) => Call<string>("TRIM", s);
    public static Expr<string> Proper(Expr<string> s) => Call<string>("PROPER", s);
    public static Expr<string> Upper(Expr<string> s) => Call<string>("UPPER", s);
    public static Expr<string> Lower(Expr<string> s) => Call<string>("LOWER", s);
    public static Expr<string> XmlValue(Expr<string> xml, string xpath) => Call<string>("XMLVALUE", xml, Lit(xpath));
    public static Expr<string> JsonValue(Expr<string> json, string jsonPath) => Call<string>("JSONVALUE", json, Lit(jsonPath));

    // -------- List --------

    public static Expr<List<T>> List<T>(params Expr<T>[] items) => Call<List<T>>("LIST", items);

    public static Expr<List<EntityRef>> LinkedEntities(string linkTypeId) =>
        Call<List<EntityRef>>("LINKEDENTITIES", Lit(linkTypeId));

    public static Expr<bool> Any<T>(Expr<List<T>> list, Func<Expr<T>, Expr<bool>> predicate) =>
        Call<bool>("ANY", list, predicate(new VarExpr<T>("$VALUE")));

    public static Expr<bool> All<T>(Expr<List<T>> list, Func<Expr<T>, Expr<bool>> predicate) =>
        Call<bool>("ALL", list, predicate(new VarExpr<T>("$VALUE")));

    public static Expr<T> First<T>(Expr<List<T>> list, Func<Expr<T>, Expr<bool>>? predicate = null) =>
        predicate is null
            ? Call<T>("FIRST", list)
            : Call<T>("FIRST", list, predicate(new VarExpr<T>("$VALUE")));

    public static Expr<int> Count<T>(Expr<List<T>> list, Func<Expr<T>, Expr<bool>>? predicate = null) =>
        predicate is null
            ? Call<int>("COUNT", list)
            : Call<int>("COUNT", list, predicate(new VarExpr<T>("$VALUE")));

    public static Expr<double> Sum(Expr<List<double>> list) => Call<double>("SUM", list);

    public static Expr<double> Sum<T>(Expr<List<T>> list, Func<Expr<T>, Expr<double>> projection) =>
        Call<double>("SUM", list, projection(new VarExpr<T>("$VALUE")));

    public static Expr<double> SumIf<T>(Expr<List<T>> list, Func<Expr<T>, Expr<bool>> condition, Func<Expr<T>, Expr<double>>? projection = null)
    {
        var v = new VarExpr<T>("$VALUE");
        return projection is null
            ? Call<double>("SUMIF", list, condition(v))
            : Call<double>("SUMIF", list, condition(v), projection(v));
    }

    public static Expr<List<TOut>> Map<TIn, TOut>(Expr<List<TIn>> list, Func<Expr<TIn>, Expr<TOut>> projection) =>
        Call<List<TOut>>("MAP", list, projection(new VarExpr<TIn>("$VALUE")));

    public static Expr<List<T>> Filter<T>(Expr<List<T>> list, Func<Expr<T>, Expr<bool>> predicate) =>
        Call<List<T>>("FILTER", list, predicate(new VarExpr<T>("$VALUE")));

    public static Expr<List<T>> Distinct<T>(Expr<List<T>> list) => Call<List<T>>("DISTINCT", list);

    // -------- Logic --------

    public static Expr<T> If<T>(Expr<bool> condition, Expr<T> whenTrue, Expr<T> whenFalse) =>
        Call<T>("IF", condition, whenTrue, whenFalse);

    public static Expr<T> Ifs<T>(params (Expr<bool> cond, Expr<T> value)[] cases)
    {
        var args = cases.SelectMany(c => new Expr[] { c.cond, c.value }).ToArray();
        return Call<T>("IFS", args);
    }

    public static Expr<TVal> Switch<TKey, TVal>(Expr<TKey> subject, (Expr<TKey> match, Expr<TVal> result)[] cases, Expr<TVal> defaultValue)
    {
        var args = new Expr[] { subject }
            .Concat(cases.SelectMany(c => new Expr[] { c.match, c.result }))
            .Concat([defaultValue])
            .ToArray();
        return Call<TVal>("SWITCH", args);
    }

    public static Expr<bool> And(params Expr<bool>[] terms) => Call<bool>("AND", terms);
    public static Expr<bool> Or(params Expr<bool>[] terms) => Call<bool>("OR", terms);
    public static Expr<bool> Not(Expr<bool> term) => Call<bool>("NOT", term);
    public static Expr<bool> IsError(Expr value) => Call<bool>("ISERROR", value);
    public static Expr<bool> IsNumber(Expr value) => Call<bool>("ISNUMBER", value);
    public static Expr<bool> IsEmpty(Expr value) => Call<bool>("ISEMPTY", value);
    public static Expr<string> Guid() => Call<string>("GUID");

    // -------- Inriver-specific --------

    public static Expr<EntityRef> FirstLinkedEntity(string linkTypeId) =>
        Call<EntityRef>("FIRSTLINKEDENTITY", Lit(linkTypeId));

    public static Expr<T> FieldValue<T>(string fieldTypeId, Expr<EntityRef>? entity = null) =>
        CallOptional<T>("FIELDVALUE", Lit(fieldTypeId), entity);

    public static Expr<List<object>> FieldValues(bool currentFieldSetOnly, string? categoryId = null) =>
        categoryId is null
            ? Call<List<object>>("FIELDVALUES", Lit(currentFieldSetOnly))
            : Call<List<object>>("FIELDVALUES", Lit(currentFieldSetOnly), Lit(categoryId));

    public static Expr<string> FieldSetId(Expr<EntityRef>? entity = null) =>
        entity is null ? Call<string>("FIELDSETID") : Call<string>("FIELDSETID", entity);

    public static Expr<LocaleString> CvlValue(string cvlId, string cvlKey) =>
        Call<LocaleString>("CVLVALUE", Lit(cvlId), Lit(cvlKey));

    public static Expr<LocaleString> FieldCvlValue(string fieldTypeId, Expr<EntityRef>? entity = null) =>
        CallOptional<LocaleString>("FIELDCVLVALUE", Lit(fieldTypeId), entity);

    public static Expr<string> SegmentId(Expr<EntityRef>? entity = null) =>
        entity is null ? Call<string>("SEGMENTID") : Call<string>("SEGMENTID", entity);

    public static Expr<string> SegmentName(Expr<string>? segmentId = null) =>
        segmentId is null ? Call<string>("SEGMENTNAME") : Call<string>("SEGMENTNAME", segmentId);

    // -------- Raw escape hatch --------

    public static Expr<T> Raw<T>(string text) => new RawExpr<T>(text);

    // -------- Strongly-typed selector / generic overloads --------
    //
    // These mirror the string-id factories above with selectors / type-parameters that point at
    // declared model classes. They produce identical render output — the lambda body is parsed
    // at construction time for its MemberExpression; no runtime evaluation, no Field instance
    // required (the owning entity stamp from ModelLoader hasn't happened yet at expression-build
    // time, so we deliberately don't rely on it). The string-id overloads above remain valid for
    // dynamic ids and as a fallback path.

    /// <summary>Field reference via property selector — equivalent to <c>FieldValue&lt;TData&gt;(entityId + propName)</c>.</summary>
    public static Expr<TData> FieldValue<TEntity, TData>(
        Expression<Func<TEntity, Field<TData>>> selector,
        Expr<EntityRef>? entity = null)
        where TEntity : EntityType, new()
        => FieldValue<TData>(FieldIdFromSelector(selector), entity);

    /// <summary>Field-cvl reference via property selector.</summary>
    public static Expr<LocaleString> FieldCvlValue<TEntity, TData>(
        Expression<Func<TEntity, Field<TData>>> selector,
        Expr<EntityRef>? entity = null)
        where TEntity : EntityType, new()
        => FieldCvlValue(FieldIdFromSelector(selector), entity);

    /// <summary>LocaleStringValue via property selector — TEntity entity field that holds a LocaleString.</summary>
    public static Expr<string> LocaleStringValue<TEntity>(
        Expression<Func<TEntity, Field<LocaleString>>> selector,
        string languageCode,
        Expr<EntityRef>? entity = null)
        where TEntity : EntityType, new()
        => LocaleStringValue(FieldIdFromSelector(selector), languageCode, entity);

    /// <summary>Linked entities via link-type class — equivalent to <c>LinkedEntities(linkTypeId)</c>.</summary>
    public static Expr<List<EntityRef>> LinkedEntities<TLinkType>() where TLinkType : LinkType, new()
        => LinkedEntities(Mm.LinkTypeId<TLinkType>());

    /// <summary>First linked entity via link-type class.</summary>
    public static Expr<EntityRef> FirstLinkedEntity<TLinkType>() where TLinkType : LinkType, new()
        => FirstLinkedEntity(Mm.LinkTypeId<TLinkType>());

    /// <summary>CVL entry lookup via CVL class — equivalent to <c>CvlValue(cvlId, key)</c>.</summary>
    public static Expr<LocaleString> CvlValue<TCvl>(string key) where TCvl : Cvl, new()
        => CvlValue(Mm.CvlId<TCvl>(), key);

    /// <summary>Field values filtered by category class.</summary>
    public static Expr<List<object>> FieldValues<TCategory>(bool currentFieldSetOnly)
        where TCategory : Category, new()
        => FieldValues(currentFieldSetOnly, Mm.CategoryId<TCategory>());

    /// <summary>
    /// Extracts the inriver field id from a property-selector lambda. Walks an optional Convert
    /// node (the C# compiler inserts one when the selector's return type is wider than
    /// <c>Field&lt;TData&gt;</c>, though for our selector signature it generally doesn't).
    /// </summary>
    /// <remarks>
    /// Uses <c>typeof(TEntity).Name</c> — the same convention <see cref="EntityType"/>'s ctor uses
    /// to default <see cref="EntityType.EntityTypeId"/>. We deliberately avoid <c>new TEntity()</c>
    /// here because property initializers on <c>TEntity</c> may themselves construct expressions
    /// (e.g. <c>DefaultExpression = Ex.Concatenate(Ex.FieldValue((Self r) =&gt; r.X))</c>), which
    /// would recurse infinitely. If an entity overrides <c>EntityTypeId</c> via init, callers
    /// must use the string-id overload of <c>FieldValue</c>.
    /// </remarks>
    private static string FieldIdFromSelector<TEntity, TField>(Expression<Func<TEntity, TField>> selector)
        where TEntity : EntityType, new()
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert, Operand: var inner }) body = inner;
        if (body is not MemberExpression { Member: PropertyInfo prop })
            throw new ArgumentException(
                "Selector must be a simple property access, e.g. r => r.MimeType", nameof(selector));
        return typeof(TEntity).Name + prop.Name;
    }
}
