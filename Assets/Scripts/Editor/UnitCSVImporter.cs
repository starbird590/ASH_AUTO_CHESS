using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class UnitCSVImporter
{
    public const string PlayerCsvFileName = "PlayerUnits.csv";
    public const string EnemyCsvFileName = "EnemyUnits.csv";
    public const string SilentOutputFolder = "Assets/Resources/Units/DataAssets";

    private static readonly string[] FieldOrder =
    {
        "chessId",
        "chessName",
        "spriteName",
        "unionId",
        "faction",
        "playerDirective",
        "unitCost",
        "unitPrice",
        "unitRare",
        "unitTier",
        "unitType",
        "attackType",
        "baseHp",
        "baseArmor",
        "bayonetArmor",
        "critRate",
        "critDamage",
        "fireDamage",
        "fireRate",
        "fireSpeed",
        "fireRange",
        "ammo",
        "ammoSpeed",
        "firePenPct",
        "firePenFlat",
        "damageAoe",
        "bayonetId",
        "bayonetDamage",
        "bayonetCost",
        "bayonetSpeed",
        "bayonetRange",
        "bayonetPenPct",
        "bayonetPenFlat",
        "moveSpeed",
        "captureSpeed",
        "threatValue"
    };

    private static bool isSilentBatchImportRunning;

    public static bool IsSilentBatchImportRunning => isSilentBatchImportRunning;

    public static bool ExecuteSilentBatchImport(string sourceFolderPath)
    {
        if (isSilentBatchImportRunning)
        {
            Debug.LogWarning("[UnitCSVImporter] 静默导入正在执行，本次重复触发已跳过。");
            return false;
        }

        isSilentBatchImportRunning = true;
        try
        {
            string normalizedSourceFolder = NormalizePath(sourceFolderPath);
            if (string.IsNullOrWhiteSpace(normalizedSourceFolder))
            {
                Debug.LogWarning("[UnitCSVImporter] 源表母文件夹路径为空，静默导入已跳过。");
                return false;
            }

            EnsureAssetFolder(SilentOutputFolder);

            string playerCsvPath = CombinePath(normalizedSourceFolder, PlayerCsvFileName);
            string enemyCsvPath = CombinePath(normalizedSourceFolder, EnemyCsvFileName);

            int importedCount = 0;
            bool importedAny = false;

            importedAny |= TryImportSingleCsvIsolated(playerCsvPath, "玩家表", out int playerCount);
            importedCount += playerCount;

            importedAny |= TryImportSingleCsvIsolated(enemyCsvPath, "敌方表", out int enemyCount);
            importedCount += enemyCount;

            if (!importedAny)
            {
                Debug.LogWarning("[UnitCSVImporter] 未在目录中找到 PlayerUnits.csv 或 EnemyUnits.csv：" + normalizedSourceFolder);
                return false;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("<color=#00FF66>[UnitCSVImporter]</color> 静默导入完成，共创建或覆盖 " + importedCount + " 个 UnitLogicDataSO。");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
        finally
        {
            isSilentBatchImportRunning = false;
        }
    }

    private static bool TryImportSingleCsvIsolated(string csvPath, string tableLabel, out int importedCount)
    {
        importedCount = 0;
        string absoluteCsvPath = ToAbsolutePath(csvPath);
        if (!File.Exists(absoluteCsvPath))
        {
            Debug.LogError("[热重载警报] 未找到" + tableLabel + "，请检查文件名！试图寻找的路径为: " + absoluteCsvPath);
            return false;
        }

        Debug.Log("[UnitCSVImporter] 正在读取" + tableLabel + "：" + absoluteCsvPath);
        try
        {
            importedCount = ImportOneCsv(absoluteCsvPath, SilentOutputFolder);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[热重载警报] " + tableLabel + "解析失败，但不会阻断另一张表。路径: " + absoluteCsvPath + "\n" + ex);
            importedCount = 0;
            return false;
        }
    }

    private static int ImportOneCsv(string absoluteCsvPath, string outputAssetFolder)
    {
        string csvText;
        using (FileStream fileStream = new FileStream(absoluteCsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using (StreamReader streamReader = new StreamReader(fileStream, Encoding.Default))
            {
                csvText = streamReader.ReadToEnd();
            }
        }
        List<List<string>> rows = ParseCsv(csvText);
        if (rows.Count == 0)
        {
            Debug.LogWarning("[UnitCSVImporter] CSV 为空：" + absoluteCsvPath);
            return 0;
        }

        int headerRowIndex = FindHeaderRow(rows);
        Dictionary<string, int> columnIndexByField = BuildColumnIndex(rows, headerRowIndex);
        int dataStartRow = Mathf.Max(3, headerRowIndex >= 0 ? headerRowIndex + 1 : 3);
        int importedCount = 0;

        for (int rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            if (row == null || row.Count == 0 || IsRowEmpty(row))
            {
                continue;
            }

            string chessId = GetCell(row, columnIndexByField, "chessId");
            if (string.IsNullOrWhiteSpace(chessId))
            {
                continue;
            }

            UnitLogicDataSO unitData = LoadOrCreateUnitData(chessId.Trim(), outputAssetFolder);
            FillUnitData(unitData, row, columnIndexByField);
            AssignUnitSprite(unitData, row, columnIndexByField);
            EditorUtility.SetDirty(unitData);
            AutoWireMatchingPrefabs(unitData);
            importedCount++;
        }

        Debug.Log("[UnitCSVImporter] 已导入 " + importedCount + " 行：" + absoluteCsvPath);
        return importedCount;
    }

    private static Dictionary<string, int> BuildColumnIndex(List<List<string>> rows, int headerRowIndex)
    {
        Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (headerRowIndex >= 0 && headerRowIndex < rows.Count)
        {
            List<string> headerRow = rows[headerRowIndex];
            for (int i = 0; i < headerRow.Count; i++)
            {
                string normalizedHeader = NormalizeHeader(headerRow[i]);
                string fieldName = ResolveFieldName(normalizedHeader);
                if (!string.IsNullOrEmpty(fieldName) && !result.ContainsKey(fieldName))
                {
                    result[fieldName] = i;
                }
            }
        }

        for (int i = 0; i < FieldOrder.Length; i++)
        {
            if (!result.ContainsKey(FieldOrder[i]))
            {
                result[FieldOrder[i]] = i;
            }
        }

        return result;
    }

    private static int FindHeaderRow(List<List<string>> rows)
    {
        int limit = Mathf.Min(4, rows.Count);
        for (int rowIndex = 0; rowIndex < limit; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            for (int colIndex = 0; colIndex < row.Count; colIndex++)
            {
                string normalized = NormalizeHeader(row[colIndex]);
                if (string.Equals(normalized, "chessid", StringComparison.OrdinalIgnoreCase))
                {
                    return rowIndex;
                }
            }
        }

        return -1;
    }

    private static void FillUnitData(UnitLogicDataSO data, List<string> row, Dictionary<string, int> columns)
    {
        data.chessId = GetString(row, columns, "chessId");
        data.chessName = GetString(row, columns, "chessName");
        data.unionId = GetString(row, columns, "unionId");
        data.faction = GetInt(row, columns, "faction");
        data.playerDirective = GetInt(row, columns, "playerDirective");
        data.unitCost = GetInt(row, columns, "unitCost");
        data.unitPrice = GetInt(row, columns, "unitPrice");
        data.unitRare = GetInt(row, columns, "unitRare");
        data.unitTier = GetInt(row, columns, "unitTier");
        data.unitType = GetInt(row, columns, "unitType");
        data.attackType = GetInt(row, columns, "attackType");
        data.baseHp = GetInt(row, columns, "baseHp");
        data.baseArmor = GetInt(row, columns, "baseArmor");
        data.bayonetArmor = GetInt(row, columns, "bayonetArmor");
        data.critRate = GetFloat(row, columns, "critRate");
        data.critDamage = GetFloat(row, columns, "critDamage");
        data.fireDamage = GetInt(row, columns, "fireDamage");
        data.fireRate = GetFloat(row, columns, "fireRate");
        data.fireSpeed = GetFloat(row, columns, "fireSpeed");
        data.fireRange = GetInt(row, columns, "fireRange");
        data.ammo = GetInt(row, columns, "ammo");
        data.ammoSpeed = Mathf.Max(1, GetInt(row, columns, "ammoSpeed"));
        data.firePenPct = GetFloat(row, columns, "firePenPct");
        data.firePenFlat = GetFloat(row, columns, "firePenFlat");
        data.damageAoe = GetInt(row, columns, "damageAoe");
        data.bayonetId = GetString(row, columns, "bayonetId");
        data.bayonetDamage = GetInt(row, columns, "bayonetDamage");
        data.bayonetCost = GetString(row, columns, "bayonetCost");
        data.bayonetSpeed = GetFloat(row, columns, "bayonetSpeed");
        data.bayonetRange = GetInt(row, columns, "bayonetRange");
        data.bayonetPenPct = GetFloat(row, columns, "bayonetPenPct");
        data.bayonetPenFlat = GetFloat(row, columns, "bayonetPenFlat");
        data.moveSpeed = GetFloat(row, columns, "moveSpeed");
        data.captureSpeed = GetFloat(row, columns, "captureSpeed");
        data.threatValue = GetInt(row, columns, "threatValue");
    }

    private static void AssignUnitSprite(UnitLogicDataSO data, List<string> row, Dictionary<string, int> columns)
    {
        if (data == null)
        {
            return;
        }

        string spriteName = GetString(row, columns, "spriteName");
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            spriteName = data.chessId;
        }

        if (string.IsNullOrWhiteSpace(spriteName))
        {
            data.unitSprite = null;
            return;
        }

        string resourcePath = "Units/Icons/" + spriteName.Trim();
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        data.unitSprite = sprite;

        if (sprite == null)
        {
            Debug.LogWarning("[UnitCSVImporter] 未找到单位贴图 Resources/" + resourcePath + "，ChessId=" + data.chessId);
        }
    }

    private static UnitLogicDataSO LoadOrCreateUnitData(string chessId, string outputAssetFolder)
    {
        string safeFileName = MakeSafeFileName(chessId);
        string assetPath = NormalizeAssetPath(outputAssetFolder).TrimEnd('/') + "/" + safeFileName + ".asset";
        UnitLogicDataSO data = AssetDatabase.LoadAssetAtPath<UnitLogicDataSO>(assetPath);
        if (data != null)
        {
            return data;
        }

        data = ScriptableObject.CreateInstance<UnitLogicDataSO>();
        AssetDatabase.CreateAsset(data, assetPath);
        return data;
    }

    private static void AutoWireMatchingPrefabs(UnitLogicDataSO unitData)
    {
        if (unitData == null)
        {
            return;
        }

        HashSet<string> keys = BuildUnitMatchKeys(unitData);
        if (keys.Count == 0)
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                continue;
            }

            string prefabKey = NormalizeMatchKey(prefab.name);
            if (!keys.Contains(prefabKey))
            {
                continue;
            }

            UnitLogic unitLogic = prefab.GetComponent<UnitLogic>();
            if (unitLogic != null)
            {
                if (unitLogic.unitDataConfig != unitData)
                {
                    unitLogic.unitDataConfig = unitData;
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                    Debug.Log("[UnitCSVImporter] 已兼容连线旧单位 Prefab 数据：" + prefabPath + " -> " + unitData.chessId);
                }

                continue;
            }

            if (unitData.unitPrefab != prefab)
            {
                unitData.unitPrefab = prefab;
                EditorUtility.SetDirty(unitData);
                Debug.Log("[UnitCSVImporter] 已自动连线美术 Prefab：" + unitData.chessId + " -> " + prefabPath);
            }
        }
    }

    private static HashSet<string> BuildUnitMatchKeys(UnitLogicDataSO unitData)
    {
        HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddMatchKey(keys, unitData.chessId);
        AddMatchKey(keys, unitData.chessName);
        AddMatchKey(keys, StripStarSymbols(unitData.chessName));
        AddMatchKey(keys, StripTrailingTierDigit(unitData.chessId));
        return keys;
    }

    private static void AddMatchKey(HashSet<string> keys, string value)
    {
        string key = NormalizeMatchKey(value);
        if (!string.IsNullOrEmpty(key))
        {
            keys.Add(key);
        }
    }

    private static string NormalizeMatchKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return StripStarSymbols(value)
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace("（", string.Empty)
            .Replace("）", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static void EnsureAssetFolder(string assetFolderPath)
    {
        string normalized = NormalizeAssetPath(assetFolderPath).TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized))
        {
            return;
        }

        string[] parts = normalized.Split('/');
        if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[UnitCSVImporter] 只能在 Assets 内创建导出目录：" + assetFolderPath);
            return;
        }

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static string GetString(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        return CleanCell(GetCell(row, columns, fieldName));
    }

    private static int GetInt(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        string value = GetString(row, columns, fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
        {
            return intValue;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            return Mathf.RoundToInt(floatValue);
        }

        Debug.LogWarning("[UnitCSVImporter] 整数字段解析失败：" + fieldName + " = " + value);
        return 0;
    }

    private static float GetFloat(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        string value = GetString(row, columns, fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0f;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            return floatValue;
        }

        Debug.LogWarning("[UnitCSVImporter] 浮点字段解析失败：" + fieldName + " = " + value);
        return 0f;
    }

    private static string GetCell(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        if (!columns.TryGetValue(fieldName, out int columnIndex))
        {
            return string.Empty;
        }

        if (columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }

        return row[columnIndex];
    }

    private static string ResolveFieldName(string normalizedHeader)
    {
        for (int i = 0; i < FieldOrder.Length; i++)
        {
            if (string.Equals(normalizedHeader, NormalizeHeader(FieldOrder[i]), StringComparison.OrdinalIgnoreCase))
            {
                return FieldOrder[i];
            }
        }

        return string.Empty;
    }

    private static string NormalizeHeader(string value)
    {
        return CleanCell(value)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static string CleanCell(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value
            .Replace("\uFEFF", string.Empty)
            .Replace("\u00A0", string.Empty)
            .Replace("\\xa0", string.Empty)
            .Trim();
    }

    private static bool IsRowEmpty(List<string> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(CleanCell(row[i])))
            {
                return false;
            }
        }

        return true;
    }

    private static List<List<string>> ParseCsv(string csvText)
    {
        List<List<string>> rows = new List<List<string>>();
        List<string> currentRow = new List<string>();
        StringBuilder currentCell = new StringBuilder();
        bool insideQuotes = false;

        for (int i = 0; i < csvText.Length; i++)
        {
            char c = csvText[i];
            if (c == '"')
            {
                if (insideQuotes && i + 1 < csvText.Length && csvText[i + 1] == '"')
                {
                    currentCell.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (c == ',' && !insideQuotes)
            {
                currentRow.Add(currentCell.ToString());
                currentCell.Length = 0;
                continue;
            }

            if ((c == '\n' || c == '\r') && !insideQuotes)
            {
                if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                {
                    i++;
                }

                currentRow.Add(currentCell.ToString());
                currentCell.Length = 0;
                rows.Add(currentRow);
                currentRow = new List<string>();
                continue;
            }

            currentCell.Append(c);
        }

        currentRow.Add(currentCell.ToString());
        if (currentRow.Count > 1 || !string.IsNullOrWhiteSpace(currentRow[0]))
        {
            rows.Add(currentRow);
        }

        return rows;
    }

    private static string CombinePath(string folderPath, string fileName)
    {
        string normalizedFolder = NormalizePath(folderPath).TrimEnd('/');
        return string.IsNullOrEmpty(normalizedFolder) ? fileName : normalizedFolder + "/" + fileName;
    }

    private static string ToAbsolutePath(string path)
    {
        string normalizedPath = NormalizePath(path);
        if (Path.IsPathRooted(normalizedPath))
        {
            return NormalizePhysicalPath(normalizedPath);
        }

        return NormalizePhysicalPath(Path.Combine(GetProjectRootPath(), normalizedPath));
    }

    private static string GetProjectRootPath()
    {
        DirectoryInfo dataDirectory = Directory.GetParent(Application.dataPath);
        return dataDirectory != null ? dataDirectory.FullName : Application.dataPath;
    }

    private static string NormalizePhysicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }

    private static string MakeSafeFileName(string value)
    {
        string safe = CleanCell(value);
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            safe = safe.Replace(invalidChars[i], '_');
        }

        return safe;
    }

    private static string StripStarSymbols(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("★", string.Empty).Replace("*", string.Empty).Trim();
    }

    private static string StripTrailingTierDigit(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length == 1)
        {
            return value;
        }

        char last = value[value.Length - 1];
        return char.IsDigit(last) ? value.Substring(0, value.Length - 1) : value;
    }
}
