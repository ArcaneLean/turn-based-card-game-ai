using System.Collections.Generic;

public class ISMCTSNode
{
    public InformationGameState InformationGameState { get; }
    public ISMCTSNode Parent { get; }
    public Dictionary<CombatMove, List<ISMCTSNode>> Children { get; } = new();
    public bool IsTerminal => InformationGameState.IsCombatOver;
    public int VisitCount = 0;
    public ISMCTSStats P1Stats = new();
    public ISMCTSStats P2Stats = new();
    public PlayerID PlayerID { get; }
    public int Id { get; }

    public ISMCTSNode(InformationGameState informationGameState, ISMCTSNode parent, PlayerID playerID, int id)
    {
        InformationGameState = informationGameState;
        Parent = parent;
        PlayerID = playerID;
        Id = id;
    }
}