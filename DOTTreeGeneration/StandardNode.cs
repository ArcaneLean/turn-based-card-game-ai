using System.Collections.Generic;

public class StandardNode : ITreeNode
{
    private static int s_idCounter = 0;
    private readonly int _id = s_idCounter++;
    private readonly string _label;
    private readonly List<(ITreeNode, string, string)> _children = new();

    public StandardNode(string label)
    {
        _label = label;
    }

    public void AddChild(ITreeNode child, string edgeLabel, string edgeColor) => _children.Add((child, edgeLabel, edgeColor));

    public string GetID() => $"result_{_id}";

    public string GetLabel() => _label;

    public string GetShape() => "box";

    public IEnumerable<(ITreeNode Child, string EdgeLabel, string EdgeColor)> GetLabeledChildren() => _children;
}