using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.Utilities;
using UnityEngine;

public static class AIUtilities
{
    private static List<Card> s_standardCards;
    private static List<Card> s_allCards;
    private static List<Card> s_equipmentCards;
    private static readonly string s_sessionId = Guid.NewGuid().ToString("N");

    public static readonly string DOTFilesPath = Path.Combine(
        Directory.GetParent(Application.dataPath).FullName,
        "Logs",
        "DOTFiles"
    );

    public static List<CombatMove> GenerateCombatMoves(GameState state, bool addPass = true, bool addDraw = true)
    {
        List<CombatMove> moves = new();

        if (state.IsCombatOver)
            return moves;

        HashSet<int> idsSeen = new();

        foreach (Card card in state.ActivePlayer.Hand)
        {
            if (!idsSeen.Add(card.ID))
                continue;

            if (card.GetCost(state.ActivePlayer) > state.ActivePlayer.Energy)
                continue;

            if (card.Category == Cardcategory.Surge && state.BoardState.GetAllCardsFromPlayer(state.ActivePlayer.ID).FindAll(x => x.Category == Cardcategory.Surge).Count >= state.ActivePlayer.ChainStats.maximumSurgesPerChain)
                continue;

            foreach (Link link in state.GetLinks(state.ActivePlayer.ID))
            {
                if (link.Cards.Count == 0 || card.Keywords.Contains(Keywords.Bolster))
                {
                    EventMessage message = new(state, owner: state.ActivePlayer, triggeringCard: card, targetCard: card, activeLink: link, targetLink: link);
                    if (state.CheckRange(card.Range) && ConditionChecker.Evaluate(card.PlayConditions, message) && ConditionChecker.Evaluate(link.Conditions, message))
                        moves.Add(new(MoveType.PlayCard, card, link.ID));
                }
            }
        }

        if (addDraw && state.ActivePlayer.Energy >= state.ActivePlayer.ChainStats.EquipmentCost && state.ActivePlayer.Equipment.Count > 0)
            moves.Add(new(MoveType.DrawEquipment));

        if (addPass)
            moves.Add(new(MoveType.Pass));

        return moves;
    }

    public static List<CombatMove> ValidCombatMovesForCard(GameState state, Card card)
    {
        List<CombatMove> moves = new();

        if (card.GetCost(state.ActivePlayer) > state.ActivePlayer.Energy)
            return moves;

        if (card.Category == Cardcategory.Surge && state.ActivePlayer.ChainStats.surgesPlayed >= state.ActivePlayer.ChainStats.maximumSurgesPerChain)
            return moves;

        foreach (Link link in state.GetLinks(state.ActivePlayer.ID))
        {
            if (link.Cards.Count == 0 || card.Keywords.Contains(Keywords.Bolster))
            {
                EventMessage message = new(state, owner: state.ActivePlayer, triggeringCard: card, targetCard: card, activeLink: link, targetLink: link);
                if (state.CheckRange(card.Range) && ConditionChecker.Evaluate(card.PlayConditions, message))
                    moves.Add(new(MoveType.PlayCard, card, link.ID));
            }
        }

        return moves;
    }

    public static List<CombatMove> ValidCombatMovesForHand(GameState state, List<Card> hand)
    {
        List<CombatMove> moves = new();

        foreach (Card card in hand)
            moves.AddRange(ValidCombatMovesForCard(state, card));

        if (state.ActivePlayer.Energy >= state.ActivePlayer.ChainStats.EquipmentCost && state.ActivePlayer.Equipment.Count > 0)
            moves.Add(new(MoveType.DrawEquipment));

        moves.Add(new(MoveType.Pass));

        return moves;
    }

    private static List<Card> GenerateStandardCards()
    {
        HashSet<Card> result = new();
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/AronTest2").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/AronTest1").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/AronTest3").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/AronTest4").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/AronTest5").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/PlaytestAI").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));
        result.AddRange(Resources.Load<NPC>("NPC/Aron Test NPCs/PlaytestPlayer").npcData.currentDeck.Decklist().Select(config => new Card(config.Data)));

        return result.ToList();
    }

    public static List<Card> GetStandardCards()
    {
        s_standardCards ??= GenerateStandardCards();
        return new(s_standardCards);
    }

    private static List<Card> GenerateAllCards()
    {
        List<Card> result = Resources.LoadAll<CardConfig>("Cards").Select(config => new Card(config.Data)).ToList();

        return result;
    }

    public static List<Card> GetAllCards()
    {
        s_allCards ??= GenerateAllCards();
        return new(s_allCards);
    }

    private static List<Card> GenerateEquipmentCards()
    {
        HashSet<Card> result = new();

        foreach (Equipment equipment in Resources.LoadAll<Equipment>("Equipment"))
        {
            result.AddRange(equipment.equipmentCards.Select(config => new Card(config.Data)));
        }

        return result.ToList();
    }

    public static List<Card> GetEquipmentCards()
    {
        s_equipmentCards ??= GenerateEquipmentCards();
        return new(s_equipmentCards);
    }

    public static float GetRiskAdjustedScore(IEnumerable<float> scores, PersonalityProfile profile, float capFactor = 10f)
    {
        if (scores == null)
            throw new ArgumentException("Scores cannot be null");

        int count = 0;
        float sum = 0f;
        float sumSquares = 0f;
        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (float x in scores)
        {
            count++;
            sum += x;
            sumSquares += x * x;
            if (x < min) min = x;
            if (x > max) max = x;
        }

        return GetRiskAdjustedScore(count, sum, sumSquares, min, max, profile, capFactor);
    }

    public static float GetRiskAdjustedScore(int count, float sum, float sumOfSquares, float min, float max, PersonalityProfile profile, float capFactor = 10f)
    {
        float risk = profile.Risk;

        if (count == 0)
            throw new ArgumentException("Scores cannot be empty");

        float mean = sum / count;

        if (risk == 0f || count == 1 || min == max)
            return mean;

        float variance = (sumOfSquares - (sum * sum / count)) / (count - 1);
        float stdDev = (float)Math.Sqrt(variance);

        float absRisk = Math.Abs(risk);

        float range = max - min;
        float lambda = absRisk / range;

        float heuristicRange = profile.Bounds.Value.UpperBound - profile.Bounds.Value.LowerBound;
        float maxLambda = absRisk / heuristicRange * capFactor;

        lambda = Math.Min(lambda, maxLambda);

        float sign = Math.Sign(risk);

        return mean + (sign * lambda * stdDev);
    }

    public static (bool deterministic, GameState resultingState) CheckMoveIsDeterministic(
        GameState state,
        CombatMove move,
        bool bothPlayers = false,
        bool passTurn = true
    )
    {
        GameState clone = state.Clone();
        List<CombatPlayer> playersToCheck = bothPlayers
            ? new() { clone.Player1, clone.Player2 }
            : new() { clone.ActivePlayer };

        List<(int cardsDrawn, int shuffles)> current = playersToCheck
            .Select(p => (p.ChainStats.cardsDrawn, p.ChainStats.shuffles))
            .ToList();

        clone.ApplyMove(move, passTurn: passTurn).GetAwaiter().GetResult();

        List<(int cardsDrawn, int shuffles)> after = playersToCheck
            .Select(p => (p.ChainStats.cardsDrawn, p.ChainStats.shuffles))
            .ToList();

        return (current.SequenceEqual(after), clone);
    }

    public static void SaveDOTString(string dotSavePath, string dotString, int dotFileCounter, string algName)
    {
        if (string.IsNullOrWhiteSpace(dotSavePath))
            return;

        try
        {
            string baseDir = dotSavePath;

            string sessionDir = Path.Combine(baseDir, algName, s_sessionId);

            if (!Directory.Exists(sessionDir))
            {
                Directory.CreateDirectory(sessionDir);
            }

            string fileName = $"{dotFileCounter}.dot";
            string fullPath = Path.Combine(sessionDir, fileName);

            File.WriteAllText(fullPath, dotString);

            Debug.Log($"[{algName}Controller] DOT file saved: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{algName}Controller] Failed to save DOT file: {ex.Message}");
        }
    }
}