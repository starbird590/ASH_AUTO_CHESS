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

    [Header("UI References")]
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;

    [Header("Visual State")]
    [SerializeField] private Color selectableColor = Color.white;
    [SerializeField] private Color lockedColor = new Color(0.35f, 0.35f, 0.35f, 0.45f);

    public MapNodeSO Node => node;

    private void Reset()
    {
        button = GetComponent<Button>();
        iconImage = GetComponent<Image>();
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
        bool selectable = mapManager != null && mapManager.IsNodeSelectable(node);

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
        if (!mapManager.SelectNode(node))
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
}
