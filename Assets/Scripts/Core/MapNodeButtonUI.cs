using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class MapNodeButtonUI : MonoBehaviour, IPointerClickHandler
{
    private static readonly List<MapNodeButtonUI> ActiveButtons = new List<MapNodeButtonUI>();

    [Header("Node Binding")]
    [SerializeField] private MapNodeSO node;
    [SerializeField] private string runtimeNodeId;

    [Header("UI References")]
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private Text labelText;

    [Header("Visual State")]
    [SerializeField] private Color selectableColor = Color.white;
    [SerializeField] private Color lockedColor = new Color(0.35f, 0.35f, 0.35f, 0.45f);

    private MapNodeData runtimeNode;

    public MapNodeSO Node => node;
    public string RuntimeNodeId => !string.IsNullOrWhiteSpace(runtimeNodeId)
        ? runtimeNodeId
        : runtimeNode != null
            ? runtimeNode.NodeId
            : string.Empty;

    private void Reset()
    {
        button = GetComponent<Button>();
        iconImage = GetComponent<Image>();
        labelText = GetComponentInChildren<Text>(true);
    }

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (iconImage == null)
        {
            iconImage = GetComponent<Image>();
        }

        if (labelText == null)
        {
            labelText = GetComponentInChildren<Text>(true);
        }
    }

    private void OnEnable()
    {
        if (!ActiveButtons.Contains(this))
        {
            ActiveButtons.Add(this);
        }

        StageMapManager mapManager = StageMapManager.EnsureInstance();
        if (node != null)
        {
            mapManager.RegisterNode(node);
        }

        mapManager.MapStateChanged -= HandleMapStateChanged;
        mapManager.MapStateChanged += HandleMapStateChanged;
        RefreshVisualState();
    }

    private void OnDisable()
    {
        ActiveButtons.Remove(this);

        if (StageMapManager.Instance != null)
        {
            StageMapManager.Instance.MapStateChanged -= HandleMapStateChanged;
        }
    }

    private void Update()
    {
        RefreshVisualState();
    }

    public void BindNode(MapNodeSO nextNode)
    {
        node = nextNode;
        runtimeNode = null;
        runtimeNodeId = string.Empty;
        UpdateLabel();
        RefreshVisualState();
    }

    public void BindRuntimeNode(MapNodeData nextNode)
    {
        runtimeNode = nextNode;
        runtimeNodeId = nextNode != null ? nextNode.NodeId : string.Empty;
        node = null;
        UpdateLabel();
        RefreshVisualState();
    }

    private void HandleMapStateChanged()
    {
        RefreshVisualState();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        TrySelectNode();
    }

    public void RefreshVisualState()
    {
        StageMapManager mapManager = StageMapManager.Instance;
        bool selectable = mapManager != null && IsSelectable(mapManager);

        if (button != null)
        {
            button.interactable = selectable;
        }

        if (iconImage != null)
        {
            iconImage.color = selectable ? selectableColor : lockedColor;
        }
    }

    public bool TrySelectNode()
    {
        StageMapManager mapManager = StageMapManager.EnsureInstance();
        bool selected = false;
        if (runtimeNode != null)
        {
            selected = mapManager.SelectNode(runtimeNode);
        }
        else if (!string.IsNullOrWhiteSpace(runtimeNodeId))
        {
            selected = mapManager.SelectNode(runtimeNodeId);
        }
        else
        {
            selected = mapManager.SelectNode(node);
        }

        if (!selected)
        {
            RefreshVisualState();
            return false;
        }

        RefreshAllButtons();
        return true;
    }

    public static void RefreshAllButtons()
    {
        for (int i = ActiveButtons.Count - 1; i >= 0; i--)
        {
            MapNodeButtonUI buttonUI = ActiveButtons[i];
            if (buttonUI == null)
            {
                ActiveButtons.RemoveAt(i);
                continue;
            }

            buttonUI.RefreshVisualState();
        }
    }

    private bool IsSelectable(StageMapManager mapManager)
    {
        if (runtimeNode != null)
        {
            return mapManager.IsNodeSelectable(runtimeNode);
        }

        if (!string.IsNullOrWhiteSpace(runtimeNodeId))
        {
            return mapManager.IsNodeSelectable(runtimeNodeId);
        }

        return mapManager.IsNodeSelectable(node);
    }

    private void UpdateLabel()
    {
        if (labelText == null)
        {
            return;
        }

        if (runtimeNode != null)
        {
            labelText.text = runtimeNode.NodeId;
            return;
        }

        if (!string.IsNullOrWhiteSpace(runtimeNodeId))
        {
            labelText.text = runtimeNodeId;
            return;
        }

        labelText.text = node != null ? node.NodeId : string.Empty;
    }
}
