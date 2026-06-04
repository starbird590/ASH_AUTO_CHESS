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

    private readonly List<SynergyItemUI> itemPool = new List<SynergyItemUI>();

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
        Dictionary<TraitSO, int> ledger = SynergyManager.Instance.CurrentTraitCounts;
        foreach (KeyValuePair<TraitSO, int> pair in ledger)
        {
            if (pair.Key == null || pair.Value <= 0)
            {
                continue;
            }

            SynergyItemUI item = GetOrCreateItem(visibleIndex);
            if (item == null)
            {
                continue;
            }

            item.transform.SetParent(contentRoot, false);
            item.Refresh(pair.Key, pair.Value);
            item.gameObject.SetActive(true);
            visibleIndex++;
        }
    }

    private SynergyItemUI GetOrCreateItem(int index)
    {
        while (itemPool.Count <= index)
        {
            SynergyItemUI newItem = Instantiate(itemPrefab, contentRoot);
            newItem.gameObject.SetActive(false);
            itemPool.Add(newItem);
        }

        return itemPool[index];
    }
}
