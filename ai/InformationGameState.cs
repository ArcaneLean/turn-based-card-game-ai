using System;
using System.Collections.Generic;
using System.Linq;

public static class HashSetHelper
{
    public static int CombineHashCodes<T>(this HashSet<T> hashSet)
    {
        return hashSet == null || hashSet.Count == 0
            ? 0
            : hashSet
                .OrderBy(e => e?.GetHashCode() ?? 0)
                .Aggregate(0, (acc, elem) => HashCode.Combine(acc, elem?.GetHashCode() ?? 0));
    }
}

public class InformationGameState
{
    public InformationPlayerState Player1 { get; }
    public InformationPlayerState Player2 { get; }
    public PlayerID ActivePlayer { get; }
    public PlayerID AttackingPlayer { get; }
    public InformationBoardState BoardState { get; }
    public int LinkCount { get; }
    public bool PreviousTurnPassed { get; }
    public HashSet<Card> SelectedCards { get; }
    public HashSet<Card> ConsumedCards { get; }
    public HashSet<(Card, int)> ChainValues { get; }
    public HashSet<(Card, int)> CombatValues { get; }
    public int ChainCount { get; }
    public bool IsCombatOver { get; }

    public InformationGameState(GameState state, PlayerID perspective)
    {
        Player1 = perspective == PlayerID.Player1 ? new InformationAIState(state.Player1) : new InformationOpponentState(state.Player1);
        Player2 = perspective == PlayerID.Player2 ? new InformationAIState(state.Player2) : new InformationOpponentState(state.Player2);
        ActivePlayer = state.ActivePlayer.ID;
        AttackingPlayer = state.AttackingPlayer.ID;
        BoardState = new InformationBoardState(state.BoardState);
        LinkCount = state.LinkCount;
        PreviousTurnPassed = state.PreviousTurnPassed;
        SelectedCards = state.SelectedCards.ToHashSet();
        ConsumedCards = state.ConsumedCards.ToHashSet();
        ChainValues = state.chainValues.Select(kvp => (kvp.Key, kvp.Value)).ToHashSet();
        CombatValues = state.combatValues.Select(kvp => (kvp.Key, kvp.Value)).ToHashSet();
        ChainCount = state.ChainCount;
        IsCombatOver = state.IsCombatOver;
    }

    public override bool Equals(object obj)
    {
        return obj is InformationGameState other &&
            Player1.Equals(other.Player1) &&
            Player2.Equals(other.Player2) &&
            ActivePlayer == other.ActivePlayer &&
            AttackingPlayer == other.AttackingPlayer &&
            BoardState.Equals(other.BoardState) &&
            LinkCount == other.LinkCount &&
            PreviousTurnPassed == other.PreviousTurnPassed &&
            SelectedCards.SetEquals(other.SelectedCards) &&
            ConsumedCards.SetEquals(other.ConsumedCards) &&
            ChainValues.SetEquals(other.ChainValues) &&
            CombatValues.SetEquals(other.CombatValues) &&
            ChainCount == other.ChainCount &&
            IsCombatOver == other.IsCombatOver;
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();

        hashCode.Add(Player1);
        hashCode.Add(Player2);
        hashCode.Add(ActivePlayer);
        hashCode.Add(AttackingPlayer);
        hashCode.Add(BoardState);
        hashCode.Add(LinkCount);
        hashCode.Add(PreviousTurnPassed);
        hashCode.Add(SelectedCards.CombineHashCodes());
        hashCode.Add(ConsumedCards.CombineHashCodes());
        hashCode.Add(ChainValues.CombineHashCodes());
        hashCode.Add(CombatValues.CombineHashCodes());
        hashCode.Add(ChainCount);
        hashCode.Add(IsCombatOver);

        return hashCode.ToHashCode();
    }
}

public abstract class InformationPlayerState
{
    public int Health { get; }
    public int Energy { get; }
    public int EnergyPerTurn { get; }
    public int CardsPerTurn { get; }
    public ChainStats ChainStats { get; }
    public HashSet<LastingEffect> LastingEffects { get; }

    public InformationPlayerState(CombatPlayer player)
    {
        Health = player.Health;
        Energy = player.Energy;
        EnergyPerTurn = player.EnergyPerTurn;
        CardsPerTurn = player.CardsPerTurn;
        ChainStats = player.ChainStats.Clone();
        LastingEffects = player.LastingEffects.Select(effect => effect.Clone()).ToHashSet();
    }

    public override bool Equals(object obj)
    {
        return obj is InformationPlayerState other &&
            Health == other.Health &&
            Energy == other.Energy &&
            EnergyPerTurn == other.EnergyPerTurn &&
            CardsPerTurn == other.CardsPerTurn &&
            ChainStats.Equals(other.ChainStats) &&
            LastingEffects.SetEquals(other.LastingEffects);
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();

        hashCode.Add(Health);
        hashCode.Add(Energy);
        hashCode.Add(EnergyPerTurn);
        hashCode.Add(CardsPerTurn);
        hashCode.Add(ChainStats);
        hashCode.Add(LastingEffects.CombineHashCodes());

        return hashCode.ToHashCode();
    }
}

public class InformationAIState : InformationPlayerState
{
    public HashSet<Card> Deck { get; }
    public HashSet<Card> Hand { get; }
    public HashSet<Card> Equipment { get; }
    public HashSet<Card> Graveyard { get; }

    public InformationAIState(CombatPlayer player) : base(player)
    {
        Deck = player.Deck.Select(card => card.Clone()).ToHashSet();
        Hand = player.Hand.Select(card => card.Clone()).ToHashSet();
        Equipment = player.Equipment.Select(card => card.Clone()).ToHashSet();
        Graveyard = player.Graveyard.Select(card => card.Clone()).ToHashSet();
    }

    public override bool Equals(object obj)
    {
        return obj is InformationAIState other &&
            base.Equals(other) &&
            Deck.SetEquals(other.Deck) &&
            Hand.SetEquals(other.Hand) &&
            Equipment.SetEquals(other.Equipment) &&
            Graveyard.SetEquals(other.Graveyard);
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();

        hashCode.Add(base.GetHashCode());
        hashCode.Add(Deck.CombineHashCodes());
        hashCode.Add(Hand.CombineHashCodes());
        hashCode.Add(Equipment.CombineHashCodes());
        hashCode.Add(Graveyard.CombineHashCodes());

        return hashCode.ToHashCode();
    }
}

public class InformationOpponentState : InformationPlayerState
{
    public int DeckCount { get; }
    public int HandCount { get; }
    public int EquipmentCount { get; }
    public int GraveyardCount { get; }

    public InformationOpponentState(CombatPlayer player) : base(player)
    {
        DeckCount = player.Deck.Count;
        HandCount = player.Hand.Count;
        EquipmentCount = player.Equipment.Count;
        GraveyardCount = player.Graveyard.Count;
    }

    public override bool Equals(object obj)
    {
        return obj is InformationOpponentState other &&
            base.Equals(other) &&
            DeckCount == other.DeckCount &&
            HandCount == other.HandCount &&
            EquipmentCount == other.EquipmentCount &&
            GraveyardCount == other.GraveyardCount;
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();

        hashCode.Add(base.GetHashCode());
        hashCode.Add(DeckCount);
        hashCode.Add(HandCount);
        hashCode.Add(EquipmentCount);
        hashCode.Add(GraveyardCount);

        return hashCode.ToHashCode();
    }
}

public class InformationBoardState
{
    public HashSet<InformationLinkState> Player1Grid { get; }
    public HashSet<InformationLinkState> Player2Grid { get; }
    public Cardrange Range { get; }

    public InformationBoardState(BoardState boardState)
    {
        Player1Grid = boardState.Player1Grid.Select(link => new InformationLinkState(link)).ToHashSet();
        Player2Grid = boardState.Player2Grid.Select(link => new InformationLinkState(link)).ToHashSet();
        Range = boardState.Range;
    }

    public override bool Equals(object obj)
    {
        return obj is InformationBoardState other &&
            Player1Grid.SetEquals(other.Player1Grid) &&
            Player2Grid.SetEquals(other.Player2Grid) &&
            Range == other.Range;
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();

        hashCode.Add(Player1Grid.CombineHashCodes());
        hashCode.Add(Player2Grid.CombineHashCodes());
        hashCode.Add(Range);

        return hashCode.ToHashCode();
    }
}

public class InformationLinkState
{
    public PlayerID Owner { get; }
    public HashSet<Card> Cards { get; }
    public HashSet<EffectCondition> Conditions { get; }
    public int ID { get; }
    public bool IsDestroyed { get; }

    public InformationLinkState(Link link)
    {
        Owner = link.Owner.ID;
        Cards = link.Cards.Select(card => card.Clone()).ToHashSet();
        Conditions = link.Conditions.ToHashSet();
        ID = link.ID;
        IsDestroyed = link.IsDestroyed;
    }

    public override bool Equals(object obj)
    {
        return obj is InformationLinkState other &&
            Owner == other.Owner &&
            Cards.SetEquals(other.Cards) &&
            Conditions.SetEquals(other.Conditions) &&
            ID == other.ID &&
            IsDestroyed == other.IsDestroyed;
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();

        hashCode.Add(Owner);
        hashCode.Add(Cards.CombineHashCodes());
        hashCode.Add(Conditions.CombineHashCodes());
        hashCode.Add(ID);
        hashCode.Add(IsDestroyed);

        return hashCode.ToHashCode();
    }
}