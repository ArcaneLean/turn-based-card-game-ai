using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/**
The Determinized Greedy Search algorithm is a decision-making strategy under uncer-
tainty that evaluates all possible actions for the current player and selects the action with
the highest expected utility. To handle partial observability, the algorithm generates a set
of scenarios by determinizing the hidden elements (e.g., sampling cards, shuffling decks)
and evaluates each action across these scenarios. The expected score of each action is com-
puted by collecting the individual scenario evaluations using the risk-adjusted aggregation
function. Unlike ISMCTS or SAB-DESPOT, DGS does not expand a multi-step search
tree; it only considers the immediate next action, but its evaluation incorporates multiple
possible states to approximate the belief.
**/
public class DGSController : IAIController
{
    public PersonalityFSM PersonalityFSM { get; }
    
    /**
    The ScenarioCount limits how many determinizations we do to approximate the belief state.
    **/
    public int ScenarioCount { get; }
    public float Timeout { get; }
    public string DOTSavePath { get; }

    private readonly Dictionary<CombatMove, List<float>> _moveEvaluations = new();
    private readonly Dictionary<CombatMove, float> _moveValues = new();
    private CombatMove _bestMove;


    private int _dotFileCounter = 0;

    public DGSController(PersonalityFSM personalityFSM, int scenarioCount, float timeout = 0f, string dotSavePath = "")
    {
        PersonalityFSM = personalityFSM;
        ScenarioCount = scenarioCount;
        Timeout = timeout;
        DOTSavePath = dotSavePath;
    }

    public async Task<CombatMove> GetMove(GameState state, CombatPlayer player)
    {
        PersonalityFSM.Update(state);

        CombatMove move = await GetGreedyMove(state, player.ID, PersonalityFSM.CurrentProfile);

        if (ShouldForceDrawEquipment(move, state, player))
            move = new(MoveType.DrawEquipment);

        _bestMove = move;

        if (DOTSavePath != "")
            AIUtilities.SaveDOTString(DOTSavePath, GetDOTString(), _dotFileCounter++, "Greedy");

        return move;
    }

    public bool ShouldForceDrawEquipment(CombatMove move, GameState state, CombatPlayer player) => move.Type == MoveType.Pass && state.PreviousTurnPassed && player.Energy >= player.ChainStats.EquipmentCost && player.Equipment.Count > 0;

    private async Task<CombatMove> GetGreedyMove(GameState state, PlayerID playerID, PersonalityProfile profile)
    {
        _moveValues.Clear();
        _moveEvaluations.Clear();

        List<GameState> scenarios = new();

        for (int i = 0; i < ScenarioCount; i++)
            scenarios.Add(GenerateScenario(state));

        List<CombatMove> moves = AIUtilities.GenerateCombatMoves(state, addPass: false, addDraw: true);

        float passValue = ScoreMove(playerID, profile, new(MoveType.Pass), scenarios);

        if (Timeout > 0f)
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(Timeout));

            Task scoringTask = Task.Run(() =>
            {
                foreach (CombatMove move in moves)
                {
                    ScoreMove(playerID, profile, move, scenarios, cts.Token);
                }
            }, cts.Token);

            try
            {
                await scoringTask;
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning($"Timout reached: time > {Timeout}s");
            }
        }
        else
        {
            foreach (CombatMove move in moves)
            {
                ScoreMove(playerID, profile, move, scenarios);
            }
        }

        if (_moveValues.Count == 0)
            return new(MoveType.Pass);

        CombatMove bestMove = _moveValues
            .OrderByDescending(x => x.Value)
            .Select(x => x.Key)
            .FirstOrDefault();

        if (bestMove is null || _moveValues[bestMove] == passValue)
            bestMove = new(MoveType.Pass);

        return bestMove;
    }

    private float ScoreMove(
        PlayerID playerID,
        PersonalityProfile profile,
        CombatMove move,
        List<GameState> scenarios,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        (bool deterministic, GameState resultingState) = AIUtilities.CheckMoveIsDeterministic(scenarios[0], move);
        float firstValue = profile.ScoreState(resultingState, playerID);

        if (deterministic)
        {
            _moveValues[move] = firstValue;
            _moveEvaluations[move] = new();
        }
        else
        {
            ConcurrentBag<float> evaluations = new() { firstValue };

            Parallel.ForEach(
                scenarios.Skip(1),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = token
                },
                scenario =>
                {
                    token.ThrowIfCancellationRequested();

                    GameState clone = scenario.Clone();
                    clone.ApplyMove(move).GetAwaiter().GetResult();
                    float eval = profile.ScoreState(clone, playerID);
                    evaluations.Add(eval);
                }
            );

            List<float> evaluationList = evaluations.ToList();
            float value = AIUtilities.GetRiskAdjustedScore(evaluationList, profile);
            _moveValues[move] = value;
            _moveEvaluations[move] = evaluationList;
        }

        return _moveValues[move];
    }

    private GameState GenerateScenario(GameState state)
    {
        GameState clone = state.Clone();
        clone.ActivePlayer.ShuffleDeck();
        clone.ActivePlayer.ShuffleEquipmentDeck();
        return clone;
    }

    public Task<List<Card>> ChooseMulligan(GameState state, CombatPlayer player) => Task.FromResult(new List<Card>());

    public Task<List<Card>> ChooseDiscardToMax(GameState state, CombatPlayer player) =>
        Task.FromResult(player.Hand.GetRange(0, player.Hand.Count - GlobalNumerals.maxCardsInHand));

    public Task<List<Card>> SelectCards(GameState state, CombatPlayer player, List<Card> cards, int amount, bool mustReachAmount) =>
        Task.FromResult(mustReachAmount ? cards.GetRange(0, amount) : new List<Card>());

    private string GetDOTString()
    {
        System.Text.StringBuilder sb = new();
        sb.AppendLine("digraph GreedyController {");
        sb.AppendLine("  node [shape=box, style=filled, color=lightgrey];");

        sb.AppendLine($"  root [label=\"{_moveValues[_bestMove]:F2}\"];");

        int moveIndex = 0;

        foreach (KeyValuePair<CombatMove, List<float>> kvp in _moveEvaluations)
        {
            CombatMove move = kvp.Key;
            List<float> evals = kvp.Value;
            float value = _moveValues[move];

            string moveId = $"move_{moveIndex++}";

            string label = value.ToString("F2");
            if (evals != null && evals.Count > 0)
            {
                float mean = evals.Average();
                label += $"\\nMean: {mean:F2}";
            }

            if (move.Equals(_bestMove))
            {
                sb.AppendLine($"  {moveId} [label=\"{move.ToShortString()}\\n{label}\", color=green, style=bold];");
                sb.AppendLine($"  root -> {moveId} [color=green];");
            }
            else
            {
                sb.AppendLine($"  {moveId} [label=\"{move.ToShortString()}\\n{label}\"];");
                sb.AppendLine($"  root -> {moveId};");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public string GetName() => "Greedy";

    public Dictionary<string, object> GetParameters() => new()
    {
        ["ScenarioCount"] = ScenarioCount,
        ["Timeout"] = Timeout
    };

    public PersonalityProfile GetProfile() => PersonalityFSM.CurrentProfile;
}