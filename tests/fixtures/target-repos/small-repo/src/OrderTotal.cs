namespace SmallRepo;

/// <summary>
/// Fixture bug for the <c>add-null-check</c> scenario
/// (tests/fixtures/scenarios/small-repo/add-null-check.scenario.yaml):
/// <see cref="Calculate"/> dereferences <paramref name="orders"/> without a
/// null guard.
/// </summary>
public static class OrderTotal
{
    /// <summary>Sums every order's amount. Throws NullReferenceException today when <paramref name="orders"/> is null — that is the bug the scenario's ticket asks a worker to fix.</summary>
    /// <param name="orders">The orders to total; must not be null.</param>
    public static decimal Calculate(IEnumerable<Order>? orders)
    {
        decimal total = 0;
        foreach (var order in orders!)
        {
            total += order.Amount;
        }

        return total;
    }
}

/// <summary>One order line for <see cref="OrderTotal.Calculate"/>.</summary>
/// <param name="Amount">The order's amount.</param>
public sealed record Order(decimal Amount);
