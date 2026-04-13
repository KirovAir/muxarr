using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Muxarr.Data.Extensions;

public static class PagingExtensions
{
    public static async Task<(IQueryable<T> Data, int Total, int TotalPages)> FindPagedAsync<T>(
        this IQueryable<T> collection,
        int pageNumber,
        int pageSize,
        bool noTracking = false) where T : class
    {
        if (noTracking)
        {
            collection = collection.AsNoTracking();
        }

        var count = await collection.CountAsync();
        var totalPages = Convert.ToInt32(Math.Ceiling(count / (double)pageSize));
        var offset = (pageNumber - 1) * pageSize;
        var res = collection
            .Skip(offset)
            .Take(pageSize);

        return (res, count, totalPages);
    }

    public static async Task<List<T>> ToPagedListAsync<T>(
        this IQueryable<T> collection,
        int pageNumber,
        int pageSize,
        bool noTracking = false) where T : class
    {
        if (noTracking)
        {
            collection = collection.AsNoTracking();
        }

        var offset = (pageNumber - 1) * pageSize;
        var res = collection
            .Skip(offset)
            .Take(pageSize);

        return await res.ToListAsync();
    }

    public static IQueryable<T> Sort<T>(this IQueryable<T> query, string? sortProperty, bool? ascending = true)
    {
        if (!string.IsNullOrEmpty(sortProperty))
        {
            var parameter = Expression.Parameter(typeof(T), "f");
            var property = Expression.Property(parameter, sortProperty);
            var conversion = Expression.Convert(property, typeof(object));
            var lambda = Expression.Lambda<Func<T, object>>(conversion, parameter);

            query = ascending.GetValueOrDefault(true) ? query.OrderBy(lambda) : query.OrderByDescending(lambda);
        }

        return query;
    }

    public static IQueryable<T> WhereDynamic<T>(this IQueryable<T> sourceList,
        Expression<Func<T, IComparable>>[] searchProperties, string query)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var expressions = GetWhereExpressions(searchProperties, parameter, query).ToList();

        if (expressions.Count == 0)
        {
            return sourceList;
        }

        var expression = expressions[0];
        for (var i = 1; i < expressions.Count; i++)
        {
            expression = Expression.Or(expression, expressions[i]);
        }

        var lambda = Expression.Lambda<Func<T, bool>>(expression, parameter);
        return sourceList.Where(lambda);
    }

    public static IEnumerable<Expression> GetWhereExpressions<T>(Expression<Func<T, IComparable>>[] searchProperties,
        ParameterExpression parameter, string query)
    {
        foreach (var propertyExpression in searchProperties)
        {
            var memberAccess = Expression.PropertyOrField(parameter, propertyExpression.GetPropertyName());
            var memberType = memberAccess.Type;

            Expression? expr = null;
            if (memberType == typeof(int))
            {
                if (int.TryParse(query, out var intVal))
                {
                    expr = Expression.Equal(memberAccess, Expression.Constant(intVal));
                }
            }
            else if (memberType == typeof(bool))
            {
                if (bool.TryParse(query, out var boolVal))
                {
                    expr = Expression.Equal(memberAccess, Expression.Constant(boolVal));
                }
            }
            else if (memberType == typeof(string))
            {
                expr = Expression.Call(memberAccess, "Contains", null, Expression.Constant(query, typeof(string)));
            }
            else if (memberType.IsEnum)
            {
                if (Enum.TryParse(memberType, query, true, out var enumVal))
                {
                    expr = Expression.Equal(memberAccess, Expression.Constant(enumVal));
                }
            }

            if (expr != null)
            {
                yield return expr;
            }
        }
    }

    public static string GetPropertyName<T>(this Expression<Func<T, IComparable>> prop)
    {
        PropertyInfo property;
        string propertyName;
        if (prop.Body is MemberExpression expression)
        {
            property = (PropertyInfo)expression.Member;
            propertyName = property.Name;
        }
        else
        {
            var op = ((UnaryExpression)prop.Body).Operand;
            property = (PropertyInfo)((MemberExpression)op).Member;
            propertyName = property.Name;
        }

        return propertyName;
    }
}
