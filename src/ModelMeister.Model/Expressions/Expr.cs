using System.Globalization;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Expressions;

/// <summary>
/// Untyped expression base. Useful for storage and erasure into homogeneous argument lists. The
/// concrete subclasses are <see cref="LiteralExpr{T}"/>, <see cref="BinaryExpr{T}"/>,
/// <see cref="FunctionExpr{T}"/>, <see cref="VarExpr{T}"/> and <see cref="RawExpr{T}"/>.
/// </summary>
public abstract class Expr
{
    /// <summary>Renders this node as inriver Expression-Engine text (without a leading <c>=</c>).</summary>
    public abstract string Render(RenderContext ctx);

    /// <summary>Renders the top-level expression, prepending <c>=</c> when not already present.</summary>
    public string RenderTopLevel(RenderContext? ctx = null)
    {
        var body = Render(ctx ?? new RenderContext());
        return body.StartsWith('=') ? body : $"={body}";
    }

    public sealed override string ToString() => RenderTopLevel();
}

/// <summary>
/// Typed expression node. <typeparamref name="T"/> is the static type the inriver evaluator will yield.
/// </summary>
public abstract class Expr<T> : Expr
{
    /// <summary>Implicitly lifts a literal value into a <see cref="LiteralExpr{T}"/>.</summary>
    public static implicit operator Expr<T>(T literal) => new LiteralExpr<T>(literal);

    public static Expr<T> operator +(Expr<T> a, Expr<T> b) => new BinaryExpr<T>("+", a, b);
    public static Expr<T> operator -(Expr<T> a, Expr<T> b) => new BinaryExpr<T>("-", a, b);
    public static Expr<T> operator *(Expr<T> a, Expr<T> b) => new BinaryExpr<T>("*", a, b);
    public static Expr<T> operator /(Expr<T> a, Expr<T> b) => new BinaryExpr<T>("/", a, b);

    public static Expr<bool> operator <(Expr<T> a, Expr<T> b) => new BinaryExpr<bool>("<", a, b);
    public static Expr<bool> operator >(Expr<T> a, Expr<T> b) => new BinaryExpr<bool>(">", a, b);
    public static Expr<bool> operator <=(Expr<T> a, Expr<T> b) => new BinaryExpr<bool>("<=", a, b);
    public static Expr<bool> operator >=(Expr<T> a, Expr<T> b) => new BinaryExpr<bool>(">=", a, b);

    /// <summary>Equality comparison. Named (not an operator) because <c>==</c> would conflict with reference equality.</summary>
    public static Expr<bool> Eq(Expr<T> a, Expr<T> b) => new BinaryExpr<bool>("=", a, b);
}

/// <summary>A literal value lifted into an expression.</summary>
public sealed class LiteralExpr<T>(T value) : Expr<T>
{
    public T Value { get; } = value;

    public override string Render(RenderContext ctx) => FormatLiteral(Value);

    internal static string FormatLiteral(object? v) => v switch
    {
        null => "NULL",
        bool b => b ? "TRUE" : "FALSE",
        string s => Quote(s),
        LocaleString ls => Quote(ls.DefaultValue),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        DateTime dt => $"DATETIME({dt.Year}, {dt.Month}, {dt.Day}, {dt.Hour}, {dt.Minute}, {dt.Second})",
        _ => Quote(v.ToString() ?? string.Empty),
    };

    /// <summary>Wraps <paramref name="s"/> in single quotes, escaping any embedded <c>'</c> as <c>''</c>.</summary>
    internal static string Quote(string s) => $"'{s.Replace("'", "''")}'";
}

/// <summary>A binary operation, rendered as <c>(lhs OP rhs)</c>.</summary>
public sealed class BinaryExpr<T>(string op, Expr lhs, Expr rhs) : Expr<T>
{
    public string Op { get; } = op;
    public Expr Lhs { get; } = lhs;
    public Expr Rhs { get; } = rhs;

    public override string Render(RenderContext ctx) => $"({Lhs.Render(ctx)}{Op}{Rhs.Render(ctx)})";
}

/// <summary>An inriver Expression-Engine function call.</summary>
public sealed class FunctionExpr<T>(string name, params Expr[] args) : Expr<T>
{
    public string Name { get; } = name;
    public Expr[] Args { get; } = args;

    public override string Render(RenderContext ctx) =>
        Args.Length == 0
            ? Name
            : $"{Name}({string.Join(", ", Args.Select(a => a.Render(ctx)))})";
}

/// <summary>A variable reference (e.g. closure-scoped <c>$VALUE</c> or <c>$LANG</c>).</summary>
public sealed class VarExpr<T>(string name) : Expr<T>
{
    public string Name { get; } = name;

    public override string Render(RenderContext ctx) => Name;
}

/// <summary>
/// Raw escape-hatch — emits its payload verbatim. Used by the parser when round-tripping unknown
/// future Expression Engine functions; also available to consumers as a last resort.
/// </summary>
public sealed class RawExpr<T>(string raw) : Expr<T>
{
    public string Raw { get; } = raw;

    public override string Render(RenderContext ctx) => Raw;
}

/// <summary>Mutable rendering context. Currently tracks loop nesting depth.</summary>
public sealed class RenderContext
{
    public int LoopDepth { get; set; }
}
