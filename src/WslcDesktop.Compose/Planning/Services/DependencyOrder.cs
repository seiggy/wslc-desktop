
namespace WslcDesktop.Compose.Planning.Services;

/// <summary>Orders services so dependencies start first.</summary>
public static partial class DependencyOrder
{
    public static IReadOnlyList<string> TopologicalOrder(IReadOnlyList<string> names, Dictionary<string, List<string>> deps, List<string> errors)
    {
        var order = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var cyclic = false;

        void Visit(string n)
        {
            if (state.GetValueOrDefault(n) == 2)
            {
                return;
            }

            if (state.GetValueOrDefault(n) == 1)
            {
                cyclic = true;
                return;
            }

            state[n] = 1;
            foreach (var d in deps.GetValueOrDefault(n) ?? [])
            {
                Visit(d);
            }

            state[n] = 2;
            order.Add(n);
        }

        foreach (var n in names)
        {
            Visit(n);
        }

        if (cyclic)
        {
            errors.Add("The services' depends_on entries form a cycle.");
        }

        return order;
    }
}
