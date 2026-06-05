using System;
using System.Collections.Generic;
using UnityEngine;

public class StageMapManager : MonoBehaviour
{
    public static StageMapManager Instance { get; private set; }

    private const string CurrentNodeIdSaveKey = "StageMap.CurrentNodeId";
    private const string CurrentLayerIndexSaveKey = "StageMap.CurrentLayerIndex";
    private const string CurrentNodeCompletedSaveKey = "StageMap.CurrentNodeCompleted";
    private const string CurrentBattleWaveIdSaveKey = "StageMap.CurrentBattleWaveId";

    [Header("Map Data")]
    [SerializeField] private List<MapNodeSO> allNodes = new List<MapNodeSO>();
    [SerializeField] private bool loadPersistentDataOnAwake = true;

    [Header("Runtime State")]
    [SerializeField] private string currentNodeId = string.Empty;
    [SerializeField] private int currentLayerIndex = -1;
    [SerializeField] private bool currentNodeCompleted;
    [SerializeField] private int currentBattleWaveId = -1;

    public string CurrentNodeId => currentNodeId;
    public int CurrentLayerIndex => currentLayerIndex;
    public bool CurrentNodeCompleted => currentNodeCompleted;
    public int CurrentBattleWaveId => currentBattleWaveId;
    public MapNodeSO CurrentNode => FindNodeById(currentNodeId);
    public IReadOnlyList<MapNodeSO> AllNodes => allNodes;

    public event Action MapStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (loadPersistentDataOnAwake)
        {
            LoadMapState();
        }
    }

    private void OnValidate()
    {
        currentLayerIndex = Mathf.Max(-1, currentLayerIndex);
        RemoveInvalidNodes();
    }

    public static StageMapManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject managerObject = new GameObject("StageMapManager");
        return managerObject.AddComponent<StageMapManager>();
    }

    public void RegisterNode(MapNodeSO node)
    {
        if (node == null)
        {
            return;
        }

        if (allNodes == null)
        {
            allNodes = new List<MapNodeSO>();
        }

        if (!allNodes.Contains(node))
        {
            allNodes.Add(node);
        }
    }

    public bool IsNodeSelectable(MapNodeSO targetNode)
    {
        if (targetNode == null)
        {
            return false;
        }

        RegisterNode(targetNode);

        if (currentLayerIndex < 0)
        {
            return targetNode.LayerIndex == 0;
        }

        if (!currentNodeCompleted)
        {
            return false;
        }

        if (targetNode.LayerIndex != currentLayerIndex + 1)
        {
            return false;
        }

        MapNodeSO currentNode = FindNodeById(currentNodeId);
        if (currentNode == null)
        {
            return false;
        }

        return currentNode.HasNextNode(targetNode);
    }

    public bool SelectNode(MapNodeSO targetNode)
    {
        if (!IsNodeSelectable(targetNode))
        {
            return false;
        }

        currentNodeId = targetNode.NodeId;
        currentLayerIndex = targetNode.LayerIndex;
        currentNodeCompleted = false;
        currentBattleWaveId = targetNode.GetRandomBattleWaveId();

        SaveMapState();
        MapStateChanged?.Invoke();

        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.ChangeState(GameState.Intermission);
        }

        StageMapUIView mapView = StageMapUIView.Instance;
        if (mapView != null)
        {
            mapView.HideMap();
        }

        return true;
    }

    public void MarkCurrentNodeCompleted()
    {
        if (currentLayerIndex < 0 || string.IsNullOrEmpty(currentNodeId))
        {
            return;
        }

        currentNodeCompleted = true;
        SaveMapState();
        MapStateChanged?.Invoke();
    }

    public int CalculateCurrentNodeReward(bool victory, int fallbackVictoryReward, int fallbackDefeatReward)
    {
        MapNodeSO currentNode = CurrentNode;
        if (currentNode == null)
        {
            return Mathf.Max(0, victory ? fallbackVictoryReward : fallbackDefeatReward);
        }

        int bonus = victory ? currentNode.VictoryBonus : currentNode.DefeatBonus;
        return Mathf.Max(0, currentNode.BaseReward + bonus);
    }

    public void ResetMapProgress()
    {
        currentNodeId = string.Empty;
        currentLayerIndex = -1;
        currentNodeCompleted = false;
        currentBattleWaveId = -1;
        SaveMapState();
        MapStateChanged?.Invoke();
    }

    public void SaveMapState()
    {
        PlayerPrefs.SetString(CurrentNodeIdSaveKey, currentNodeId);
        PlayerPrefs.SetInt(CurrentLayerIndexSaveKey, currentLayerIndex);
        PlayerPrefs.SetInt(CurrentNodeCompletedSaveKey, currentNodeCompleted ? 1 : 0);
        PlayerPrefs.SetInt(CurrentBattleWaveIdSaveKey, currentBattleWaveId);
        PlayerPrefs.Save();
    }

    public void LoadMapState()
    {
        currentNodeId = PlayerPrefs.GetString(CurrentNodeIdSaveKey, currentNodeId);
        currentLayerIndex = PlayerPrefs.GetInt(CurrentLayerIndexSaveKey, currentLayerIndex);
        currentNodeCompleted = PlayerPrefs.GetInt(CurrentNodeCompletedSaveKey, currentNodeCompleted ? 1 : 0) == 1;
        currentBattleWaveId = PlayerPrefs.GetInt(CurrentBattleWaveIdSaveKey, currentBattleWaveId);
    }

    private MapNodeSO FindNodeById(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return null;
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            MapNodeSO node = allNodes[i];
            if (node != null && node.NodeId == nodeId)
            {
                return node;
            }
        }

        return null;
    }

    private void RemoveInvalidNodes()
    {
        if (allNodes == null)
        {
            allNodes = new List<MapNodeSO>();
            return;
        }

        for (int i = allNodes.Count - 1; i >= 0; i--)
        {
            if (allNodes[i] == null)
            {
                allNodes.RemoveAt(i);
            }
        }
    }
}
