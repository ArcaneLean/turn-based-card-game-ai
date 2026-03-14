#nullable enable

using System.Collections.Generic;

public interface ITreeNode
{
    string GetLabel();
    string GetShape() => "ellipse";
    string GetColor() => "white";
    string GetStyle() => "solid";
    string GetID();
    IEnumerable<(ITreeNode Child, string? EdgeLabel, string? EdgeColor)> GetLabeledChildren();
}