#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ShopManagerCSVImporter
{
    private const string PoolCsvName = "ShopPool.csv";
    private const string ProbabilityCsvName = "ShopProbability.csv";
    private const string OutputDirectory = "Assets/Resources/Units/DataAssets";
    private const string OutputAssetPath = OutputDirectory + "/ShopManagerData.asset";

    public static void ExecuteSilentShopImport()
    {
        string poolCsvPath = FindCsvAssetPath(PoolCsvName);
        string probabilityCsvPath = FindCsvAssetPath(ProbabilityCsvName);

        if (string.IsNullOrEmpty(poolCsvPath) && string.IsNullOrEmpty(probabilityCsvPath))
        {
            Debug.LogWarning("[ShopManagerCSVImporter] ShopPool.csv and ShopProbability.csv were not found.");
            return;
        }

        ShopManagerDataSO dataAsset = AssetDatabase.LoadAssetAtPath<ShopManagerDataSO>(OutputAssetPath);
        if (dataAsset == null)
        {
            EnsureOutputDirectory();
            dataAsset = ScriptableObject.CreateInstance<ShopManagerDataSO>();
            AssetDatabase.CreateAsset(dataAsset, OutputAssetPath);
        }

        dataAsset.ClearPoolConfigs();
        dataAsset.ClearProbabilityConfigs();

        if (!string.IsNullOrEmpty(poolCsvPath))
        {
            ImportPoolCsv(poolCsvPath, dataAsset);
        }
        else
        {
            Debug.LogWarning("[ShopManagerCSVImporter] Missing ShopPool.csv. Pool config list was cleared.");
        }

        if (!string.IsNullOrEmpty(probabilityCsvPath))
        {
            ImportProbabilityCsv(probabilityCsvPath, dataAsset);
        }
        else
        {
            Debug.LogWarning("[ShopManagerCSVImporter] Missing ShopProbability.csv. Probability config list was cleared.");
        }

        EditorUtility.SetDirty(dataAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ShopManagerCSVImporter] Imported ShopManagerData.asset from split shop CSV files.");
    }

    public static void Import(string csvAssetPath)
    {
        ExecuteSilentShopImport();
    }

    private static void ImportPoolCsv(string csvAssetPath, ShopManagerDataSO dataAsset)
    {
        List<Dictionary<string, string>> rows = ReadCsvRows(csvAssetPath);
        for (int i = 0; i < rows.Count; i++)
        {
            int unitRare;
            int cardCount;
            if (!TryReadInt(rows[i], "UnitRare", out unitRare) || !TryReadInt(rows[i], "CardCount", out cardCount))
            {
                Debug.LogWarning("[ShopManagerCSVImporter] Skip ShopPool row " + (i + 2) + ", missing UnitRare or CardCount.");
                continue;
            }

            dataAsset.SetPoolConfig(new ShopPoolConfig
            {
                unitRare = unitRare,
                cardCount = cardCount
            });
        }
    }

    private static void ImportProbabilityCsv(string csvAssetPath, ShopManagerDataSO dataAsset)
    {
        List<Dictionary<string, string>> rows = ReadCsvRows(csvAssetPath);
        for (int i = 0; i < rows.Count; i++)
        {
            ShopProbabilityConfig config;
            if (!TryReadProbabilityConfig(rows[i], out config))
            {
                Debug.LogWarning("[ShopManagerCSVImporter] Skip ShopProbability row " + (i + 2) + ", missing required probability columns.");
                continue;
            }

            dataAsset.SetProbabilityConfig(config);
        }
    }

    private static bool TryReadProbabilityConfig(Dictionary<string, string> row, out ShopProbabilityConfig config)
    {
        config = null;

        int shopLevel;
        float weightT1;
        float weightT2;
        float weightT3;
        float weightT4;
        float weightT5;

        if (!TryReadInt(row, "ShopLevel", out shopLevel)
            || !TryReadFloat(row, "WeightT1", out weightT1)
            || !TryReadFloat(row, "WeightT2", out weightT2)
            || !TryReadFloat(row, "WeightT3", out weightT3)
            || !TryReadFloat(row, "WeightT4", out weightT4)
            || !TryReadFloat(row, "WeightT5", out weightT5))
        {
            return false;
        }

        config = new ShopProbabilityConfig
        {
            shopLevel = shopLevel,
            weightT1 = weightT1,
            weightT2 = weightT2,
            weightT3 = weightT3,
            weightT4 = weightT4,
            weightT5 = weightT5
        };

        return true;
    }

    private static string FindCsvAssetPath(string csvName)
    {
        string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(csvName));
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.Equals(Path.GetFileName(assetPath), csvName, StringComparison.OrdinalIgnoreCase))
            {
                return assetPath;
            }
        }

        string defaultPath = "Assets/Data/" + csvName;
        return File.Exists(defaultPath) ? defaultPath : string.Empty;
    }

    private static void EnsureOutputDirectory()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Units"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Units");
        }

        if (!AssetDatabase.IsValidFolder(OutputDirectory))
        {
            AssetDatabase.CreateFolder("Assets/Resources/Units", "DataAssets");
        }
    }

    private static List<Dictionary<string, string>> ReadCsvRows(string path)
    {
        List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return rows;
        }

        List<string> headers = ParseCsvLine(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            List<string> values = ParseCsvLine(lines[i]);
            Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                string value = columnIndex < values.Count ? values[columnIndex] : string.Empty;
                row[headers[columnIndex]] = value;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        List<string> values = new List<string>();
        if (line == null)
        {
            values.Add(string.Empty);
            return values;
        }

        bool inQuotes = false;
        string current = string.Empty;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current += '"';
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.Trim());
                current = string.Empty;
            }
            else
            {
                current += c;
            }
        }

        values.Add(current.Trim());
        return values;
    }

    private static bool TryReadInt(Dictionary<string, string> row, string columnName, out int value)
    {
        value = 0;
        string text;
        return row.TryGetValue(columnName, out text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadFloat(Dictionary<string, string> row, string columnName, out float value)
    {
        value = 0f;
        string text;
        return row.TryGetValue(columnName, out text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
#endif
