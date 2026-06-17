using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MapNode", menuName = "ASH Auto Chess/Map Node")]
public class MapNodeSO : ScriptableObject
{
    [Header("Node Identity")]
    [SerializeField] private string nodeId;
    [SerializeField] private int layerIndex;

    [Header("Battle Wave Candidates")]
    [SerializeField] private List<string> battleWaveIds = new List<string>();

    [Header("Topology")]
    [SerializeField] private List<MapNodeSO> nextNodes = new List<MapNodeSO>();

    [Header("Reward")]
    [SerializeField] private int baseReward = 5;
    [SerializeField] private int victoryBonus = 10;
    [SerializeField] private int defeatBonus = 3;

    public string NodeId => nodeId;
    public int LayerIndex => layerIndex;
    public int BaseReward => baseReward;
    public int VictoryBonus => victoryBonus;
    public int DefeatBonus => defeatBonus;
    public IReadOnlyList<string> BattleWaveIds => battleWaveIds;
    public IReadOnlyList<MapNodeSO> NextNodes => nextNodes;

    private void OnValidate()
    {
        if (battleWaveIds == null)
        {
            battleWaveIds = new List<string>();
        }

        if (nextNodes == null)
        {
            nextNodes = new List<MapNodeSO>();
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = name;
        }

        layerIndex = Mathf.Max(0, layerIndex);
        baseReward = Mathf.Max(0, baseReward);
        victoryBonus = Mathf.Max(0, victoryBonus);
        defeatBonus = Mathf.Max(0, defeatBonus);

        for (int i = battleWaveIds.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(battleWaveIds[i]))
            {
                battleWaveIds.RemoveAt(i);
                continue;
            }

            battleWaveIds[i] = battleWaveIds[i].Trim();
        }

        for (int i = nextNodes.Count - 1; i >= 0; i--)
        {
            if (nextNodes[i] == null || nextNodes[i] == this)
            {
                nextNodes.RemoveAt(i);
            }
        }
    }

    public string GetRandomBattleWaveId()
    {
        if (battleWaveIds == null || battleWaveIds.Count == 0)
        {
            Debug.LogWarning("[MapNodeSO] Node " + name + " has no battle wave candidates. Returning empty wave id.");
            return string.Empty;
        }

        int randomIndex = Random.Range(0, battleWaveIds.Count);
        return battleWaveIds[randomIndex];
    }

    public bool HasNextNode(MapNodeSO targetNode)
    {
        if (targetNode == null || nextNodes == null)
        {
            return false;
        }

        for (int i = 0; i < nextNodes.Count; i++)
        {
            MapNodeSO nextNode = nextNodes[i];
            if (nextNode == null)
            {
                continue;
            }

            if (nextNode == targetNode || nextNode.NodeId == targetNode.NodeId)
            {
                return true;
            }
        }

        return false;
    }
}
