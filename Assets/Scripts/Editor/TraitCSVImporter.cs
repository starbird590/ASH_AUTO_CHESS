using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TraitCSVImporter
{
    public const string TraitCsvPath = "Assets/Data/Trait.csv";
    public const string TraitTierCsvPath = "Assets/Data/TraitTier.csv";
    public const string OutputFolder = "Assets/TraitSO";
    public const string TraitIconFolder = "Assets/Resources/Traits/Icons";

    private static readonly string[] TraitFields =
    {
        "UnionId",
        "UnionName",
        "UnionDes",
        "UnionIcon"
    };

    private static readonly string[] TraitTierFields =
    {
        "UnionChildId",
        "UnionId",
        "UnionCounts",
        "LevelDes",
        "SkillID"
    };

    private static bool isSilentTraitImportRunning;

    public static bool IsSilentTraitImportRunning
    {
        get { return isSilentTraitImportRunning; }
    }

    [MenuItem("ASH Auto Chess/Import Traits From CSV")]
    public static void ImportTraitsFromMenu()
    {
        ExecuteSilentTraitImport();
    }

    public static bool ExecuteSilentTraitImport()
    {
        if (isSilentTraitImportRunning)
        {
            Debug.LogWarning("[TraitCSVImporter] Trait CSV import is already running. Skipped duplicate request.");
            return false;
        }

        isSilentTraitImportRunning = true;
        try
        {
            TextAsset traitCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(TraitCsvPath);
            if (traitCsv == null)
            {
                Debug.LogWarning("[TraitCSVImporter] Cannot find Trait.csv at " + TraitCsvPath + ".");
                return false;
            }

            List<TraitRecord> traits = ParseTraitCsv(StageTableParser.ReadTextAsset(traitCsv));
            if (traits.Count == 0)
            {
                Debug.LogWarning("[TraitCSVImporter] Trait.csv has no valid trait rows.");
                return false;
            }

            List<TraitTierRecord> tiers = new List<TraitTierRecord>();
            TextAsset traitTierCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(TraitTierCsvPath);
            if (traitTierCsv != null)
            {
                tiers = ParseTraitTierCsv(StageTableParser.ReadTextAsset(traitTierCsv));
            }
            else
            {
                Debug.LogWarning("[TraitCSVImporter] Cannot find TraitTier.csv at " + TraitTierCsvPath + ". Imported traits will have no table-driven tiers.");
            }

            if (!ValidateUniqueTierCounts(tiers))
            {
                return false;
            }

            EnsureAssetFolder(OutputFolder);

            Dictionary<string, TraitRecord> traitById = BuildTraitRecordIndex(traits);
            Dictionary<string, List<TraitTierRecord>> tiersByUnionId = GroupTiersByUnionId(tiers, traitById);
            HashSet<string> knownSkillIds = LoadKnownSkillIds();
            WarnMissingSkillIds(tiers, knownSkillIds);

            int importedCount = 0;
            for (int i = 0; i < traits.Count; i++)
            {
                TraitRecord record = traits[i];
                TraitSO trait = LoadOrCreateTraitAsset(record);
                ApplyTraitValues(trait, record, GetTierList(tiersByUnionId, record.UnionId));
                EditorUtility.SetDirty(trait);
                importedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=#00FF66>[TraitCSVImporter]</color> Imported " + importedCount + " TraitSO assets into " + OutputFolder + ".");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
        finally
        {
            isSilentTraitImportRunning = false;
        }
    }

    private static List<TraitRecord> ParseTraitCsv(string csvText)
    {
        List<TraitRecord> result = new List<TraitRecord>();
        List<List<string>> rows = StageTableParser.ParseCsvRows(csvText);
        if (rows.Count == 0)
        {
            return result;
        }

        int headerRowIndex = FindHeaderRow(rows, "UnionId");
        Dictionary<string, int> columns = BuildColumnIndex(rows, headerRowIndex, TraitFields);
        int dataStartRow = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;

        for (int rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            if (IsRowEmpty(row))
            {
                continue;
            }

            string unionId = GetString(row, columns, "UnionId");
            if (string.IsNullOrWhiteSpace(unionId) || IsInstructionCell(unionId))
            {
                continue;
            }

            result.Add(new TraitRecord
            {
                UnionId = unionId,
                UnionName = GetString(row, columns, "UnionName"),
                UnionDes = GetString(row, columns, "UnionDes"),
                UnionIcon = GetString(row, columns, "UnionIcon")
            });
        }

        return result;
    }

    private static List<TraitTierRecord> ParseTraitTierCsv(string csvText)
    {
        List<TraitTierRecord> result = new List<TraitTierRecord>();
        List<List<string>> rows = StageTableParser.ParseCsvRows(csvText);
        if (rows.Count == 0)
        {
            return result;
        }

        int headerRowIndex = FindHeaderRow(rows, "UnionChildId");
        Dictionary<string, int> columns = BuildColumnIndex(rows, headerRowIndex, TraitTierFields);
        int dataStartRow = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;

        for (int rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            if (IsRowEmpty(row))
            {
                continue;
            }

            string unionChildId = GetString(row, columns, "UnionChildId");
            string unionId = GetString(row, columns, "UnionId");
            if (string.IsNullOrWhiteSpace(unionId) || IsInstructionCell(unionId) || IsInstructionCell(unionChildId))
            {
                continue;
            }

            result.Add(new TraitTierRecord
            {
                UnionChildId = unionChildId,
                UnionId = unionId,
                UnionCounts = Mathf.Max(1, GetInt(row, columns, "UnionCounts")),
                LevelDes = GetString(row, columns, "LevelDes"),
                SkillID = NormalizeSkillIdCell(GetString(row, columns, "SkillID"))
            });
        }

        result.Sort((left, right) =>
        {
            int idCompare = string.Compare(left.UnionId, right.UnionId, StringComparison.OrdinalIgnoreCase);
            return idCompare != 0 ? idCompare : left.UnionCounts.CompareTo(right.UnionCounts);
        });
        return result;
    }

    private static Dictionary<string, TraitRecord> BuildTraitRecordIndex(List<TraitRecord> traits)
    {
        Dictionary<string, TraitRecord> result = new Dictionary<string, TraitRecord>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < traits.Count; i++)
        {
            TraitRecord trait = traits[i];
            if (trait == null || string.IsNullOrWhiteSpace(trait.UnionId))
            {
                continue;
            }

            if (result.ContainsKey(trait.UnionId))
            {
                Debug.LogWarning("[TraitCSVImporter] Duplicate UnionId in Trait.csv. Later row overrides earlier row: " + trait.UnionId);
            }

            result[trait.UnionId] = trait;
        }

        return result;
    }

    private static Dictionary<string, List<TraitTierRecord>> GroupTiersByUnionId(
        List<TraitTierRecord> tiers,
        Dictionary<string, TraitRecord> traitById)
    {
        Dictionary<string, List<TraitTierRecord>> result = new Dictionary<string, List<TraitTierRecord>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tiers.Count; i++)
        {
            TraitTierRecord tier = tiers[i];
            if (tier == null || string.IsNullOrWhiteSpace(tier.UnionId))
            {
                continue;
            }

            if (!traitById.ContainsKey(tier.UnionId))
            {
                Debug.LogWarning("[TraitCSVImporter] TraitTier row references missing UnionId: " + tier.UnionId);
                continue;
            }

            if (!result.TryGetValue(tier.UnionId, out List<TraitTierRecord> list))
            {
                list = new List<TraitTierRecord>();
                result[tier.UnionId] = list;
            }

            list.Add(tier);
        }

        return result;
    }

    private static bool ValidateUniqueTierCounts(List<TraitTierRecord> tiers)
    {
        Dictionary<string, HashSet<int>> countsByUnionId = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        bool isValid = true;

        for (int i = 0; i < tiers.Count; i++)
        {
            TraitTierRecord tier = tiers[i];
            if (tier == null || string.IsNullOrWhiteSpace(tier.UnionId))
            {
                continue;
            }

            if (!countsByUnionId.TryGetValue(tier.UnionId, out HashSet<int> counts))
            {
                counts = new HashSet<int>();
                countsByUnionId[tier.UnionId] = counts;
            }

            if (!counts.Add(tier.UnionCounts))
            {
                Debug.LogError("[TraitCSVImporter] Duplicate UnionCounts " + tier.UnionCounts + " for UnionId " + tier.UnionId + ". Fix TraitTier.csv before importing.");
                isValid = false;
            }
        }

        return isValid;
    }

    private static void WarnMissingSkillIds(List<TraitTierRecord> tiers, HashSet<string> knownSkillIds)
    {
        if (knownSkillIds == null || knownSkillIds.Count == 0)
        {
            return;
        }

        HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tiers.Count; i++)
        {
            List<string> skillIds = TraitSO.ParseSkillIdList(tiers[i].SkillID);
            for (int skillIndex = 0; skillIndex < skillIds.Count; skillIndex++)
            {
                string skillId = skillIds[skillIndex];
                if (!knownSkillIds.Contains(skillId) && warned.Add(skillId))
                {
                    Debug.LogWarning("[TraitCSVImporter] TraitTier references missing SkillID: " + skillId);
                }
            }
        }
    }

    private static TraitSO LoadOrCreateTraitAsset(TraitRecord record)
    {
        TraitSO existing = FindExistingTraitAsset(record);
        if (existing != null)
        {
            return existing;
        }

        string assetPath = OutputFolder + "/" + MakeSafeFileName(record.UnionId) + ".asset";
        TraitSO trait = AssetDatabase.LoadAssetAtPath<TraitSO>(assetPath);
        if (trait != null)
        {
            return trait;
        }

        trait = ScriptableObject.CreateInstance<TraitSO>();
        AssetDatabase.CreateAsset(trait, assetPath);
        return trait;
    }

    private static TraitSO FindExistingTraitAsset(TraitRecord record)
    {
        string[] guids = AssetDatabase.FindAssets("t:TraitSO");
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            TraitSO trait = AssetDatabase.LoadAssetAtPath<TraitSO>(assetPath);
            if (trait == null)
            {
                continue;
            }

            if (MatchesTraitRecord(trait.unionId, record.UnionId)
                || MatchesTraitRecord(trait.traitName, record.UnionName)
                || MatchesTraitRecord(trait.name, record.UnionName)
                || MatchesTraitRecord(trait.name, record.UnionId))
            {
                return trait;
            }
        }

        return null;
    }

    private static bool MatchesTraitRecord(string value, string expected)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(expected)
            && string.Equals(value.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyTraitValues(TraitSO trait, TraitRecord record, List<TraitTierRecord> tierRecords)
    {
        trait.unionId = record.UnionId;
        trait.traitName = string.IsNullOrWhiteSpace(record.UnionName) ? record.UnionId : record.UnionName;
        trait.description = record.UnionDes;
        trait.icon = LoadTraitIcon(record.UnionIcon);
        trait.tiers = BuildTierConfigs(tierRecords);
        trait.thresholds = BuildThresholds(trait.tiers);
    }

    private static TraitTierConfig[] BuildTierConfigs(List<TraitTierRecord> tierRecords)
    {
        if (tierRecords == null || tierRecords.Count == 0)
        {
            return new TraitTierConfig[0];
        }

        TraitTierConfig[] tiers = new TraitTierConfig[tierRecords.Count];
        for (int i = 0; i < tierRecords.Count; i++)
        {
            TraitTierRecord record = tierRecords[i];
            tiers[i] = new TraitTierConfig
            {
                unionChildId = record.UnionChildId,
                unionCounts = Mathf.Max(1, record.UnionCounts),
                levelDescription = record.LevelDes,
                skillIds = NormalizeSkillIdCell(record.SkillID)
            };
        }

        Array.Sort(tiers, (left, right) => left.unionCounts.CompareTo(right.unionCounts));
        return tiers;
    }

    private static int[] BuildThresholds(TraitTierConfig[] tiers)
    {
        if (tiers == null || tiers.Length == 0)
        {
            return new int[0];
        }

        int[] thresholds = new int[tiers.Length];
        for (int i = 0; i < tiers.Length; i++)
        {
            thresholds[i] = Mathf.Max(1, tiers[i].unionCounts);
        }

        return thresholds;
    }

    private static List<TraitTierRecord> GetTierList(Dictionary<string, List<TraitTierRecord>> tiersByUnionId, string unionId)
    {
        if (tiersByUnionId != null && tiersByUnionId.TryGetValue(unionId, out List<TraitTierRecord> tiers))
        {
            return tiers;
        }

        return new List<TraitTierRecord>();
    }

    private static Sprite LoadTraitIcon(string iconNameOrPath)
    {
        string cleaned = CleanCell(iconNameOrPath);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        Sprite sprite = LoadSpriteAtPath(cleaned);
        if (sprite != null)
        {
            return sprite;
        }

        string fileName = Path.GetFileNameWithoutExtension(cleaned);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            sprite = LoadSpriteAtPath(TraitIconFolder + "/" + fileName + ".png");
            if (sprite != null)
            {
                return sprite;
            }

            sprite = LoadSpriteAtPath(TraitIconFolder + "/" + fileName + ".jpg");
            if (sprite != null)
            {
                return sprite;
            }

            sprite = Resources.Load<Sprite>("Traits/Icons/" + fileName);
        }

        if (sprite == null)
        {
            Debug.LogWarning("[TraitCSVImporter] Cannot find trait icon: " + iconNameOrPath);
        }

        return sprite;
    }

    private static Sprite LoadSpriteAtPath(string assetPath)
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        if (sprite != null)
        {
            return sprite;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(normalizedPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite subSprite)
            {
                return subSprite;
            }
        }

        TextureImporter textureImporter = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
        if (textureImporter != null && textureImporter.textureType != TextureImporterType.Sprite)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        }

        return null;
    }

    private static HashSet<string> LoadKnownSkillIds()
    {
        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] skillAssetGuids = AssetDatabase.FindAssets("t:SkillDataSO");
        for (int i = 0; i < skillAssetGuids.Length; i++)
        {
            SkillDataSO skill = AssetDatabase.LoadAssetAtPath<SkillDataSO>(AssetDatabase.GUIDToAssetPath(skillAssetGuids[i]));
            if (skill != null && !string.IsNullOrWhiteSpace(skill.SkillID))
            {
                result.Add(skill.SkillID.Trim());
            }
        }

        TextAsset skillCsv = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/SkillTable.csv");
        if (skillCsv == null)
        {
            return result;
        }

        List<List<string>> rows = StageTableParser.ParseCsvRows(StageTableParser.ReadTextAsset(skillCsv));
        int headerRowIndex = FindHeaderRow(rows, "SkillID");
        Dictionary<string, int> columns = BuildColumnIndex(rows, headerRowIndex, new[] { "SkillID" });
        int dataStartRow = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;

        for (int rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            string skillId = GetString(rows[rowIndex], columns, "SkillID");
            if (!string.IsNullOrWhiteSpace(skillId) && !IsInstructionCell(skillId))
            {
                result.Add(skillId);
            }
        }

        return result;
    }

    private static Dictionary<string, int> BuildColumnIndex(List<List<string>> rows, int headerRowIndex, string[] fallbackFields)
    {
        Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (headerRowIndex >= 0 && headerRowIndex < rows.Count)
        {
            List<string> headerRow = rows[headerRowIndex];
            for (int i = 0; i < headerRow.Count; i++)
            {
                string normalizedHeader = NormalizeHeader(headerRow[i]);
                for (int j = 0; j < fallbackFields.Length; j++)
                {
                    string fieldName = fallbackFields[j];
                    if (MatchesHeader(normalizedHeader, fieldName) && !result.ContainsKey(fieldName))
                    {
                        result[fieldName] = i;
                    }
                }
            }
        }

        for (int i = 0; i < fallbackFields.Length; i++)
        {
            if (!result.ContainsKey(fallbackFields[i]))
            {
                result[fallbackFields[i]] = i;
            }
        }

        return result;
    }

    private static bool MatchesHeader(string normalizedHeader, string fieldName)
    {
        string normalizedField = NormalizeHeader(fieldName);
        if (string.Equals(normalizedHeader, normalizedField, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(fieldName, "UnionId", StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedHeader, "unionld", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindHeaderRow(List<List<string>> rows, string requiredField)
    {
        int limit = Mathf.Min(8, rows.Count);
        for (int rowIndex = 0; rowIndex < limit; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            for (int colIndex = 0; colIndex < row.Count; colIndex++)
            {
                if (MatchesHeader(NormalizeHeader(row[colIndex]), requiredField))
                {
                    return rowIndex;
                }
            }
        }

        return -1;
    }

    private static string GetString(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        return CleanCell(GetCell(row, columns, fieldName));
    }

    private static int GetInt(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        string value = GetString(row, columns, fieldName);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
        {
            return intValue;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            return Mathf.RoundToInt(floatValue);
        }

        return 0;
    }

    private static string GetCell(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        if (row == null || columns == null || !columns.TryGetValue(fieldName, out int columnIndex))
        {
            return string.Empty;
        }

        if (columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }

        return row[columnIndex];
    }

    private static string NormalizeSkillIdCell(string value)
    {
        List<string> ids = TraitSO.ParseSkillIdList(value);
        return ids.Count == 0 ? string.Empty : string.Join(";", ids.ToArray());
    }

    private static string NormalizeHeader(string value)
    {
        return CleanCell(value)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static bool IsInstructionCell(string value)
    {
        string normalized = NormalizeHeader(value);
        return normalized == "id"
            || normalized == "unionid"
            || normalized == "unionld"
            || normalized == "unionname"
            || normalized == "uniondes"
            || normalized == "unionicon"
            || normalized == "unionchildid"
            || normalized == "unioncounts"
            || normalized == "leveldes"
            || normalized == "skillid"
            || normalized == "\u7F81\u7ECAid"
            || normalized == "\u7F81\u7ECA\u5B50id"
            || normalized == "\u6280\u80FDid";
    }

    private static bool IsRowEmpty(List<string> row)
    {
        if (row == null)
        {
            return true;
        }

        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(CleanCell(row[i])))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureAssetFolder(string assetFolderPath)
    {
        string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized))
        {
            return;
        }

        string[] parts = normalized.Split('/');
        if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[TraitCSVImporter] Output folder must be inside Assets: " + assetFolderPath);
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

    private static string MakeSafeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "Trait" : value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            safe = safe.Replace(invalidChars[i], '_');
        }

        return safe;
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

    private sealed class TraitRecord
    {
        public string UnionId;
        public string UnionName;
        public string UnionDes;
        public string UnionIcon;
    }

    private sealed class TraitTierRecord
    {
        public string UnionChildId;
        public string UnionId;
        public int UnionCounts;
        public string LevelDes;
        public string SkillID;
    }
}
