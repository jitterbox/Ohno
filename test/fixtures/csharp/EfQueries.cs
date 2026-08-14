using System.Linq;
using System.Collections.Generic;

public class Order { public int Id { get; set; } public int Total { get; set; } }

public static class EfQueries
{
    public static List<Order> LoadExpensive(IQueryable<Order> orders)
    {
        return orders.Where(o => o.Total > 100).ToList();
    }

    public static List<Order> LoadAfterAsEnumerable(IQueryable<Order> orders)
    {
        return orders.AsEnumerable().Where(o => o.Total > 100).ToList();
    }

    public static IQueryable<Order> BuildExpression(
        IQueryable<Order> orders,
        System.Linq.Expressions.Expression<System.Func<Order, bool>> pred)
    {
        return orders.Where(pred);
    }
}
