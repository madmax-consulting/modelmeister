namespace ModelMeister.Model.Expressions;

/// <summary>
/// Visits every node in an expression tree. Used by the validator and parser-emitter. Subclasses
/// override <see cref="OnNode"/> to receive a callback per visit; traversal handles the recursion.
/// </summary>
public abstract class ExpressionWalker
{
    public void Walk(Expr root) => Visit(root);

    protected virtual void Visit(Expr node)
    {
        OnNode(node);

        foreach (var child in Children(node))
            Visit(child);
    }

    /// <summary>Returns the child expression nodes of <paramref name="node"/>. Literals/vars/raws are leaves.</summary>
    private static IEnumerable<Expr> Children(Expr node)
    {
        var type = node.GetType();
        if (!type.IsGenericType) return [];

        var def = type.GetGenericTypeDefinition();
        if (def == typeof(BinaryExpr<>))
        {
            var lhs = (Expr)type.GetProperty(nameof(BinaryExpr<int>.Lhs))!.GetValue(node)!;
            var rhs = (Expr)type.GetProperty(nameof(BinaryExpr<int>.Rhs))!.GetValue(node)!;
            return [lhs, rhs];
        }
        if (def == typeof(FunctionExpr<>))
        {
            return (Expr[])type.GetProperty(nameof(FunctionExpr<int>.Args))!.GetValue(node)!;
        }
        return [];
    }

    /// <summary>Invoked once per node, before its children are visited.</summary>
    protected abstract void OnNode(Expr node);
}
