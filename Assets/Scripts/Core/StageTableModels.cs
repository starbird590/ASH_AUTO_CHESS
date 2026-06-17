using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MapNodeData
{
    public string NodeId;
    public int LayerIndex;
    public List<string> BattleWaveIds = new List<string>();
    public List<string> NextNodeIds = new List<string>();
    public int BaseReward;
    public int VictoryBonus;
    public int DefeatBonus;

    public string GetRandomBattleWaveId()
    {
        if (BattleWaveIds == null || BattleWaveIds.Count == 0)
        {
            Debug.LogWarning("[MapNodeData] Node " + NodeId + " has no battle wave candidates. Returning empty wave id.");
            return string.Empty;
        }

        return BattleWaveIds[UnityEngine.Random.Range(0, BattleWaveIds.Count)];
    }

    public bool HasNextNode(MapNodeData targetNode)
    {
        return targetNode != null && HasNextNodeId(targetNode.NodeId);
    }

    public bool HasNextNodeId(string targetNodeId)
    {
        if (string.IsNullOrWhiteSpace(targetNodeId) || NextNodeIds == null)
        {
            return false;
        }

        for (int i = 0; i < NextNodeIds.Count; i++)
        {
            if (string.Equals(NextNodeIds[i], targetNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

[Serializable]
public sealed class WaveEnemyConfigData
{
    public string ChessId;
    public Vector2Int GridPosition;
}

[Serializable]
public sealed class BossInfoData
{
    public string BossChessId;
    public Vector2Int GridPosition;
    public List<string> SpawnPoolChessIds = new List<string>();

    public bool HasBossChessId => !string.IsNullOrWhiteSpace(BossChessId);
}

[Serializable]
public sealed class BossSpawnData
{
    public float BaseSpawnInterval = 6f;
    public float EnrageAcceleration = 0.05f;
    public float MinSpawnInterval = 1.2f;
}

[Serializable]
public sealed class WaveNodeData
{
    public string WaveId;
    public List<WaveEnemyConfigData> InitialEnemyConfigs = new List<WaveEnemyConfigData>();
    public bool HasBoss;
    public BossInfoData BossInfo = new BossInfoData();
    public BossSpawnData BossSpawn = new BossSpawnData();
}

public sealed class StageTableDatabase
{
    private readonly List<MapNodeData> mapNodes = new List<MapNodeData>();
    private readonly List<WaveNodeData> waveNodes = new List<WaveNodeData>();
    private readonly Dictionary<string, MapNodeData> mapNodeById = new Dictionary<string, MapNodeData>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WaveNodeData> waveNodeById = new Dictionary<string, WaveNodeData>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MapNodeData> MapNodes => mapNodes;
    public IReadOnlyList<WaveNodeData> WaveNodes => waveNodes;
    public bool HasMapNodes => mapNodes.Count > 0;
    public bool HasWaveNodes => waveNodes.Count > 0;

    public void ReplaceMapNodes(IEnumerable<MapNodeData> nodes)
    {
        mapNodes.Clear();
        mapNodeById.Clear();

        if (nodes == null)
        {
            return;
        }

        foreach (MapNodeData node in nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.NodeId))
            {
                continue;
            }

            string key = node.NodeId.Trim();
            node.NodeId = key;
            mapNodeById[key] = node;
        }

        mapNodes.AddRange(mapNodeById.Values);
        mapNodes.Sort(CompareMapNodes);
    }

    public void ReplaceWaveNodes(IEnumerable<WaveNodeData> nodes)
    {
        waveNodes.Clear();
        waveNodeById.Clear();

        if (nodes == null)
        {
            return;
        }

        foreach (WaveNodeData node in nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.WaveId))
            {
                continue;
            }

            string key = node.WaveId.Trim();
            node.WaveId = key;
            waveNodeById[key] = node;
        }

        waveNodes.AddRange(waveNodeById.Values);
        waveNodes.Sort((left, right) => string.Compare(left.WaveId, right.WaveId, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryGetMapNode(string nodeId, out MapNodeData node)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            node = null;
            return false;
        }

        return mapNodeById.TryGetValue(nodeId.Trim(), out node);
    }

    public bool TryGetWaveNode(string waveId, out WaveNodeData node)
    {
        if (string.IsNullOrWhiteSpace(waveId))
        {
            node = null;
            return false;
        }

        return waveNodeById.TryGetValue(waveId.Trim(), out node);
    }

    private static int CompareMapNodes(MapNodeData left, MapNodeData right)
    {
        int layerCompare = left.LayerIndex.CompareTo(right.LayerIndex);
        if (layerCompare != 0)
        {
            return layerCompare;
        }

        return string.Compare(left.NodeId, right.NodeId, StringComparison.OrdinalIgnoreCase);
    }
}
