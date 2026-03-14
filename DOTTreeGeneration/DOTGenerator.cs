using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static class DOTGenerator
{
    public static int DotFileIndex = 0;
    private static string s_subFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "DOTFiles", DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss"));

    public static void Reset()
    {
        DotFileIndex = 0;
        s_subFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "DOTFiles", DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss"));
    }

    public static string GenerateDOT(ITreeNode root)
    {
        StringBuilder sb = new();
        sb.AppendLine("digraph G {");

        HashSet<string> visited = new();
        Queue<ITreeNode> queue = new();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ITreeNode current = queue.Dequeue();
            string currentId = current.GetID();

            if (!visited.Add(currentId))
                continue;

            sb.AppendLine($"    \"{currentId}\" [label=\"{current.GetLabel()}\", style=\"filled,{current.GetStyle()}\" fillcolor={current.GetColor()}, shape={current.GetShape()}];");

            foreach ((ITreeNode child, string edgelabel, string edgeColor) in current.GetLabeledChildren())
            {
                sb.AppendLine($"    \"{currentId}\" -> \"{child.GetID()}\" [label=\"{edgelabel ?? ""}\", color={edgeColor ?? "black"}];");
                queue.Enqueue(child);
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string SaveDOTFile(ITreeNode root)
    {
        string dotContent = GenerateDOT(root);

        string fileName = $"tree_{root.GetType().Name}_{DotFileIndex++:D3}";

        string filePath = Path.Combine(s_subFolder, fileName);
        string dotFilePath = filePath + ".dot";
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        File.WriteAllText(dotFilePath, dotContent);

        return dotFilePath;
    }
}