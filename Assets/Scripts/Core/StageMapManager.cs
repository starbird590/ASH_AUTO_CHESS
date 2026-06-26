using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class StageMapManager : MonoBehaviour
{
    public static StageMapManager Instance { get; private set; }

    private const string CurrentNodeIdSaveKey = "StageMap.CurrentNodeId";
    private const string CurrentLayerIndexSaveKey = "StageMap.CurrentLayerIndex";
    private const string CurrentNodeCompletedSaveKey = "StageMap.CurrentNodeCompleted";
    private const string CurrentBattleWaveIdSaveKey = "StageMap.CurrentBattleWaveId";

    [Header("Legacy ScriptableObject Map Data")]
    [SerializeField] private List<MapNodeSO> allNodes = new List<MapNodeSO>();

    [Header("CSV Table Data")]
    [SerializeField] private TextAsset mapNodeCsv;
    [SerializeField] private TextAsset waveNodeCsv;
    [SerializeField] private string mapNodeCsvAssetPath = "Assets/Data/MapNode.csv";
    [SerializeField] private string waveNodeCsvAssetPath = "Assets/Data/WaveNode.csv";
    [SerializeField] private string mapNodeCsvResourceFallbackPath = "Data/MapNode";
    [SerializeField] private string waveNodeCsvResourceFallbackPath = "Data/WaveNode";
    [SerializeField] private bool loadCsvTablesOnAwake = true;
    [SerializeField] private bool loadPersistentDataOnAwake = false;

    [Header("Runtime State")]
    [SerializeField] private string currentNodeId = string.Empty;
    [SerializeField] private int currentLayerIndex = -1;
    [SerializeField] private bool currentNodeCompleted;
    [SerializeField] private string currentBattleWaveId = string.Empty;

    private readonly StageTableDatabase tableDatabase = new StageTableDatabase();
    private bool csvTablesLoaded;

    public string CurrentNodeId => currentNodeId;
    public int CurrentLayerIndex => currentLayerIndex;
    public bool CurrentNodeCompleted => currentNodeCompleted;
    public string CurrentBattleWaveId => currentBattleWaveId;
    public MapNodeSO CurrentNode => FindNodeById(currentNodeId);
    public MapNodeData CurrentRuntimeNode => FindRuntimeNodeById(currentNodeId);
    public IReadOnlyList<MapNodeSO> AllNodes => allNodes;
    public IReadOnlyList<MapNodeData> AllRuntimeNodes
    {
        get
        {
            EnsureCsvTablesLoaded();
            return tableDatabase.MapNodes;
        }
    }

    public bool HasRuntimeMapNodes
    {
        get
        {
            EnsureCsvTablesLoaded();
            return tableDatabase.HasMapNodes;
        }
    }

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

        if (loadCsvTablesOnAwake)
        {
            ReloadCsvTables();
        }

        if (loadPersistentDataOnAwake)
        {
            LoadMapState();
        }
    }

    private void OnValidate()
    {
        currentLayerIndex = Mathf.Max(-1, currentLayerIndex);
        RemoveInvalidNodes();
        TryAutoAssignCsvAssetsInEditor();
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

    public void ReloadCsvTables()
    {
        string mapCsvText = GetCsvText(mapNodeCsv, mapNodeCsvAssetPath, mapNodeCsvResourceFallbackPath);
        string waveCsvText = GetCsvText(waveNodeCsv, waveNodeCsvAssetPath, waveNodeCsvResourceFallbackPath);

        tableDatabase.ReplaceMapNodes(StageTableParser.ParseMapNodeCsv(mapCsvText));
        tableDatabase.ReplaceWaveNodes(StageTableParser.ParseWaveNodeCsv(waveCsvText));
        csvTablesLoaded = true;

        MapStateChanged?.Invoke();
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

    public bool IsNodeSelectable(MapNodeData targetNode)
    {
        if (targetNode == null)
        {
            return false;
        }

        EnsureCsvTablesLoaded();

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

        MapNodeData currentNode = FindRuntimeNodeById(currentNodeId);
        if (currentNode == null)
        {
            return false;
        }

        return currentNode.HasNextNode(targetNode);
    }

    public bool IsNodeSelectable(string targetNodeId)
    {
        EnsureCsvTablesLoaded();
        return tableDatabase.TryGetMapNode(targetNodeId, out MapNodeData node) && IsNodeSelectable(node);
    }

    public bool SelectNode(MapNodeSO targetNode)
    {
        if (!IsNodeSelectable(targetNode))
        {
            return false;
        }

        return SelectNodeInternal(targetNode.NodeId, targetNode.LayerIndex, targetNode.GetRandomBattleWaveId());
    }

    public bool SelectNode(MapNodeData targetNode)
    {
        if (!IsNodeSelectable(targetNode))
        {
            return false;
        }

        return SelectNodeInternal(targetNode.NodeId, targetNode.LayerIndex, targetNode.GetRandomBattleWaveId());
    }

    public bool SelectNode(string targetNodeId)
    {
        EnsureCsvTablesLoaded();
        if (tableDatabase.TryGetMapNode(targetNodeId, out MapNodeData runtimeNode))
        {
            return SelectNode(runtimeNode);
        }

        return SelectNode(FindNodeById(targetNodeId));
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
        MapNodeData runtimeNode = CurrentRuntimeNode;
        if (runtimeNode != null)
        {
            int runtimeBonus = victory ? runtimeNode.VictoryBonus : runtimeNode.DefeatBonus;
            return Mathf.Max(0, runtimeNode.BaseReward + runtimeBonus);
        }

        MapNodeSO currentNode = CurrentNode;
        if (currentNode == null)
        {
            return Mathf.Max(0, victory ? fallbackVictoryReward : fallbackDefeatReward);
        }

        int bonus = victory ? currentNode.VictoryBonus : currentNode.DefeatBonus;
        return Mathf.Max(0, currentNode.BaseReward + bonus);
    }

    public bool TryGetWaveNode(string waveId, out WaveNodeData waveNode)
    {
        EnsureCsvTablesLoaded();
        return tableDatabase.TryGetWaveNode(waveId, out waveNode);
    }

    public bool TryGetCurrentWaveNode(out WaveNodeData waveNode)
    {
        return TryGetWaveNode(currentBattleWaveId, out waveNode);
    }

    public bool TryGetCurrentWaveNode(out WaveNodeData waveNode, out bool hasWaveTableRows)
    {
        EnsureCsvTablesLoaded();
        hasWaveTableRows = tableDatabase.HasWaveNodes;
        return tableDatabase.TryGetWaveNode(currentBattleWaveId, out waveNode);
    }

    public void ResetMapProgress()
    {
        currentNodeId = string.Empty;
        currentLayerIndex = -1;
        currentNodeCompleted = false;
        currentBattleWaveId = string.Empty;
        SaveMapState();
        MapStateChanged?.Invoke();
    }

    public void SaveMapState()
    {
        PlayerPrefs.SetString(CurrentNodeIdSaveKey, currentNodeId);
        PlayerPrefs.SetInt(CurrentLayerIndexSaveKey, currentLayerIndex);
        PlayerPrefs.SetInt(CurrentNodeCompletedSaveKey, currentNodeCompleted ? 1 : 0);
        PlayerPrefs.SetString(CurrentBattleWaveIdSaveKey, currentBattleWaveId);
        PlayerPrefs.Save();
    }

    public void LoadMapState()
    {
        currentNodeId = PlayerPrefs.GetString(CurrentNodeIdSaveKey, currentNodeId);
        currentLayerIndex = PlayerPrefs.GetInt(CurrentLayerIndexSaveKey, currentLayerIndex);
        currentNodeCompleted = PlayerPrefs.GetInt(CurrentNodeCompletedSaveKey, currentNodeCompleted ? 1 : 0) == 1;
        currentBattleWaveId = PlayerPrefs.GetString(CurrentBattleWaveIdSaveKey, currentBattleWaveId);
    }

    private bool SelectNodeInternal(string nodeId, int layerIndex, string battleWaveId)
    {
        currentNodeId = nodeId;
        currentLayerIndex = layerIndex;
        currentNodeCompleted = false;
        currentBattleWaveId = battleWaveId;

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

    private MapNodeData FindRuntimeNodeById(string nodeId)
    {
        EnsureCsvTablesLoaded();
        return tableDatabase.TryGetMapNode(nodeId, out MapNodeData node) ? node : null;
    }

    private MapNodeSO FindNodeById(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || allNodes == null)
        {
            return null;
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            MapNodeSO node = allNodes[i];
            if (node != null && string.Equals(node.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }

    private void EnsureCsvTablesLoaded()
    {
        if (!csvTablesLoaded)
        {
            ReloadCsvTables();
        }
    }

    private string GetCsvText(TextAsset directAsset, string editorAssetPath, string resourceFallbackPath)
    {
        if (directAsset != null)
        {
            return StageTableParser.ReadTextAsset(directAsset);
        }

#if UNITY_EDITOR
        TextAsset editorAsset = LoadCsvAssetInEditor(editorAssetPath);
        if (editorAsset != null)
        {
            return StageTableParser.ReadTextAsset(editorAsset);
        }
#endif

        if (string.IsNullOrWhiteSpace(resourceFallbackPath))
        {
            return string.Empty;
        }

        TextAsset resourceAsset = Resources.Load<TextAsset>(resourceFallbackPath.Trim());
        return resourceAsset != null ? StageTableParser.ReadTextAsset(resourceAsset) : string.Empty;
    }

    private void TryAutoAssignCsvAssetsInEditor()
    {
#if UNITY_EDITOR
        if (mapNodeCsv == null)
        {
            mapNodeCsv = LoadCsvAssetInEditor(mapNodeCsvAssetPath);
        }

        if (waveNodeCsv == null)
        {
            waveNodeCsv = LoadCsvAssetInEditor(waveNodeCsvAssetPath);
        }
#endif
    }

#if UNITY_EDITOR
    private TextAsset LoadCsvAssetInEditor(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath.Trim().Replace('\\', '/'));
    }
#endif

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
