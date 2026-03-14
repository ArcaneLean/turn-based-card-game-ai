using System;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;

/**
The foundation of the personality system is a utility-based architecture [31], where multiple
competing objectives are represented as Intents. An Intent encapsulates a scoring function
mapping a given game state and player side to a numerical value representing the desir-
ability of that state for that player relative to a specific goal (e.g., maximizing damage or
preserving health).
A key design choice in this implementation is that Intents return their values on the
natural scale of the underlying game variables, rather than using normalization or abstract
scoring. For example, an Intent about survival directly returns the player's current health
points, while an Intent about resource availability may return the current energy value.
This decision is motivated by the desire to make the personalities interpretable and
easy to configure. Because Intents expose raw values, the relative importance of different
objectives can be expressed directly through their weights. For instance, if a designer
wishes to treat gaining 1 unit of energy as equally valuable as gaining 5 cards, this can be
achieved simply by assigning the Intent for energy a weight five times larger than that of
the Intent for cards. This makes personalities inherently about trade-offs between concrete
game quantities, providing an intuitive design space for balancing personalities.
This decomposition enables a multi-objective decision-making approach, consistent with
the principles of multi-attribute utility theory [13], while remaining directly grounded in
the semantics of the game's state variables.
**/
public class Intent
{
    private readonly Func<GameState, PlayerID, float> _utility;
    public readonly (float, float) Bounds;

    public Intent(Func<GameState, PlayerID, float> utility, (float, float) bounds)
    {
        _utility = utility;
        Bounds = bounds;
    }

    public float ScoreState(GameState state, PlayerID playerID) => _utility(state, playerID);

    public static Intent MaximizeDamage = new((state, playerID) =>
    {
        GameState clone = state.Clone();
        clone.ResolveChain().GetAwaiter().GetResult();

        CombatPlayer player = clone.Player1.ID == playerID ? clone.Player2 : clone.Player1;

        return Math.Clamp(-player.Health, -100f, 0f);
    }, (0f, 100f));

    public static Intent PreserveHealth = new((state, playerID) =>
    {
        GameState clone = state.Clone();
        clone.ResolveChain().GetAwaiter().GetResult();

        int health = clone.Player1.ID == playerID ? clone.Player1.Health : clone.Player2.Health;

        return Math.Clamp(health, 0f, 100f);
    }, (100f, 0f));

    public static Intent PreserveEnergy = new((state, playerID) =>
    {
        CombatPlayer player = state.Player1.ID == playerID ? state.Player1 : state.Player2;

        return Math.Clamp(player.Energy, 0f, 10f);
    }, (10f, 0f));

    public static Intent PreserveCards = new((state, playerID) =>
    {
        int cards = state.Player1.ID == playerID ? state.Player1.Hand.Count : state.Player2.Hand.Count;

        return cards;
    }, (10f, 0f));

    public static Intent Buff = new((state, playerID) =>
    {
        CombatPlayer player = state.Player1.ID == playerID ? state.Player1 : state.Player2;
        int buffs = player.LastingEffects.Sum(effect => effect.Stacks);

        return Math.Clamp(buffs, 0f, 50f);
    }, (50f, 0f));

    public static Intent Debuff = new((state, playerID) =>
    {
        CombatPlayer player = state.Player1.ID == playerID ? state.Player2 : state.Player1;
        int buffs = player.LastingEffects.Sum(effect => effect.Stacks);

        return Math.Clamp(buffs, 0f, 50f);
    }, (50f, 0f));

    public static Intent HaveDefensive = new((state, playerID) =>
    {
        List<Card> hand = state.Player1.ID == playerID ? state.Player1.Hand : state.Player2.Hand;

        int totalBlock = 0;
        foreach (Card card in hand)
        {
            totalBlock += card.Block;
        }

        return Math.Clamp(totalBlock, 0f, 50f);
    }, (50f, 0f));

    public static Intent HaveOffensive = new((state, playerID) =>
    {
        CombatPlayer player = state.Player1.ID == playerID ? state.Player1 : state.Player2;
        List<Card> hand = player.Hand;

        int totalDamage = 0;
        foreach (Card card in hand)
        {
            totalDamage += card.Damage;
        }

        return Math.Clamp(totalDamage, 0f, 50f);
    }, (50f, 0f));

    public static Intent Surge = new((state, playerID) =>
    {
        int energyPerTurn = state.Player1.ID == playerID ? state.Player1.EnergyPerTurn : state.Player2.EnergyPerTurn;

        return Math.Clamp(energyPerTurn, 3f, 10f);
    }, (10f, 3f));

    public static Intent PreventDeath = new((state, playerID) =>
    {
        GameState clone = state.Clone();
        clone.ResolveChain().GetAwaiter().GetResult();
        bool dead = playerID == clone.Player1.ID ? clone.Player1.Health <= 0 : clone.Player2.Health <= 0;

        return dead ? -1.0f : 0f;
    }, (0f, -1.0f));
}

/**
The place where the Intents can be assigned weights are the Personality Profiles, where the
Intents collectively represent distinct strategic personalities. Because Intents return values
in the natural scale of game variables, the configuration of Profiles become transparent
and intuitive: designers can specify, in concrete terms, how much one outcome is worth
relative to another.
This trade-off-based design simplifies the creation of diverse and interpretable Profiles.
An Aggressive Profile, for instance, might assign high weight to an Intent that scores dam-
age while giving lower weight to an Intent that scores the player's health points, whereas
a Defensive Profile would invert this emphasis. The result is a system in which strategic
personalities are not abstract parameter sets but grounded, quantitative preferences over
real game outcomes.
In addition to weighted Intents, each Personality Profile is associated with a risk tol-
erance parameter r, where r = 1 corresponds to highly risk-seeking behavior and r = -1
corresponds to strongly risk-averse behavior. This parameter influences the aggregation
of scores during backpropagation: profiles with higher risk tolerance apply a bonus to
the variance of candidate outcomes, favoring strategies with uncertain but potentially
high payoffs, while risk-averse profiles penalize variance, preferring safer, more predictable
strategies. This mechanism extends the trade-off design principle beyond static values
to also encompass how uncertainty and volatility are treated in decision-making. The
mathematical side of this risk-aware behavior is explored in appendix A.
Conceptually, the design of Personality Profiles can be loosely compared to trait-based
personality models (e.g., the Big Five [19]), in the sense that complex behavior emerges
from weighted underlying tendencies. However, this analogy is purely structural: the
Profiles in this system are not intended to represent psychological traits, nor are they
validated as such. Instead, they serve as heuristic configurations that guide the AI toward
more aggressive, defensive, or risk-taking behavior.
**/
public class PersonalityProfile
{
    public readonly string Name;
    public readonly float Risk;
    public readonly Lazy<(float LowerBound, float UpperBound)> Bounds;
    private readonly List<(Intent intent, float weight)> _intentWeights;

    public PersonalityProfile(List<(Intent, float)> intentWeights, string name, float risk = 0f)
    {
        _intentWeights = intentWeights;
        Name = name;
        Risk = risk;
        Bounds = new(ComputeBounds);
    }

    public float GetWeight(Intent intent)
    {
        return _intentWeights.Where(x => x.intent == intent)
            .Select(x => x.weight)
            .FirstOrDefault();
    }

    public float ScoreState(GameState state, PlayerID playerID)
    {
        float res = 0f;

        foreach ((Intent intent, float weight) in _intentWeights)
        {
            res += weight * intent.ScoreState(state, playerID);
        }

        return res;
    }

    private (float LowerBound, float UpperBound) ComputeBounds()
    {
        float upper = 0f;
        float lower = 0f;

        foreach ((Intent intent, float weight) in _intentWeights)
        {
            upper += weight * intent.Bounds.Item1;
            lower += weight * intent.Bounds.Item2;
        }

        return (lower, upper);
    }

    public static PersonalityProfile Aggressive = new(new()
    {
        (Intent.MaximizeDamage, 1.0f),
        (Intent.PreserveHealth, 0.4f),
        (Intent.PreserveCards, 0.3f),
        (Intent.HaveOffensive, 0.05f),
        (Intent.Buff, 3.0f),
        (Intent.Debuff, 3.0f),
        (Intent.PreserveEnergy, 0.3f),
        (Intent.Surge, 5.0f),
        (Intent.PreventDeath, 100.0f)
    }, "Aggressive", risk: 0.8f);

    public static PersonalityProfile Defensive = new(new()
    {
        (Intent.MaximizeDamage, 0.5f),
        (Intent.PreserveHealth, 1.0f),
        (Intent.PreserveCards, 0.4f),
        (Intent.HaveDefensive, 0.05f),
        (Intent.Buff, 3.0f),
        (Intent.Debuff, 3.0f),
        (Intent.PreserveEnergy, 0.6f),
        (Intent.Surge, 6.0f),
        (Intent.PreventDeath, 100.0f)
    }, "Defensive", risk: -0.8f);

    public static PersonalityProfile DynamicAggressive = new(new()
    {
        (Intent.MaximizeDamage, 1.0f),
        (Intent.PreserveHealth, 0.5f),
        (Intent.PreserveCards, 0.3f),
        (Intent.HaveOffensive, 0.05f),
        (Intent.Buff, 3.0f),
        (Intent.Debuff, 3.0f),
        (Intent.PreserveEnergy, 0.3f),
        (Intent.Surge, 5.0f),
        (Intent.PreventDeath, 100.0f)
    }, "DynamicAggressive", risk: 0.5f);

    public static PersonalityProfile DynamicDefensive = new(new()
    {
        (Intent.MaximizeDamage, 0.6f),
        (Intent.PreserveHealth, 1.0f),
        (Intent.PreserveCards, 0.4f),
        (Intent.HaveDefensive, 0.05f),
        (Intent.Buff, 3.0f),
        (Intent.Debuff, 3.0f),
        (Intent.PreserveEnergy, 0.6f),
        (Intent.Surge, 6.0f),
        (Intent.PreventDeath, 100.0f)
    }, "DynamicDefensive", risk: -0.5f);

    public static PersonalityProfile NoPersonality = new(new()
    {
        (Intent.MaximizeDamage, 1.0f),
        (Intent.PreserveHealth, 0.8f),
        (Intent.PreserveCards, 0.5f),
        (Intent.Buff, 3.0f),
        (Intent.Debuff, 3.0f),
        (Intent.PreserveEnergy, 0.5f),
        (Intent.Surge, 5.0f),
        (Intent.PreventDeath, 100.0f)
    }, "NoPersonality", risk: 0f);
}

/**
To enhance realism and avoid static, predictable patterns, the outer component of the
personality system is the Personality Finite State Machine (PersonalityFSM). This com-
ponent dynamically selects the active Profile based on contextual triggers in the game state,
such as health values or card availability. For example, if the agent's health falls below
a threshold, the FSM may transition from an Aggressive Profile to a Defensive Profile,
mirroring adaptive human behavior under stress.
**/
public abstract class PersonalityFSM
{
    public PersonalityProfile CurrentProfile;

    public abstract PersonalityProfile Update(GameState state);

    protected void Switch(PersonalityProfile profile) => CurrentProfile = profile;
}

public class AggressiveFSM : PersonalityFSM
{
    public AggressiveFSM() : base()
    {
        CurrentProfile = PersonalityProfile.Aggressive;
    }

    public override PersonalityProfile Update(GameState state) => CurrentProfile;
}

public class DefensiveFSM : PersonalityFSM
{
    public DefensiveFSM() : base()
    {
        CurrentProfile = PersonalityProfile.Defensive;
    }

    public override PersonalityProfile Update(GameState state) => CurrentProfile;
}

public class DynamicFSM : PersonalityFSM
{
    public DynamicFSM() : base()
    {
        CurrentProfile = PersonalityProfile.DynamicAggressive;
    }

    public override PersonalityProfile Update(GameState state)
    {
        switch (CurrentProfile.Name)
        {
            case "DynamicAggressive":
                if (state.ActivePlayer.Health <= 12)
                    Switch(PersonalityProfile.DynamicDefensive);
                break;
            case "DynamicDefensive":
                if (state.ActivePlayer.Health >= 15 && state.InactivePlayer.Health <= 12)
                    Switch(PersonalityProfile.DynamicAggressive);
                else if (state.ActivePlayer.Health <= 12 && state.InactivePlayer.Health <= 12)
                    Switch(Random.value < 0.3f ? PersonalityProfile.DynamicAggressive : PersonalityProfile.DynamicDefensive);
                break;
            default:
                break;
        }

        return CurrentProfile;
    }
}

public class NoPersonalityFSM : PersonalityFSM
{
    public NoPersonalityFSM() : base()
    {
        CurrentProfile = PersonalityProfile.NoPersonality;
    }

    public override PersonalityProfile Update(GameState state) => CurrentProfile;
}

public class TestFSM : PersonalityFSM
{
    public TestFSM(PersonalityProfile profile) : base()
    {
        CurrentProfile = profile;
    }

    public override PersonalityProfile Update(GameState state) => CurrentProfile;
}