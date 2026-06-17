using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class StageMapCSVImporter
{
    public const string MapNodeCsvPath = "Assets/Data/MapNode.csv";
    public const string OutputFolder = "Assets/MapNode";

    private static bool isSilentMapNodeImportRunning;

    public static bool IsSilentMapNodeImportRunning => isSilentMapNodeImportRunning;

    [MenuItem("ASH Auto Chess/Import Map Nodes From CSV")]
    public static void ImportMapNodesFromMenu()
    {
        ExecuteSilentMapNodeImport();
    }

    public static bool ExecuteSilentMapNodeImport()
    {
        if (isSilentMapNodeImportRunning)
        {
            Debug.LogWarning("[StageMapCSVImporter] MapNode.csv import is already running. Skipped duplicate request.");
            return false;
        }

        isSilentMapNodeImportRunning = true;
        try
        {
            TextAsset mapNodeCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(MapNodeCsvPath);
            if (mapNodeCsv == null)
            {
                Debug.LogWarning("[StageMapCSVImporter] Cannot find MapNode.csv at " + MapNodeCsvPath + ".");
                return false;
            }

            List<MapNodeData> nodes = StageTableParser.ParseMapNodeCsv(StageTableParser.ReadTextAsset(mapNodeCsv));
            if (nodes.Count == 0)
            {
                Debug.LogWarning("[StageMapCSVImporter] MapNode.csv has no valid node rows.");
                return false;
            }

            EnsureAssetFolder(OutputFolder);

            Dictionary<string, MapNodeSO> assetByNodeId = LoadOrCreateMapNodeAssets(nodes);
            ApplyMapNodeValues(nodes, assetByNodeId);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=#00FF66>[StageMapCSVImporter]</color> Imported " + nodes.Count + " MapNodeSO assets into " + OutputFolder + ".");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
        finally
        {
            isSilentMapNodeImportRunning = false;
        }
    }

    private static Dictionary<string, MapNodeSO> LoadOrCreateMapNodeAssets(List<MapNodeData> nodes)
    {
        Dictionary<string, MapNodeSO> result = new Dictionary<string, MapNodeSO>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < nodes.Count; i++)
        {
            MapNodeData node = nodes[i];
            if (node == null || string.IsNullOrWhiteSpace(node.NodeId))
            {
                continue;
            }

            string nodeId = node.NodeId.Trim();
            string assetPath = OutputFolder + "/" + MakeSafeFileName(nodeId) + ".asset";
            MapNodeSO asset = AssetDatabase.LoadAssetAtPath<MapNodeSO>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MapNodeSO>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            result[nodeId] = asset;
        }

        return result;
    }

    private static void ApplyMapNodeValues(List<MapNodeData> nodes, Dictionary<string, MapNodeSO> assetByNodeId)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            MapNodeData node = nodes[i];
            if (node == null || !assetByNodeId.TryGetValue(node.NodeId, out MapNodeSO asset) || asset == null)
            {
                continue;
            }

            SerializedObject serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("nodeId").stringValue = node.NodeId;
            serializedObject.FindProperty("layerIndex").intValue = Mathf.Max(0, node.LayerIndex);
            serializedObject.FindProperty("baseReward").intValue = Mathf.Max(0, node.BaseReward);
            serializedObject.FindProperty("victoryBonus").intValue = Mathf.Max(0, node.VictoryBonus);
            serializedObject.FindProperty("defeatBonus").intValue = Mathf.Max(0, node.DefeatBonus);

            WriteStringList(serializedObject.FindProperty("battleWaveIds"), node.BattleWaveIds);
            WriteNextNodeList(serializedObject.FindProperty("nextNodes"), node, assetByNodeId);

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }
    }

    private static void WriteStringList(SerializedProperty property, List<string> values)
    {
        if (property == null)
        {
            return;
        }

        int count = values != null ? values.Count : 0;
        property.arraySize = count;
        for (int i = 0; i < count; i++)
        {
            property.GetArrayElementAtIndex(i).stringValue = values[i];
        }
    }

    private static void WriteNextNodeList(
        SerializedProperty property,
        MapNodeData node,
        Dictionary<string, MapNodeSO> assetByNodeId)
    {
        if (property == null)
        {
            return;
        }

        List<MapNodeSO> resolvedNextNodes = new List<MapNodeSO>();
        if (node.NextNodeIds != null)
        {
            for (int i = 0; i < node.NextNodeIds.Count; i++)
            {
                string nextNodeId = node.NextNodeIds[i];
                if (string.IsNullOrWhiteSpace(nextNodeId))
                {
                    continue;
                }

                if (assetByNodeId.TryGetValue(nextNodeId.Trim(), out MapNodeSO nextNode) && nextNode != null)
                {
                    resolvedNextNodes.Add(nextNode);
                }
                else
                {
                    Debug.LogWarning("[StageMapCSVImporter] Node " + node.NodeId + " references missing NextNodeId: " + nextNodeId);
                }
            }
        }

        property.arraySize = resolvedNextNodes.Count;
        for (int i = 0; i < resolvedNextNodes.Count; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = resolvedNextNodes[i];
        }
    }

    private static void EnsureAssetFolder(string assetFolderPath)
    {
        string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized))
        {
            return;
        }

        string[] parts = normalized.Split('/');
        if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[StageMapCSVImporter] Output folder must be inside Assets: " + assetFolderPath);
            return;
        }

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static string MakeSafeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "MapNode" : value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            safe = safe.Replace(invalidChars[i], '_');
        }

        return safe;
    }
}
