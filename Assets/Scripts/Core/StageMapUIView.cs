using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StageMapUIView : MonoBehaviour
{
    private static StageMapUIView instance;

    [Header("Map Window")]
    [SerializeField] private GameObject mapRoot;

    [Header("Dynamic CSV Node UI")]
    [SerializeField] private bool generateRuntimeNodes = true;
    [SerializeField] private RectTransform nodeContainer;
    [SerializeField] private MapNodeButtonUI nodeButtonPrefab;
    [SerializeField] private Vector2 firstLayerAnchoredPosition = Vector2.zero;
    [SerializeField] private float horizontalSpacing = 180f;
    [SerializeField] private float verticalSpacing = 160f;
    [SerializeField] private bool positiveLayerMovesDown = true;
    [SerializeField] private bool rebuildRuntimeNodesOnShow = true;

    private readonly List<MapNodeButtonUI> generatedButtons = new List<MapNodeButtonUI>();

    public static StageMapUIView Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindLoadedInstance();
            }

            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[StageMapUIView] Multiple map views found. The latest enabled view will be used.");
        }

        instance = this;
        EnsureMapRoot();
    }

    private void OnEnable()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Reset()
    {
        mapRoot = gameObject;
        nodeContainer = transform as RectTransform;
    }

    public void ShowMap()
    {
        EnsureMapRoot();
        mapRoot.SetActive(true);

        if (rebuildRuntimeNodesOnShow)
        {
            RebuildRuntimeNodeButtons();
        }

        MapNodeButtonUI.RefreshAllButtons();
    }

    public void HideMap()
    {
        EnsureMapRoot();
        mapRoot.SetActive(false);
    }

    public void RebuildRuntimeNodeButtons()
    {
        if (!generateRuntimeNodes || nodeContainer == null || nodeButtonPrefab == null)
        {
            return;
        }

        StageMapManager mapManager = StageMapManager.EnsureInstance();
        IReadOnlyList<MapNodeData> nodes = mapManager.AllRuntimeNodes;
        if (nodes == null || nodes.Count == 0)
        {
            return;
        }

        ClearGeneratedButtons();

        Dictionary<int, List<MapNodeData>> nodesByLayer = GroupNodesByLayer(nodes);
        List<int> sortedLayers = new List<int>(nodesByLayer.Keys);
        sortedLayers.Sort();

        float yDirection = positiveLayerMovesDown ? -1f : 1f;
        for (int i = 0; i < sortedLayers.Count; i++)
        {
            int layerIndex = sortedLayers[i];
            List<MapNodeData> layerNodes = nodesByLayer[layerIndex];
            layerNodes.Sort(CompareNodeId);

            float y = firstLayerAnchoredPosition.y + layerIndex * verticalSpacing * yDirection;
            for (int nodeIndex = 0; nodeIndex < layerNodes.Count; nodeIndex++)
            {
                MapNodeButtonUI button = Instantiate(nodeButtonPrefab, nodeContainer);
                button.BindRuntimeNode(layerNodes[nodeIndex]);

                RectTransform rectTransform = button.transform as RectTransform;
                if (rectTransform != null)
                {
                    rectTransform.anchoredPosition = StageMapUILayoutUtility.CalculateCenteredRowPosition(
                        nodeIndex,
                        layerNodes.Count,
                        horizontalSpacing,
                        firstLayerAnchoredPosition.x,
                        y);
                }

                generatedButtons.Add(button);
            }
        }

        EnsureContainerHeight(sortedLayers);
    }

    private Dictionary<int, List<MapNodeData>> GroupNodesByLayer(IReadOnlyList<MapNodeData> nodes)
    {
        Dictionary<int, List<MapNodeData>> result = new Dictionary<int, List<MapNodeData>>();
        for (int i = 0; i < nodes.Count; i++)
        {
            MapNodeData node = nodes[i];
            if (node == null)
            {
                continue;
            }

            if (!result.TryGetValue(node.LayerIndex, out List<MapNodeData> layerNodes))
            {
                layerNodes = new List<MapNodeData>();
                result[node.LayerIndex] = layerNodes;
            }

            layerNodes.Add(node);
        }

        return result;
    }

    private void EnsureContainerHeight(List<int> sortedLayers)
    {
        if (nodeContainer == null || sortedLayers == null || sortedLayers.Count == 0)
        {
            return;
        }

        int minLayer = sortedLayers[0];
        int maxLayer = sortedLayers[sortedLayers.Count - 1];
        float neededHeight = Mathf.Abs(maxLayer - minLayer) * verticalSpacing + verticalSpacing;
        if (nodeContainer.sizeDelta.y < neededHeight)
        {
            nodeContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, neededHeight);
        }
    }

    private void ClearGeneratedButtons()
    {
        for (int i = generatedButtons.Count - 1; i >= 0; i--)
        {
            MapNodeButtonUI button = generatedButtons[i];
            if (button == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(button.gameObject);
            }
            else
            {
                DestroyImmediate(button.gameObject);
            }
        }

        generatedButtons.Clear();
    }

    private void EnsureMapRoot()
    {
        if (mapRoot == null)
        {
            mapRoot = gameObject;
        }
    }

    private static int CompareNodeId(MapNodeData left, MapNodeData right)
    {
        return string.Compare(left.NodeId, right.NodeId, System.StringComparison.OrdinalIgnoreCase);
    }

    private static StageMapUIView FindLoadedInstance()
    {
        StageMapUIView[] views = Resources.FindObjectsOfTypeAll<StageMapUIView>();
        for (int i = 0; i < views.Length; i++)
        {
            StageMapUIView view = views[i];
            if (view == null || view.hideFlags != HideFlags.None || !view.gameObject.scene.IsValid())
            {
                continue;
            }

            return view;
        }

        return null;
    }
}
