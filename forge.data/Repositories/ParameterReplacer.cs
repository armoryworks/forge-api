using System.Linq.Expressions;

namespace Forge.Data.Repositories;

internal sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == from ? to : base.VisitParameter(node);

    public static Expression<Func<TSource, TResult>> Compose<TSource, TMiddle, TResult>(
        Expression<Func<TSource, TMiddle>> inner, Expression<Func<TMiddle, TResult>> outer)
    {
        var body = new ParameterReplacer(outer.Parameters[0], inner.Body).Visit(outer.Body);
        return Expression.Lambda<Func<TSource, TResult>>(body, inner.Parameters);
    }
}
