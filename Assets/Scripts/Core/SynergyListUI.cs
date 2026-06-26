using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 左侧常驻羁绊面板。监听 SynergyManager 的账本事件，并用对象池循环复用条目。
/// </summary>
public class SynergyListUI : MonoBehaviour
{
    [Header("UI Pool")]
    public SynergyItemUI itemPrefab;
    public Transform contentRoot;

    [Header("Detail Popup")]
    public SynergyDetailPanelUI detailPanelView;
    public float longPressSeconds = 0.45f;

    private readonly List<SynergyItemUI> itemPool = new List<SynergyItemUI>();

    public float LongPressSeconds
    {
        get { return Mathf.Max(0.1f, longPressSeconds); }
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshList();
    }

    private void Start()
    {
        Subscribe();
        RefreshList();
    }

    private void OnDisable()
    {
        if (SynergyManager.HasInstance)
        {
            SynergyManager.Instance.OnSynergyUpdated -= RefreshList;
        }
    }

    private void Subscribe()
    {
        if (SynergyManager.Instance == null)
        {
            return;
        }

        SynergyManager.Instance.OnSynergyUpdated -= RefreshList;
        SynergyManager.Instance.OnSynergyUpdated += RefreshList;
    }

    public void RefreshList()
    {
        for (int i = 0; i < itemPool.Count; i++)
        {
            if (itemPool[i] != null)
            {
                itemPool[i].gameObject.SetActive(false);
            }
        }

        if (SynergyManager.Instance == null || itemPrefab == null || contentRoot == null)
        {
            return;
        }

        int visibleIndex = 0;
        IReadOnlyList<TraitSynergyDisplayModel> displayModels = SynergyManager.Instance.TraitDisplayModels;
        for (int modelIndex = 0; modelIndex < displayModels.Count; modelIndex++)
        {
            TraitSynergyDisplayModel displayModel = displayModels[modelIndex];
            if (displayModel == null || displayModel.Trait == null || displayModel.UnitCount <= 0)
            {
                continue;
            }

            SynergyItemUI item = GetOrCreateItem(visibleIndex);
            if (item == null)
            {
                continue;
            }

            item.transform.SetParent(contentRoot, false);
            item.SetOwner(this);
            item.Refresh(displayModel);
            item.gameObject.SetActive(true);
            visibleIndex++;
        }
    }

    public void ShowTraitDetails(TraitSynergyDisplayModel displayModel, Vector2 screenPosition)
    {
        if (displayModel == null || displayModel.Trait == null)
        {
            HideTraitDetails();
            return;
        }

        if (detailPanelView != null)
        {
            detailPanelView.Show(displayModel, screenPosition);
        }
    }

    public void HideTraitDetails()
    {
        if (detailPanelView != null)
        {
            detailPanelView.Hide();
        }
    }

    private SynergyItemUI GetOrCreateItem(int index)
    {
        while (itemPool.Count <= index)
        {
            SynergyItemUI newItem = Instantiate(itemPrefab, contentRoot);
            newItem.SetOwner(this);
            newItem.gameObject.SetActive(false);
            itemPool.Add(newItem);
        }

        return itemPool[index];
    }
}
