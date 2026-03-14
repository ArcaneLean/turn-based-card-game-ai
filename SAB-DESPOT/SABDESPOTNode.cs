using System.Collections.Generic;

public class SABDESPOTNode
{
    public CombatMove Move;
    public List<GameState> Scenarios;
    public SABDESPOTNode Parent;
    public List<SABDESPOTNode> Children;
    public float Value;
    public CombatMove BestMove;
}