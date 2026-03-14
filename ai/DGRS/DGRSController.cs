using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/**
* The Determinized Greedy Rollout Search (DGRS) algorithm extends DGS by performing
* limited-depth rollouts to evaluate the potential of actions over multiple future steps. Like
* DGS, DGRS generates multiple determinized scenarios (ScenarioCount) to approximate hidden information
* and evaluates actions according to a risk-adjusted score based on the player's personality (PersonalityFSM). 
* However, instead of only considering the immediate next action, DGRS simulates
* sequences of actions up to a configurable rollout depth (RolloutDepth) and uses the expected value of
* these sequences to select the best action from the root node. To reduce computation, the
* amount of actions evaluated with a rollout can be limited by the action limit parameter (MoveLimit).
* This parameter causes only the actions with the highest immediate utility to be evaluated
* and considered in the decision.
**/
public class DGRSController : IAIController
{
    /**
    * The amount of scenarios to determinize.
    * Higher ScenarioCount causes more computation but a more accurate approximation of possible future states.
    **/
    public int ScenarioCount { get; }

    public PersonalityFSM PersonalityFSM { get; }

    /**
    * Limits the depth of the rollouts.
    * Higher depth causes more computation but 'looks ahead' more steps.
    * A higher depth can also cause more randomness in the decision-making, so higher is not always better.
    **/
    public int RolloutDepth { get; }

    /**
    * Limits the amount of actions evaluated.
    * More actions evaluated means less chance of missing a move that has low immediate gain but sets up for better moves later on.
    * But more actions evaluated is also more computation.
    **/
    public int MoveLimit { get; }

    /**
    * A soft computation time limit.
    * When setting a timout, it is not guaranteed that the algorithm will actually stay within the timeout limit.
    * It is possible that the algorithm uses some time to shut down the computation after the timeout has already been reached.
    * It is preferable to limit computation using ScenarioCount, RolloutDepth, and MoveLimit.
    **/
    public float Timeout { get; }

    /**
    * If set to a valid path, this will store the decisions made as representive .dot files. Use the dot tool to visualize.
    **/
    public string DOTSavePath { get; }

    private int _dotFileCounter = 0;

    public DGRSController(int scenarioCount, int rolloutDepth, int moveLimit, PersonalityFSM personalityFSM, float timeout = 0f, string dotSavePath = "")
    {
        ScenarioCount = scenarioCount;
        RolloutDepth = rolloutDepth;
        MoveLimit = moveLimit;
        PersonalityFSM = personalityFSM;
        Timeout = timeout;
        DOTSavePath = dotSavePath;
    }

    public Task<CombatMove> GetMove(GameState state, CombatPlayer player)
    {
        PersonalityFSM.Update(state);

        List<GameState> initialScenarios = GenerateScenarios(state);

        float passValue = ComputePassValue(initialScenarios, player.ID);

        Dictionary<CombatMove, (List<GameState> scenarios, float score)> initialValues = ScoreInitialMoves(initialScenarios, player.ID);

        List<(CombatMove move, List<GameState> scenarios)> topMoves = initialValues
            .OrderByDescending(kvp => kvp.Value.score)
            .Take(MoveLimit)
            .Select(kvp => (kvp.Key, kvp.Value.scenarios))
            .ToList();

        List<(CombatMove move, GameState scenario)> rolloutJobs = topMoves
            .SelectMany(topMove => topMove.scenarios.Select(scenario => (topMove.move, scenario)))
            .ToList();

        ConcurrentDictionary<CombatMove, ConcurrentBag<float>> rolloutScores = new();

        CancellationTokenSource cts = Timeout > 0f ? new(TimeSpan.FromSeconds(Timeout)) : null;
        CancellationToken token = cts?.Token ?? CancellationToken.None;

        if (rolloutJobs.Count > 0)
        {
            try
            {
                Parallel.ForEach(
                    rolloutJobs,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Min(rolloutJobs.Count, Environment.ProcessorCount),
                        CancellationToken = token
                    },
                    job =>
                {
                    (CombatMove move, GameState scenario) = job;
                    float score = Rollout(scenario, player.ID, token);

                    rolloutScores.AddOrUpdate(
                        move,
                        new ConcurrentBag<float> { score },
                        (_, bag) =>
                        {
                            bag.Add(score);
                            return bag;
                        });
                });
            }
            catch (OperationCanceledException) { }
        }

        ConcurrentDictionary<CombatMove, float> rolloutValues = new();
        rolloutValues.TryAdd(new CombatMove(MoveType.Pass), passValue);

        foreach (KeyValuePair<CombatMove, ConcurrentBag<float>> kvp in rolloutScores)
        {
            float riskScore = AIUtilities.GetRiskAdjustedScore(kvp.Value, PersonalityFSM.CurrentProfile);
            rolloutValues[kvp.Key] = riskScore;
        }

        CombatMove bestMove = rolloutValues.OrderByDescending(kvp => kvp.Value).First().Key;

        if (bestMove.Type == MoveType.Pass && state.PreviousTurnPassed && state.ActivePlayer.Energy >= state.ActivePlayer.ChainStats.EquipmentCost && player.Equipment.Count > 0)
            bestMove = new(MoveType.DrawEquipment);

        if (DOTSavePath != "")
            AIUtilities.SaveDOTString(DOTSavePath, GetDOTString(passValue, bestMove, initialValues, rolloutValues), _dotFileCounter++, "SMGL");

        return Task.FromResult(bestMove);
    }

    /**
    * Computes the value of just passing by applying a pass to all scenarios.
    **/
    private float ComputePassValue(List<GameState> scenarios, PlayerID playerID)
    {
        List<float> scores = new();
        foreach (GameState scenario in scenarios)
        {
            (float score, _) = EvaluateMove(scenario, new(MoveType.Pass), playerID);
            scores.Add(score);
        }

        float passValue = AIUtilities.GetRiskAdjustedScore(scores, PersonalityFSM.CurrentProfile);

        return passValue;
    }

    /**
    * A single rollout from a scenario up to RolloutDepth.
    * Returns the value of the resulting game state.
    **/
    private float Rollout(GameState scenario, PlayerID playerID, CancellationToken token)
    {
        int depth = 0;

        while (!scenario.IsCombatOver && depth < RolloutDepth)
        {
            if (token.IsCancellationRequested)
                break;

            (float passValue, _) = EvaluateMove(scenario, new CombatMove(MoveType.Pass), playerID);

            float bestValue = passValue;
            GameState bestState = scenario;

            List<CombatMove> moves = AIUtilities.GenerateCombatMoves(scenario, addPass: false);

            foreach (CombatMove move in moves)
            {
                if (token.IsCancellationRequested)
                    break;

                (float score, GameState state) = EvaluateMove(scenario, move, playerID);
                if (score > bestValue)
                {
                    bestValue = score;
                    bestState = state;
                }
            }

            scenario = bestState;
            if (bestValue == passValue)
                break;

            depth++;
        }

        return PersonalityFSM.CurrentProfile.ScoreState(scenario, playerID);
    }

    private class MoveJob
    {
        public CombatMove Move { get; }
        public GameState Scenario { get; }
        public bool IsFirstScenario { get; }

        public MoveJob(CombatMove move, GameState scenario, bool isFirstScenario)
        {
            Move = move;
            Scenario = scenario;
            IsFirstScenario = isFirstScenario;
        }
    }

    private Dictionary<CombatMove, (List<GameState>, float)> ScoreInitialMoves(List<GameState> scenarios, PlayerID playerID)
    {
        ConcurrentDictionary<CombatMove, (ConcurrentBag<GameState>, ConcurrentBag<float>)> moveScores = new();
        ConcurrentQueue<MoveJob> jobQueue = new();
        int activeWorkers = 0;

        bool AtomicDequeue(out MoveJob result)
        {
            Interlocked.Increment(ref activeWorkers);
            if (jobQueue.TryDequeue(out MoveJob job))
            {
                result = job;
                return true;
            }
            else
            {
                Interlocked.Decrement(ref activeWorkers);
                result = null;
                return false;
            }
        }

        void ProcessJob(MoveJob job)
        {
            float score;
            GameState newScenario;
            if (job.IsFirstScenario)
            {
                (bool deterministic, GameState resultingScenario) = AIUtilities.CheckMoveIsDeterministic(job.Scenario, job.Move);

                if (resultingScenario.ActivePlayer.ID != playerID)
                    resultingScenario.ToggleActivePlayer();

                if (!deterministic)
                {
                    foreach (GameState scenario in scenarios.Skip(1))
                        jobQueue.Enqueue(new MoveJob(job.Move, scenario, false));
                }
                else
                {
                    for (int i = 1; i < ScenarioCount; i++)
                    {
                        GameState clone = resultingScenario.Clone();
                        clone.ActivePlayer.ShuffleDeck();
                        clone.ActivePlayer.ShuffleEquipmentDeck();
                        moveScores.AddOrUpdate(
                            job.Move,
                            (new ConcurrentBag<GameState>() { clone }, new ConcurrentBag<float>()),
                            (CombatMove move, (ConcurrentBag<GameState> scenarios, ConcurrentBag<float> scores) value) =>
                            {
                                value.scenarios.Add(clone);
                                return value;
                            }
                        );
                    }
                }
                score = PersonalityFSM.CurrentProfile.ScoreState(resultingScenario, playerID);
                newScenario = resultingScenario;
            }
            else
            {
                (float eval, GameState resultingScenario) = EvaluateMove(job.Scenario, job.Move, playerID);
                score = eval;
                newScenario = resultingScenario;
            }

            moveScores.AddOrUpdate(
                job.Move,
                (new ConcurrentBag<GameState>() { newScenario }, new ConcurrentBag<float>() { score }),
                (CombatMove move, (ConcurrentBag<GameState> scenarios, ConcurrentBag<float> scores) value) =>
                {
                    value.scenarios.Add(newScenario);
                    value.scores.Add(score);
                    return value;
                }
            );
        }

        List<CombatMove> moves = AIUtilities.GenerateCombatMoves(scenarios[0], addPass: false);

        foreach (CombatMove move in moves)
            jobQueue.Enqueue(new MoveJob(move, scenarios[0], true));

        Parallel.For(0, Environment.ProcessorCount, _ =>
        {
            while (true)
            {
                if (AtomicDequeue(out MoveJob job))
                {
                    try
                    {
                        ProcessJob(job);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeWorkers);
                    }
                }
                else
                {
                    if (Volatile.Read(ref activeWorkers) == 0 && jobQueue.IsEmpty)
                        break;
                }
            }
        });

        return moveScores.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value.Item1.ToList(), AIUtilities.GetRiskAdjustedScore(kvp.Value.Item2.ToList(), PersonalityFSM.CurrentProfile)));
    }

    private (float score, GameState state) EvaluateMove(GameState state, CombatMove move, PlayerID playerID)
    {
        GameState clone = state.Clone();

        clone.ApplyMove(move).GetAwaiter().GetResult();

        if (clone.ActivePlayer.ID != playerID)
            clone.ToggleActivePlayer();

        return (PersonalityFSM.CurrentProfile.ScoreState(clone, playerID), clone);
    }

    private List<GameState> GenerateScenarios(GameState state)
    {
        List<GameState> scenarios = new();
        for (int i = 0; i < ScenarioCount; i++)
        {
            GameState clone = state.Clone();
            clone.ActivePlayer.ShuffleDeck();
            clone.ActivePlayer.ShuffleEquipmentDeck();
            scenarios.Add(clone);
        }
        return scenarios;
    }

    public Task<List<Card>> ChooseMulligan(GameState state, CombatPlayer player) => Task.FromResult(new List<Card>());

    public Task<List<Card>> ChooseDiscardToMax(GameState state, CombatPlayer player) =>
        Task.FromResult(player.Hand.GetRange(0, player.Hand.Count - GlobalNumerals.maxCardsInHand));

    public Task<List<Card>> SelectCards(GameState state, CombatPlayer player, List<Card> cards, int amount, bool mustReachAmount) =>
        Task.FromResult(mustReachAmount ? cards.GetRange(0, amount) : new List<Card>());

    private string GetDOTString(
        float passValue,
        CombatMove chosenMove,
        Dictionary<CombatMove, (List<GameState>, float)> initialValues,
        ConcurrentDictionary<CombatMove, float> rolloutValues)
    {
        StringBuilder sb = new();
        sb.AppendLine("digraph G {");
        sb.AppendLine("  node [shape=box];");

        string rootLabel = $"Root\\nValue: {passValue:F2}";
        sb.AppendLine($"  root [label=\"{rootLabel}\"];");

        int nodeCount = 0;

        foreach (KeyValuePair<CombatMove, (List<GameState>, float)> kvp in initialValues)
        {
            CombatMove move = kvp.Key;
            float currentValue = kvp.Value.Item2;

            string label = $"Current: {currentValue:F2}";
            if (rolloutValues.TryGetValue(move, out float rolloutValue))
            {
                label += $"\\nRollout: {rolloutValue:F2}";
            }

            string style = move.Equals(chosenMove) ? ", style=bold, color=green" : "";

            string nodeName = $"{nodeCount++}";
            sb.AppendLine($"  {nodeName} [label=\"{label}\"{style}];");

            sb.AppendLine($"  root -> {nodeName} [label=\"{move.ToShortString()}\"];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public string GetName() => "SMGL";

    public Dictionary<string, object> GetParameters() => new()
    {
        ["ScenarioCount"] = ScenarioCount,
        ["RolloutDepth"] = RolloutDepth,
        ["MoveLimit"] = MoveLimit,
        ["Timeout"] = Timeout
    };

    public PersonalityProfile GetProfile() => PersonalityFSM.CurrentProfile;
}