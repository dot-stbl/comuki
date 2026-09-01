namespace Comuki.Shared.Contracts.Plans;

/// <summary>
/// Pure plan validation shared by the brain's <c>emit_plan</c> tool and
/// any future caller: shape checks (non-empty ids/titles/briefs/profile
/// keys, unique node ids), referential integrity of edges and DAG
/// acyclicity. No I/O, no dependencies — safe for Contracts.
/// </summary>
public static class PlanValidator
{
    /// <summary>Validates a plan; every rule broken contributes one error.</summary>
    /// <param name="plan"></param>
    public static PlanValidationResult Validate(Plan? plan)
    {
        if (plan is null)
        {
            return new PlanValidationResult(["plan is required"]);
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(plan.Summary))
        {
            errors.Add("summary must not be empty");
        }

        if (plan.Nodes.Count == 0)
        {
            errors.Add("plan must contain at least one node");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in plan.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add("node id must not be empty");
            }
            else if (!ids.Add(node.Id))
            {
                errors.Add($"node id '{node.Id}' is duplicated");
            }

            if (string.IsNullOrWhiteSpace(node.Title))
            {
                errors.Add($"node '{node.Id}' title must not be empty");
            }

            if (string.IsNullOrWhiteSpace(node.ProfileKey))
            {
                errors.Add($"node '{node.Id}' profile key must not be empty");
            }

            if (string.IsNullOrWhiteSpace(node.Brief))
            {
                errors.Add($"node '{node.Id}' brief must not be empty");
            }
        }

        foreach (var edge in plan.Edges)
        {
            if (!ids.Contains(edge.From))
            {
                errors.Add($"edge references unknown node '{edge.From}'");
            }

            if (!ids.Contains(edge.To))
            {
                errors.Add($"edge references unknown node '{edge.To}'");
            }

            if (edge.From == edge.To)
            {
                errors.Add($"edge '{edge.From}' -> '{edge.To}' is a self-loop");
            }
        }

        var cycle = FindCycleNode(plan);
        if (cycle is { } cycleNode)
        {
            errors.Add($"plan graph must be acyclic (cycle passes through '{cycleNode}')");
        }

        return errors.Count == 0 ? PlanValidationResult.Valid : new PlanValidationResult(errors);
    }

    /// <summary>Returns one node on a cycle, or null when the graph is a DAG. Tolerates duplicate ids (Validate reports those separately).</summary>
    /// <param name="plan"></param>
    public static string? FindCycleNode(Plan plan)
    {
        // first-wins on duplicate ids: Validate() already reports them as
        // errors — crashing here would mask those errors
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in plan.Nodes)
        {
            if (!outgoing.ContainsKey(node.Id))
            {
                outgoing[node.Id] = [];
                state[node.Id] = 0;
            }
        }

        foreach (var edge in plan.Edges)
        {
            if (outgoing.TryGetValue(edge.From, out var targets)
                && outgoing.ContainsKey(edge.To)
                && edge.From != edge.To)
            {
                targets.Add(edge.To);
            }
        }

        // 0 = unvisited, 1 = on the current path, 2 = done
        foreach (var node in plan.Nodes)
        {
            if (state[node.Id] != 0)
            {
                continue;
            }

            var onCycle = Visit(node.Id, outgoing, state);
            if (onCycle is { } cycleNode)
            {
                return cycleNode;
            }
        }

        return null;

        static string? Visit(string id, Dictionary<string, List<string>> outgoing, Dictionary<string, int> state)
        {
            state[id] = 1;
            foreach (var next in outgoing[id])
            {
                switch (state[next])
                {
                    case 1:
                        return next;
                    case 0:
                        var nested = Visit(next, outgoing, state);
                        if (nested is { } found)
                        {
                            return found;
                        }

                        break;
                }
            }

            state[id] = 2;
            return null;
        }
    }
}
