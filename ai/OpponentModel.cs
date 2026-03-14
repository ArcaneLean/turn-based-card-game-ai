using System;
using System.Collections.Generic;
using System.Linq;

public class OpponentModel
{
    private readonly Dictionary<Guid, DrawSource> _instanceIdToDrawSource = new();
    private readonly HashSet<Guid> _seenInstanceIds = new();
    private readonly HashSet<Guid> _revealedInstanceIds = new();
    private PlayerID _opponentId;
    private List<Card> _equipmentCards;

    private readonly Dictionary<Guid, CardStringChange> _keywordSubscriptions = new();

    public void Initialize(GameState state, CombatPlayer opponent)
    {
        _opponentId = opponent.ID;

        opponent.OnDrawEvent += (card, source) => OnDraw(card, source);
        state.BoardState.CardAdded += (card, playerID, linkID) => OnCardAddedToBoard(card, playerID);
        opponent.OnLeaveHandEvent += (card) => OnLeaveHand(card);

        _equipmentCards = opponent.Equipment.Select(card => card.Clone()).ToList();
    }

    private void OnLeaveHand(Card card)
    {
        if (_keywordSubscriptions.TryGetValue(card.InstanceId, out CardStringChange handler))
        {
            card.OnKeywordAdded -= handler;
            _keywordSubscriptions.Remove(card.InstanceId);
        }

        _revealedInstanceIds.Remove(card.InstanceId);
    }

    private void OnDraw(Card card, DrawSource source)
    {
        if (source is DrawSource.Equipment or DrawSource.Created)
            _instanceIdToDrawSource[card.InstanceId] = source;

        void handler() => OnKeyWordAdded(card);
        _keywordSubscriptions[card.InstanceId] = handler;

        card.OnKeywordAdded += handler;
    }

    private void OnKeyWordAdded(Card card)
    {
        if (card.Keywords.Contains(Keywords.Marked))
        {
            _revealedInstanceIds.Add(card.InstanceId);
        }
    }

    private void OnCardAddedToBoard(Card card, PlayerID playerID)
    {
        if (playerID == _opponentId)
        {
            _seenInstanceIds.Add(card.InstanceId);
        }
    }

    public GameState SampleState(GameState state)
    {
        List<Card> equipmentCards = new(_equipmentCards);

        CombatPlayer opponent = _opponentId == PlayerID.Player1 ? state.Player1 : state.Player2;

        GameState clone = state.Clone();

        CombatPlayer opponentClone = _opponentId == PlayerID.Player1 ? clone.Player1 : clone.Player2;

        List<Card> knownHand = opponent.Hand.FindAll(card => _revealedInstanceIds.Contains(card.InstanceId)).Select(card => card.Clone()).ToList();

        List<Card> deck = SampleList(opponent.Deck, equipmentCards);
        List<Card> hand = SampleList(opponent.Hand.FindAll(card => !_revealedInstanceIds.Contains(card.InstanceId)), equipmentCards);

        deck.AddRange(hand);
        hand.Clear();

        hand.AddRange(knownHand);

        for (int i = hand.Count; i < opponent.Hand.Count; i++)
        {
            Card card = deck.GetRandomElement();
            deck.Remove(card);
            hand.Add(card);
        }

        opponentClone.Deck.Clear();
        opponentClone.Deck.AddRange(deck);

        opponentClone.Hand.Clear();
        opponentClone.Hand.AddRange(hand);

        opponentClone.ShuffleDeck();

        opponentClone.Graveyard.Clear();
        opponentClone.Graveyard.AddRange(SampleList(opponent.Graveyard, equipmentCards));

        opponentClone.Equipment.Clear();

        for (int i = 0; i < opponent.Equipment.Count; i++)
            opponentClone.Equipment.Add(SampleFromEquipmentCards(equipmentCards));

        opponentClone.ShuffleEquipmentDeck();

        return clone;
    }

    private List<Card> SampleList(List<Card> cards, List<Card> equipmentCards)
    {
        List<Card> result = new();

        foreach (Card card in cards)
        {
            if (_seenInstanceIds.Contains(card.InstanceId))
            {
                result.Add(card.Clone());
                continue;
            }

            if (!_instanceIdToDrawSource.TryGetValue(card.InstanceId, out DrawSource source))
                source = DrawSource.Deck;

            if (source is DrawSource.Equipment)
            {
                result.Add(SampleFromEquipmentCards(equipmentCards));
                continue;
            }

            result.Add(SampleFromDeckCards());
        }

        return result;
    }

    private Card SampleFromDeckCards() => AIUtilities.GetStandardCards().GetRandomElement().Clone();

    private Card SampleFromEquipmentCards(List<Card> equipmentCards)
    {
        Card card = equipmentCards.GetRandomElement();
        equipmentCards.Remove(card);
        return card.Clone();
    }
}