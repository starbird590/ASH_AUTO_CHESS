using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class UnitDataManager : MonoBehaviour
{
    public static UnitDataManager Instance { get; private set; }

    [Header("Standard Unit Factory")]
    public GameObject baseUnitPrefab;

    private readonly Dictionary<string, UnitLogicDataSO> unitDataDict = new Dictionary<string, UnitLogicDataSO>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TraitSO> traitDict = new Dictionary<string, TraitSO>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, UnitLogicDataSO> UnitDataDict => unitDataDict;
    public IReadOnlyDictionary<string, TraitSO> TraitDict => traitDict;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InitializeData();
    }

    public void InitializeData()
    {
        unitDataDict.Clear();
        traitDict.Clear();

        LoadUnitDataAssets();
        LoadTraitAssets();

        Debug.Log("[UnitDataManager] Loaded unit data: " + unitDataDict.Count + ", traits: " + traitDict.Count + ".");
    }

    public UnitLogic SpawnUnitOnBoard(string chessId, Vector3 spawnPosition, UnitFaction faction)
    {
        if (string.IsNullOrWhiteSpace(chessId))
        {
            Debug.LogError("[UnitDataManager] Spawn failed because ChessId is empty.");
            return null;
        }

        if (unitDataDict.Count == 0)
        {
            InitializeData();
        }

        if (!unitDataDict.TryGetValue(chessId.Trim(), out UnitLogicDataSO unitData) || unitData == null)
        {
            Debug.LogError("[UnitDataManager] Cannot find UnitLogicDataSO for ChessId: " + chessId);
            return null;
        }

        if (baseUnitPrefab == null)
        {
            Debug.LogError("[UnitDataManager] baseUnitPrefab is not assigned. Cannot spawn unit: " + chessId);
            return null;
        }

        GameObject unitObject = Instantiate(baseUnitPrefab, spawnPosition, Quaternion.identity);
        if (!string.IsNullOrWhiteSpace(unitData.chessName))
        {
            unitObject.name = unitData.chessName;
        }
        UnitLogic unitLogic = unitObject.GetComponent<UnitLogic>();
        if (unitLogic == null)
        {
            Debug.LogError("[UnitDataManager] baseUnitPrefab is missing UnitLogic. Spawn aborted: " + chessId);
            Destroy(unitObject);
            return null;
        }

        AttachModel(unitLogic, unitData);
        List<TraitSO> resolvedTraits = ResolveTraits(unitData.unionId);
        unitLogic.InitializeFromConfig(unitData, faction, resolvedTraits);
        return unitLogic;
    }

    public bool TryGetUnitData(string chessId, out UnitLogicDataSO unitData)
    {
        if (unitDataDict.Count == 0)
        {
            InitializeData();
        }

        return unitDataDict.TryGetValue(chessId, out unitData);
    }

    private void LoadUnitDataAssets()
    {
        UnitLogicDataSO[] unitAssets = Resources.LoadAll<UnitLogicDataSO>("Units/DataAssets");
        for (int i = 0; i < unitAssets.Length; i++)
        {
            RegisterUnitData(unitAssets[i]);
        }
    }

    private void LoadTraitAssets()
    {
        LoadTraitResourcesFolder("Synergies");
        LoadTraitResourcesFolder("TraitSO");
        LoadTraitResourcesFolder("Traits");

#if UNITY_EDITOR
        string[] traitGuids = AssetDatabase.FindAssets("t:TraitSO");
        for (int i = 0; i < traitGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(traitGuids[i]);
            TraitSO trait = AssetDatabase.LoadAssetAtPath<TraitSO>(assetPath);
            RegisterTrait(trait);
        }
#endif
    }

    private void LoadTraitResourcesFolder(string resourcesPath)
    {
        TraitSO[] traits = Resources.LoadAll<TraitSO>(resourcesPath);
        for (int i = 0; i < traits.Length; i++)
        {
            RegisterTrait(traits[i]);
        }
    }

    private void RegisterUnitData(UnitLogicDataSO unitData)
    {
        if (unitData == null || string.IsNullOrWhiteSpace(unitData.chessId))
        {
            return;
        }

        string key = unitData.chessId.Trim();
        unitDataDict[key] = unitData;
    }

    private void RegisterTrait(TraitSO trait)
    {
        if (trait == null)
        {
            return;
        }

        AddTraitKey(trait.name, trait);
        AddTraitKey(ReadTraitStringMember(trait, "traitId"), trait);
        AddTraitKey(ReadTraitStringMember(trait, "id"), trait);
        AddTraitKey(ReadTraitStringMember(trait, "traitName"), trait);
        AddTraitKey(ReadTraitStringMember(trait, "displayName"), trait);
    }

    private void AddTraitKey(string key, TraitSO trait)
    {
        if (string.IsNullOrWhiteSpace(key) || trait == null)
        {
            return;
        }

        traitDict[key.Trim()] = trait;
    }

    private string ReadTraitStringMember(TraitSO trait, string memberName)
    {
        Type traitType = trait.GetType();
        System.Reflection.FieldInfo field = traitType.GetField(memberName);
        if (field != null && field.FieldType == typeof(string))
        {
            return field.GetValue(trait) as string;
        }

        System.Reflection.PropertyInfo property = traitType.GetProperty(memberName);
        if (property != null && property.PropertyType == typeof(string) && property.CanRead)
        {
            return property.GetValue(trait, null) as string;
        }

        return string.Empty;
    }

    private void AttachModel(UnitLogic unitLogic, UnitLogicDataSO unitData)
    {
        if (unitLogic == null || unitData == null || unitData.unitPrefab == null)
        {
            return;
        }

        Transform parent = unitLogic.modelContainer != null ? unitLogic.modelContainer : unitLogic.transform;
        ClearExistingModelChildren(parent);

        GameObject modelObject = Instantiate(unitData.unitPrefab, parent);
        modelObject.transform.localPosition = Vector3.zero;
        modelObject.transform.localRotation = Quaternion.identity;
        modelObject.transform.localScale = Vector3.one;
    }

    private void ClearExistingModelChildren(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private List<TraitSO> ResolveTraits(string unionId)
    {
        List<TraitSO> resolvedTraits = new List<TraitSO>();
        if (string.IsNullOrWhiteSpace(unionId))
        {
            return resolvedTraits;
        }

        string[] ids = unionId.Split(new[] { ',', '，', ';', '；', '|', '/', '、' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[i].Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (traitDict.TryGetValue(id, out TraitSO trait) && trait != null)
            {
                if (!resolvedTraits.Contains(trait))
                {
                    resolvedTraits.Add(trait);
                }
            }
            else
            {
                Debug.LogWarning("[UnitDataManager] Cannot resolve TraitSO for unionId segment: " + id);
            }
        }

        return resolvedTraits;
    }
}
