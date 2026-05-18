namespace ModelMeister.Model.Expressions;

/// <summary>
/// Catalogue of model-symbol references reachable from a rooted expression. Built by walking the
/// AST and inspecting <see cref="FunctionExpr{T}"/> calls whose names target inriver model symbols.
/// </summary>
public sealed class ExpressionRefs
{
    public HashSet<string> FieldIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> LinkTypeIds { get; } = new(StringComparer.Ordinal);
    public HashSet<(string CvlId, string Key)> CvlValues { get; } = [];
    public HashSet<string> CvlIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CategoryIds { get; } = new(StringComparer.Ordinal);
}

/// <summary>Walks an expression tree collecting an <see cref="ExpressionRefs"/> catalogue.</summary>
public static class ExpressionRefCollector
{
    public static ExpressionRefs Collect(Expr root)
    {
        var refs = new ExpressionRefs();
        new Walker(refs).Walk(root);
        return refs;
    }

    /// <summary>Walker implementation that fills <see cref="ExpressionRefs"/> from function-call nodes.</summary>
    private sealed class Walker(ExpressionRefs refs) : ExpressionWalker
    {
        protected override void OnNode(Expr node)
        {
            var type = node.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(FunctionExpr<>)) return;

            var name = (string)type.GetProperty(nameof(FunctionExpr<int>.Name))!.GetValue(node)!;
            var args = (Expr[])type.GetProperty(nameof(FunctionExpr<int>.Args))!.GetValue(node)!;

            switch (name)
            {
                case "FIELDVALUE":
                case "FIELDCVLVALUE":
                case "LOCALESTRINGVALUE":
                    if (args.Length > 0 && TryGetStringLiteral(args[0], out var fid))
                        refs.FieldIds.Add(fid);
                    break;

                case "LINKEDENTITIES":
                case "FIRSTLINKEDENTITY":
                    if (args.Length > 0 && TryGetStringLiteral(args[0], out var lid))
                        refs.LinkTypeIds.Add(lid);
                    break;

                case "CVLVALUE":
                    if (args.Length >= 2
                        && TryGetStringLiteral(args[0], out var cid)
                        && TryGetStringLiteral(args[1], out var key))
                    {
                        refs.CvlValues.Add((cid, key));
                        refs.CvlIds.Add(cid);
                    }
                    break;

                case "FIELDVALUES":
                    if (args.Length >= 2 && TryGetStringLiteral(args[1], out var catId))
                        refs.CategoryIds.Add(catId);
                    break;
            }
        }

        private static bool TryGetStringLiteral(Expr e, out string value)
        {
            if (e is LiteralExpr<string> lit)
            {
                value = lit.Value;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
