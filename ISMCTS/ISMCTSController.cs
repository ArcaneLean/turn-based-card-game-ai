using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

public class ISMCTSController : IAIController
{
    public PersonalityFSM PersonalityFSM { get; }
    public int Iterations { get; }
    public OpponentModel OpponentModel { get; }
    public int RolloutDepth { get; }
    public float Timeout { get; }
    public string DOTSavePath { get; }
    public float Exploration { get; }

    private ISMCTSNode _root;
    private int _dotFileCounter = 0;
    private int _nodeCounter = 0;

    public ISMCTSController(PersonalityFSM personalityFSM, int iterations, int rolloutDepth, float exploration, float timeout = 0f, string dotSavePath = "")
    {
        PersonalityFSM = personalityFSM;
        Iterations = iterations;
        RolloutDepth = rolloutDepth;
        Timeout = timeout;
        DOTSavePath = dotSavePath;
        OpponentModel = new();
        Exploration = exploration;
    }

    public Task<CombatMove> GetMove(GameState state, CombatPlayer player)
    {
        PersonalityFSM.Update(state);

        _root = new(new(state, player.ID), null, player.ID, _nodeCounter++);

        using CancellationTokenSource cts = Timeout > 0f ? new(TimeSpan.FromSeconds(Timeout)) : null;
        CancellationToken token = cts?.Token ?? CancellationToken.None;

        for (int i = 0; i < Iterations; i++)
        {
            if (token.IsCancellationRequested)
                break;

            GameState determinization = OpponentModel.SampleState(state);
            determinization.ActivePlayer.ShuffleDeck();
            determinization.ActivePlayer.ShuffleEquipmentDeck();

            Iterate(_root, determinization, player.ID, token);
        }

        CombatMove bestMove = _root.Children.Count > 0 ?
            _root.Children.OrderByDescending(kvp => kvp.Value.Sum(node => node.VisitCount)).First().Key :
            new(MoveType.Pass);

        if (bestMove.Type == MoveType.Pass && state.PreviousTurnPassed && state.ActivePlayer.Energy >= state.ActivePlayer.ChainStats.EquipmentCost && player.Equipment.Count > 0)
            bestMove = new(MoveType.DrawEquipment);

        if (DOTSavePath != "")
            AIUtilities.SaveDOTString(DOTSavePath, GetDOTString(), _dotFileCounter++, "ISMCTS");

        return Task.FromResult(bestMove);
    }

    private void Iterate(ISMCTSNode node, GameState determinization, PlayerID playerID, CancellationToken token)
    {
        (ISMCTSNode selectedNode, List<CombatMove> moves) = Selection(node, determinization, playerID);

        if (token.IsCancellationRequested)
            return;

        CombatMove unexploredMove = moves.FirstOrDefault(move => !node.Children.ContainsKey(move));
        if (unexploredMove is not null)
            selectedNode = Expansion(selectedNode, determinization, playerID, unexploredMove);

        try
        {
            (float valueP1, float valueP2) = Simulation(determinization, token);

            Backpropagation(selectedNode, valueP1, valueP2);
        }
        catch (OperationCanceledException) { }
    }

    private void Backpropagation(ISMCTSNode node, float valueP1, float valueP2)
    {
        while (node is not null)
        {
            node.VisitCount++;
            node.P1Stats.Update(valueP1);
            node.P2Stats.Update(valueP2);
            node = node.Parent;
        }
    }

    private (float, float) Simulation(GameState determinization, CancellationToken token)
    {
        for (int i = 0; i < RolloutDepth; i++)
        {
            if (determinization.IsCombatOver)
                break;

            token.ThrowIfCancellationRequested();

            CombatPlayer activePlayer = determinization.ActivePlayer;

            List<CombatMove> moves = AIUtilities.GenerateCombatMoves(determinization);

            determinization = moves.Select(move => EvaluateMove(determinization, move, token)).OrderByDescending(tuple => tuple.Item1).First().Item2;
        }

        return (PersonalityFSM.CurrentProfile.ScoreState(determinization, PlayerID.Player1), PersonalityFSM.CurrentProfile.ScoreState(determinization, PlayerID.Player2));
    }

    private (float, GameState) EvaluateMove(GameState state, CombatMove move, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        PlayerID activeID = state.ActivePlayer.ID;

        GameState clone = state.Clone();

        clone.ApplyMove(move).GetAwaiter().GetResult();

        return (PersonalityFSM.CurrentProfile.ScoreState(clone, activeID), clone);
    }

    private ISMCTSNode Expansion(ISMCTSNode node, GameState determinization, PlayerID playerID, CombatMove unexploredMove)
    {
        determinization.ApplyMove(unexploredMove).GetAwaiter().GetResult();
        InformationGameState targetInformation = new(determinization, playerID);

        ISMCTSNode matchingChild = node.Children.SelectMany(kvp => kvp.Value).FirstOrDefault(node => node.InformationGameState.Equals(targetInformation));

        matchingChild ??= new(targetInformation, node, determinization.ActivePlayer.ID, _nodeCounter++);

        node.Children[unexploredMove] = new() { matchingChild };

        return matchingChild;
    }

    private (ISMCTSNode, List<CombatMove>) Selection(ISMCTSNode node, GameState determinization, PlayerID playerID)
    {
        List<CombatMove> moves = AIUtilities.GenerateCombatMoves(determinization);

        while (!node.IsTerminal && moves.All(move => node.Children.ContainsKey(move)))
        {
            CombatMove bestMove = node.Children
                .OrderByDescending(kvp => kvp.Value.Average(child => UCT(child, determinization.ActivePlayer.ID)))
                .First().Key;

            determinization.ApplyMove(bestMove).GetAwaiter().GetResult();
            InformationGameState targetInformation = new(determinization, playerID);

            ISMCTSNode matchingChild = node.Children[bestMove].FirstOrDefault(node => node.InformationGameState.Equals(targetInformation));

            if (matchingChild is null)
            {
                matchingChild = new(targetInformation, node, determinization.ActivePlayer.ID, _nodeCounter++);
                node.Children[bestMove].Add(matchingChild);
            }

            node = matchingChild;
            moves = AIUtilities.GenerateCombatMoves(determinization);
        }

        return (node, moves);
    }

    private float UCT(ISMCTSNode node, PlayerID playerID)
    {
        if (node.VisitCount == 0)
            return float.MaxValue;

        ISMCTSStats stats = playerID == PlayerID.Player1 ? node.P1Stats : node.P2Stats;

        float exploitation = AIUtilities.GetRiskAdjustedScore(node.VisitCount, stats.Sum, stats.SumOfSquares, stats.Min, stats.Max, PersonalityFSM.CurrentProfile);

        float exploration = Exploration * (float)Math.Sqrt(Math.Log(node.Parent.VisitCount) / node.VisitCount);

        return exploitation + exploration;
    }

    public Task<List<Card>> ChooseMulligan(GameState state, CombatPlayer player) => Task.FromResult(new List<Card>());

    public Task<List<Card>> ChooseDiscardToMax(GameState state, CombatPlayer player) =>
        Task.FromResult(player.Hand.GetRange(0, player.Hand.Count - GlobalNumerals.maxCardsInHand));

    public Task<List<Card>> SelectCards(GameState state, CombatPlayer player, List<Card> cards, int amount, bool mustReachAmount) =>
        Task.FromResult(mustReachAmount ? cards.GetRange(0, amount) : new List<Card>());

    private string GetDOTString()
    {
        StringBuilder sb = new();
        sb.AppendLine("digraph ISMCTS {");
        sb.AppendLine("  node [style=filled, color=lightgrey];");
        sb.AppendLine("  rankdir=TB;");

        Dictionary<int, string> createdNodes = new();
        int moveNodeCounter = 0;

        void WriteNode(ISMCTSNode node)
        {
            string nodeName = $"node{node.Id}";

            if (!createdNodes.ContainsKey(node.Id))
            {
                string shape = node.PlayerID == PlayerID.Player1 ? "ellipse" : "box";
                string label = $"Visits: {node.VisitCount}\\n" +
                            $"P1 Value: {(node.VisitCount > 0 ? AIUtilities.GetRiskAdjustedScore(node.VisitCount, node.P1Stats.Sum, node.P1Stats.SumOfSquares, node.P1Stats.Min, node.P1Stats.Max, PersonalityFSM.CurrentProfile) : float.PositiveInfinity):F2}\\n" +
                            $"P2 Value: {(node.VisitCount > 0 ? AIUtilities.GetRiskAdjustedScore(node.VisitCount, node.P2Stats.Sum, node.P2Stats.SumOfSquares, node.P2Stats.Min, node.P2Stats.Max, PersonalityFSM.CurrentProfile) : float.PositiveInfinity):F2}";

                if (node.Parent is not null)
                    label += $"\\nUCT: {UCT(node, node.Parent.PlayerID):F2}";

                sb.AppendLine($"  {nodeName} [label=\"{label}\", shape={shape}];");
                createdNodes[node.Id] = nodeName;
            }

            if (node.Children.Count == 0)
                return;

            IEnumerable<IGrouping<CombatMove, (CombatMove move, ISMCTSNode child)>> moveGroups = node.Children
                .SelectMany(kvp => kvp.Value.Select(child => (move: kvp.Key, child)))
                .GroupBy(x => x.move);

            foreach (IGrouping<CombatMove, (CombatMove move, ISMCTSNode child)> group in moveGroups)
            {
                CombatMove move = group.Key;
                List<ISMCTSNode> nodes = group.Select(x => x.child).ToList();

                double avgUCT = nodes
                    .Average(n => UCT(n, node.PlayerID));

                string moveNodeName = $"move{moveNodeCounter++}";
                sb.AppendLine($"  {moveNodeName} [label=\"{move.ToShortString()}\\nVisits: {nodes.Sum(node => node.VisitCount)}\\nAvg UCT: {avgUCT:F2}\", shape=diamond, style=filled, color=lightblue];");

                sb.AppendLine($"  {nodeName} -> {moveNodeName};");

                foreach (ISMCTSNode child in nodes)
                {
                    string childName = $"node{child.Id}";

                    sb.AppendLine($"  {moveNodeName} -> {childName};");

                    WriteNode(child);
                }
            }
        }

        WriteNode(_root);

        sb.AppendLine("  subgraph cluster_legend {");
        sb.AppendLine("    label=\"Legend\";");
        sb.AppendLine("    keyP1 [label=\"Player1\", shape=ellipse, style=filled, color=lightgrey];");
        sb.AppendLine("    keyP2 [label=\"Player2\", shape=box, style=filled, color=lightgrey];");
        sb.AppendLine("    keyMove [label=\"Move Node\", shape=diamond, style=filled, color=lightblue];");
        sb.AppendLine("  }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    public string GetName() => "ISMCTS";

    public Dictionary<string, object> GetParameters() => new()
    {
        ["Iterations"] = Iterations,
        ["RolloutDepth"] = RolloutDepth,
        ["Exploration"] = Exploration,
        ["Timeout"] = Timeout
    };

    public PersonalityProfile GetProfile() => PersonalityFSM.CurrentProfile;
}