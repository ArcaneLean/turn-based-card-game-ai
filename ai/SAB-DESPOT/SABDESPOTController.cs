using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class SABDESPOTController : IAIController
{
    public int ScenarioCount { get; }
    public int NodeBudget { get; }
    public PersonalityFSM PersonalityFSM { get; }
    public float Timeout { get; }
    public string DOTSavePath { get; }

    private int _nodeCount;
    private int _dotFileCounter = 0;
    private SABDESPOTNode _root;

    public SABDESPOTController(int scenarioCount, int nodeBudget, PersonalityFSM personalityFSM, float timeout = 0f, string dotSavePath = "")
    {
        ScenarioCount = scenarioCount;
        NodeBudget = nodeBudget;
        PersonalityFSM = personalityFSM;
        Timeout = timeout;
        DOTSavePath = dotSavePath;
    }

    public Task<CombatMove> GetMove(GameState state, CombatPlayer player)
    {
        PersonalityFSM.Update(state);

        _nodeCount = 1;

        List<GameState> scenarios = GenerateScenarios(state);

        _root = new()
        {
            Scenarios = scenarios
        };

        List<SABDESPOTNode> leaves = ExpandRecursively(_root, player.ID);

        EvaluateLeaves(leaves, player.ID);

        Backpropagation(leaves);

        CombatMove bestMove = _root.BestMove;

        if (bestMove.Type == MoveType.Pass && player.Energy >= player.ChainStats.EquipmentCost && player.Equipment.Count > 0 && state.PreviousTurnPassed)
            bestMove = new(MoveType.DrawEquipment);

        if (DOTSavePath != "")
            AIUtilities.SaveDOTString(DOTSavePath, GetDOTString(), _dotFileCounter++, "SABDESPOT");

        return Task.FromResult(bestMove);
    }

    private void Backpropagation(List<SABDESPOTNode> leaves)
    {
        HashSet<SABDESPOTNode> seen = new();
        Queue<SABDESPOTNode> queue = new();

        foreach (SABDESPOTNode parent in leaves.Select(leaf => leaf.Parent).ToHashSet())
        {
            queue.Enqueue(parent);
            seen.Add(parent);
        }

        while (queue.Count > 0)
        {
            SABDESPOTNode node = queue.Dequeue();

            Dictionary<CombatMove, List<float>> moveValues = new();

            foreach (SABDESPOTNode child in node.Children)
            {
                if (!moveValues.TryGetValue(child.Move, out List<float> values))
                    values = new();

                values.AddRange(Enumerable.Repeat(child.Value, child.Scenarios.Count));
                moveValues[child.Move] = values;
            }

            (CombatMove bestMove, float bestValue) = moveValues.Select(kvp => (kvp.Key, AIUtilities.GetRiskAdjustedScore(kvp.Value, PersonalityFSM.CurrentProfile))).OrderByDescending(tuple => tuple.Item2).First();

            node.Value = bestValue;
            node.BestMove = bestMove;

            if (node.Parent != null && !seen.Contains(node.Parent))
            {
                queue.Enqueue(node.Parent);
                seen.Add(node.Parent);
            }
        }
    }

    private void EvaluateLeaves(List<SABDESPOTNode> leaves, PlayerID playerID)
    {
        foreach (SABDESPOTNode leaf in leaves)
        {
            leaf.Value = PersonalityFSM.CurrentProfile.ScoreState(leaf.Scenarios[0], playerID);
        }
    }

    private List<GameState> GenerateScenarios(GameState state, bool minusOne = false)
    {
        List<GameState> scenarios = new();
        for (int i = minusOne ? 1 : 0; i < ScenarioCount; i++)
        {
            GameState clone = state.Clone();
            clone.ActivePlayer.ShuffleDeck();
            clone.ActivePlayer.ShuffleEquipmentDeck();
            scenarios.Add(clone);
        }

        return scenarios;
    }

    private List<SABDESPOTNode> ExpandRecursively(SABDESPOTNode root, PlayerID playerID)
    {
        Queue<SABDESPOTNode> nodeQueue = new();
        nodeQueue.Enqueue(root);

        List<SABDESPOTNode> leaves = new();

        using CancellationTokenSource cts = Timeout > 0f
            ? new(TimeSpan.FromSeconds(Timeout))
            : new();

        while (_nodeCount < NodeBudget && nodeQueue.Count > 0)
        {
            if (cts.Token.IsCancellationRequested)
                break;

            SABDESPOTNode node = nodeQueue.Dequeue();

            if (node.Scenarios[0].IsCombatOver || node.Scenarios[0].ActivePlayer.ID != playerID)
            {
                leaves.Add(node);
                continue;
            }

            try
            {
                List<SABDESPOTNode> children = Expand(node, playerID, cts.Token);

                if (children.Count == 0)
                    leaves.Add(node);

                foreach (SABDESPOTNode child in children)
                    nodeQueue.Enqueue(child);

                _nodeCount += children.Count;
            }
            catch (OperationCanceledException)
            {
                leaves.Add(node);
            }
        }

        leaves.AddRange(nodeQueue);

        return leaves;
    }

    private List<SABDESPOTNode> Expand(SABDESPOTNode node, PlayerID playerID, CancellationToken token)
    {
        List<SABDESPOTNode> children = new();

        GameState firstScenario = node.Scenarios[0];

        foreach (CombatMove move in AIUtilities.GenerateCombatMoves(firstScenario))
        {
            token.ThrowIfCancellationRequested();

            (bool deterministic, GameState resultingState) = AIUtilities.CheckMoveIsDeterministic(firstScenario, move, passTurn: false);

            if (deterministic)
            {
                List<GameState> scenarios = GenerateScenarios(resultingState, true);
                scenarios.Add(resultingState);

                children.Add(new() { Scenarios = scenarios, Move = move });
            }
            else
            {
                List<GameState> scenarios = new() { resultingState };

                foreach (GameState scenario in node.Scenarios.Skip(1))
                {
                    token.ThrowIfCancellationRequested();

                    GameState clone = scenario.Clone();
                    clone.ApplyMove(move, passTurn: false).GetAwaiter().GetResult();
                    scenarios.Add(clone);
                }

                children.AddRange(GroupScenarios(scenarios, playerID).Select(node =>
                {
                    node.Move = move;
                    return node;
                }));
            }
        }

        node.Children = children;

        foreach (SABDESPOTNode child in children)
            child.Parent = node;

        return children;
    }

    private List<SABDESPOTNode> GroupScenarios(List<GameState> scenarios, PlayerID playerID)
    {
        return scenarios
            .GroupBy(scenario => new InformationGameState(scenario, playerID))
            .Select(group => new SABDESPOTNode
            {
                Scenarios = group.ToList()
            })
            .ToList();
    }

    public Task<List<Card>> ChooseMulligan(GameState state, CombatPlayer player) => Task.FromResult(new List<Card>());

    public Task<List<Card>> ChooseDiscardToMax(GameState state, CombatPlayer player) =>
        Task.FromResult(player.Hand.GetRange(0, player.Hand.Count - GlobalNumerals.maxCardsInHand));

    public Task<List<Card>> SelectCards(GameState state, CombatPlayer player, List<Card> cards, int amount, bool mustReachAmount) =>
        Task.FromResult(mustReachAmount ? cards.GetRange(0, amount) : new List<Card>());

    private string GetDOTString()
    {
        StringBuilder sb = new();
        sb.AppendLine("digraph SABDESPOT {");
        sb.AppendLine("  node [shape=box, style=filled, color=lightgrey];");
        sb.AppendLine("  rankdir=TB;");

        int nodeId = 0;
        Dictionary<SABDESPOTNode, string> nodeNames = new();

        string GetNodeName() => $"node{nodeId++}";

        void WriteNode(SABDESPOTNode node)
        {
            if (nodeNames.ContainsKey(node))
                return;

            string name = GetNodeName();
            nodeNames[node] = name;

            // Base node label
            string label = $"Scenarios: {node.Scenarios?.Count ?? 0}\\n" +
                        $"Value: {node.Value:0.00}";

            sb.AppendLine($"  {name} [label=\"{label}\", style=filled, color=lightgrey];");

            if (node.Children != null && node.Children.Count > 0)
            {
                // Collect values per move (like in the algorithm)
                Dictionary<CombatMove, List<float>> moveValues = new();
                Dictionary<CombatMove, int> moveScenarioCounts = new();

                foreach (SABDESPOTNode child in node.Children)
                {
                    if (child.Move == null)
                        continue;

                    if (!moveValues.TryGetValue(child.Move, out List<float> values))
                    {
                        values = new();
                        moveValues[child.Move] = values;
                        moveScenarioCounts[child.Move] = 0;
                    }

                    int count = child.Scenarios?.Count ?? 0;
                    values.AddRange(Enumerable.Repeat(child.Value, count));
                    moveScenarioCounts[child.Move] += count;
                }

                foreach (KeyValuePair<CombatMove, List<float>> kvp in moveValues)
                {
                    CombatMove move = kvp.Key;
                    List<float> values = kvp.Value;
                    int totalScenarios = moveScenarioCounts[move];

                    float riskAdjusted = AIUtilities.GetRiskAdjustedScore(values, PersonalityFSM.CurrentProfile);

                    string moveNodeName = GetNodeName();
                    string moveLabel = $"{move.ToShortString()}\\n" +
                                    $"Scenarios: {totalScenarios}\\n" +
                                    $"Value: {riskAdjusted:0.00}";

                    bool isBest = node.BestMove != null && move.Equals(node.BestMove);

                    string moveColor = isBest ? "green" : "lightblue";
                    sb.AppendLine($"  {moveNodeName} [label=\"{moveLabel}\", style=filled, color={moveColor}];");

                    // Connect parent → move node
                    string edgeAttrs = isBest ? " [color=green, penwidth=2]" : "";
                    sb.AppendLine($"  {name} -> {moveNodeName}{edgeAttrs};");

                    // Connect move node → each child with this move
                    foreach (SABDESPOTNode child in node.Children.Where(c => move.Equals(c.Move)))
                    {
                        WriteNode(child);
                        sb.AppendLine($"  {moveNodeName} -> {nodeNames[child]};");
                    }
                }
            }
        }

        if (_root != null)
            WriteNode(_root);

        sb.AppendLine("}");
        return sb.ToString();
    }

    public string GetName() => "SABDESPOT";

    public Dictionary<string, object> GetParameters() => new()
    {
        ["ScenarioCount"] = ScenarioCount,
        ["NodeBudget"] = NodeBudget,
        ["Timeout"] = Timeout
    };

    public PersonalityProfile GetProfile() => PersonalityFSM.CurrentProfile;
}