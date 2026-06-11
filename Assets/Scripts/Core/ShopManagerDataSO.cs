using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShopPoolConfig
{
    public int unitRare;
    public int cardCount;
}

[Serializable]
public class ShopProbabilityConfig
{
    public int shopLevel;
    public float weightT1;
    public float weightT2;
    public float weightT3;
    public float weightT4;
    public float weightT5;
}

[CreateAssetMenu(fileName = "ShopManagerData", menuName = "ASH Auto Chess/Shop Manager Data")]
public class ShopManagerDataSO : ScriptableObject
{
    [SerializeField] private List<ShopPoolConfig> poolConfigs = new List<ShopPoolConfig>();
    [SerializeField] private List<ShopProbabilityConfig> probabilityConfigs = new List<ShopProbabilityConfig>();

    private Dictionary<int, ShopPoolConfig> poolConfigByRare;
    private Dictionary<int, ShopProbabilityConfig> probabilityConfigByLevel;

    public IReadOnlyList<ShopPoolConfig> PoolConfigs => poolConfigs;
    public IReadOnlyList<ShopProbabilityConfig> ProbabilityConfigs => probabilityConfigs;

    public bool TryGetPoolConfig(int unitRare, out ShopPoolConfig config)
    {
        EnsurePoolLookup();
        return poolConfigByRare.TryGetValue(unitRare, out config);
    }

    public bool TryGetProbabilityConfig(int shopLevel, out ShopProbabilityConfig config)
    {
        EnsureProbabilityLookup();
        return probabilityConfigByLevel.TryGetValue(shopLevel, out config);
    }

    public int GetCardCountOrDefault(int unitRare, int defaultValue)
    {
        ShopPoolConfig config;
        return TryGetPoolConfig(unitRare, out config) ? Mathf.Max(0, config.cardCount) : defaultValue;
    }

    public void ClearPoolConfigs()
    {
        poolConfigs.Clear();
        poolConfigByRare = null;
    }

    public void ClearProbabilityConfigs()
    {
        probabilityConfigs.Clear();
        probabilityConfigByLevel = null;
    }

    public void SetPoolConfig(ShopPoolConfig config)
    {
        if (config == null)
        {
            return;
        }

        config.unitRare = Mathf.Max(0, config.unitRare);
        config.cardCount = Mathf.Max(0, config.cardCount);

        for (int i = 0; i < poolConfigs.Count; i++)
        {
            if (poolConfigs[i] != null && poolConfigs[i].unitRare == config.unitRare)
            {
                poolConfigs[i] = config;
                poolConfigByRare = null;
                return;
            }
        }

        poolConfigs.Add(config);
        poolConfigByRare = null;
    }

    public void SetProbabilityConfig(ShopProbabilityConfig config)
    {
        if (config == null)
        {
            return;
        }

        config.shopLevel = Mathf.Max(0, config.shopLevel);
        config.weightT1 = Mathf.Max(0f, config.weightT1);
        config.weightT2 = Mathf.Max(0f, config.weightT2);
        config.weightT3 = Mathf.Max(0f, config.weightT3);
        config.weightT4 = Mathf.Max(0f, config.weightT4);
        config.weightT5 = Mathf.Max(0f, config.weightT5);

        for (int i = 0; i < probabilityConfigs.Count; i++)
        {
            if (probabilityConfigs[i] != null && probabilityConfigs[i].shopLevel == config.shopLevel)
            {
                probabilityConfigs[i] = config;
                probabilityConfigByLevel = null;
                return;
            }
        }

        probabilityConfigs.Add(config);
        probabilityConfigByLevel = null;
    }

    private void OnValidate()
    {
        for (int i = poolConfigs.Count - 1; i >= 0; i--)
        {
            if (poolConfigs[i] == null)
            {
                poolConfigs.RemoveAt(i);
                continue;
            }

            poolConfigs[i].unitRare = Mathf.Max(0, poolConfigs[i].unitRare);
            poolConfigs[i].cardCount = Mathf.Max(0, poolConfigs[i].cardCount);
        }

        for (int i = probabilityConfigs.Count - 1; i >= 0; i--)
        {
            if (probabilityConfigs[i] == null)
            {
                probabilityConfigs.RemoveAt(i);
                continue;
            }

            probabilityConfigs[i].shopLevel = Mathf.Max(0, probabilityConfigs[i].shopLevel);
            probabilityConfigs[i].weightT1 = Mathf.Max(0f, probabilityConfigs[i].weightT1);
            probabilityConfigs[i].weightT2 = Mathf.Max(0f, probabilityConfigs[i].weightT2);
            probabilityConfigs[i].weightT3 = Mathf.Max(0f, probabilityConfigs[i].weightT3);
            probabilityConfigs[i].weightT4 = Mathf.Max(0f, probabilityConfigs[i].weightT4);
            probabilityConfigs[i].weightT5 = Mathf.Max(0f, probabilityConfigs[i].weightT5);
        }

        poolConfigByRare = null;
        probabilityConfigByLevel = null;
    }

    private void EnsurePoolLookup()
    {
        if (poolConfigByRare != null)
        {
            return;
        }

        poolConfigByRare = new Dictionary<int, ShopPoolConfig>();
        for (int i = 0; i < poolConfigs.Count; i++)
        {
            ShopPoolConfig config = poolConfigs[i];
            if (config != null)
            {
                poolConfigByRare[config.unitRare] = config;
            }
        }
    }

    private void EnsureProbabilityLookup()
    {
        if (probabilityConfigByLevel != null)
        {
            return;
        }

        probabilityConfigByLevel = new Dictionary<int, ShopProbabilityConfig>();
        for (int i = 0; i < probabilityConfigs.Count; i++)
        {
            ShopProbabilityConfig config = probabilityConfigs[i];
            if (config != null)
            {
                probabilityConfigByLevel[config.shopLevel] = config;
            }
        }
    }
}
