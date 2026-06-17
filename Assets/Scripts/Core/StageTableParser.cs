using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public static class StageTableParser
{
    private static readonly string[] MapNodeFields =
    {
        "NodeId",
        "LayerIndex",
        "BattleWaveIds",
        "NextNodeId",
        "BaseReward",
        "VictoryBonus",
        "DefeatBonus"
    };

    private static readonly string[] WaveNodeFields =
    {
        "WaveID",
        "InitialEnemyConfigs",
        "HaveBoss",
        "BossInfo",
        "BossSpawn"
    };

    public static List<MapNodeData> ParseMapNodeCsv(string csvText)
    {
        List<MapNodeData> result = new List<MapNodeData>();
        List<List<string>> rows = ParseCsvRows(csvText);
        if (rows.Count == 0)
        {
            return result;
        }

        int headerRowIndex = FindHeaderRow(rows, "NodeId");
        Dictionary<string, int> columns = BuildColumnIndex(rows, headerRowIndex, MapNodeFields);
        int dataStartRow = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;

        for (int rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            if (IsRowEmpty(row))
            {
                continue;
            }

            string nodeId = GetString(row, columns, "NodeId");
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                Debug.LogWarning("[StageTableParser] MapNode row " + (rowIndex + 1) + " skipped because NodeId is empty.");
                continue;
            }

            MapNodeData node = new MapNodeData
            {
                NodeId = nodeId,
                LayerIndex = Mathf.Max(0, GetInt(row, columns, "LayerIndex")),
                BattleWaveIds = ParseStringList(GetString(row, columns, "BattleWaveIds")),
                NextNodeIds = ParseStringList(GetString(row, columns, "NextNodeId")),
                BaseReward = Mathf.Max(0, GetInt(row, columns, "BaseReward")),
                VictoryBonus = Mathf.Max(0, GetInt(row, columns, "VictoryBonus")),
                DefeatBonus = Mathf.Max(0, GetInt(row, columns, "DefeatBonus"))
            };

            result.Add(node);
        }

        return result;
    }

    public static List<WaveNodeData> ParseWaveNodeCsv(string csvText)
    {
        List<WaveNodeData> result = new List<WaveNodeData>();
        List<List<string>> rows = ParseCsvRows(csvText);
        if (rows.Count == 0)
        {
            return result;
        }

        int headerRowIndex = FindHeaderRow(rows, "WaveID");
        Dictionary<string, int> columns = BuildColumnIndex(rows, headerRowIndex, WaveNodeFields);
        int dataStartRow = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;

        for (int rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            if (IsRowEmpty(row))
            {
                continue;
            }

            string waveId = GetString(row, columns, "WaveID");
            if (string.IsNullOrWhiteSpace(waveId))
            {
                Debug.LogWarning("[StageTableParser] WaveNode row " + (rowIndex + 1) + " skipped because WaveID is empty.");
                continue;
            }

            WaveNodeData wave = new WaveNodeData
            {
                WaveId = waveId,
                InitialEnemyConfigs = ParseInitialEnemyConfigs(GetString(row, columns, "InitialEnemyConfigs")),
                HasBoss = ParseHaveBoss(GetString(row, columns, "HaveBoss")),
                BossInfo = ParseBossInfo(GetString(row, columns, "BossInfo")),
                BossSpawn = ParseBossSpawn(GetString(row, columns, "BossSpawn"))
            };

            result.Add(wave);
        }

        return result;
    }

    public static string ReadTextAsset(TextAsset textAsset)
    {
        if (textAsset == null)
        {
            return string.Empty;
        }

        byte[] bytes = textAsset.bytes;
        if (bytes == null || bytes.Length == 0)
        {
            return textAsset.text;
        }

        return DecodeCsvBytes(bytes);
    }

    private static string DecodeCsvBytes(byte[] bytes)
    {
        string utf8Text = TryDecode(bytes, new UTF8Encoding(false, true));
        if (!string.IsNullOrEmpty(utf8Text))
        {
            return utf8Text;
        }

        string gb18030Text = TryDecodeByName(bytes, "GB18030");
        if (!string.IsNullOrEmpty(gb18030Text))
        {
            return gb18030Text;
        }

        string gbkText = TryDecodeByName(bytes, "GBK");
        if (!string.IsNullOrEmpty(gbkText))
        {
            return gbkText;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string TryDecodeByName(byte[] bytes, string encodingName)
    {
        try
        {
            Encoding encoding = Encoding.GetEncoding(
                encodingName,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return TryDecode(bytes, encoding);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryDecode(byte[] bytes, Encoding encoding)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static List<Vector2> CalculateLayerPositions(int itemCount, float horizontalSpacing, float centerX, float y)
    {
        List<Vector2> positions = new List<Vector2>();
        for (int i = 0; i < itemCount; i++)
        {
            positions.Add(StageMapUILayoutUtility.CalculateCenteredRowPosition(i, itemCount, horizontalSpacing, centerX, y));
        }

        return positions;
    }

    public static List<List<string>> ParseCsvRows(string csvText)
    {
        List<List<string>> rows = new List<List<string>>();
        if (string.IsNullOrEmpty(csvText))
        {
            return rows;
        }

        List<string> currentRow = new List<string>();
        StringBuilder currentCell = new StringBuilder();
        bool insideQuotes = false;
        int braceDepth = 0;

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

            if (!insideQuotes)
            {
                if (c == '{')
                {
                    braceDepth++;
                }
                else if (c == '}' && braceDepth > 0)
                {
                    braceDepth--;
                }
            }

            if (c == ',' && !insideQuotes && braceDepth == 0)
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
                braceDepth = 0;
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

    public static List<int> ParseIntList(string value)
    {
        List<int> result = new List<int>();
        List<string> tokens = ParseStringList(value);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (TryParseInt(tokens[i], out int parsed))
            {
                result.Add(parsed);
            }
            else
            {
                Debug.LogWarning("[StageTableParser] Cannot parse int list token: " + tokens[i]);
            }
        }

        return result;
    }

    public static List<string> ParseStringList(string value)
    {
        List<string> result = new List<string>();
        string inner = StripOuterBraces(CleanCell(value));
        if (string.IsNullOrWhiteSpace(inner))
        {
            return result;
        }

        List<string> tokens = SplitTopLevel(inner, ',', ';', '|');
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = StripOuterBraces(CleanCell(tokens[i]));
            if (!string.IsNullOrWhiteSpace(token))
            {
                result.Add(token);
            }
        }

        return result;
    }

    public static List<WaveEnemyConfigData> ParseInitialEnemyConfigs(string value)
    {
        List<WaveEnemyConfigData> result = new List<WaveEnemyConfigData>();
        string inner = StripOuterBraces(CleanCell(value));
        if (string.IsNullOrWhiteSpace(inner))
        {
            return result;
        }

        List<string> enemyTokens = SplitTopLevel(inner, ';');
        for (int i = 0; i < enemyTokens.Count; i++)
        {
            string enemyText = StripOuterBraces(CleanCell(enemyTokens[i]));
            if (string.IsNullOrWhiteSpace(enemyText))
            {
                continue;
            }

            List<string> parts = SplitTopLevel(enemyText, ',');
            if (parts.Count < 3)
            {
                Debug.LogWarning("[StageTableParser] InitialEnemyConfigs item needs ChessId,x,y: " + enemyText);
                continue;
            }

            string chessId = CleanCell(parts[0]);
            if (string.IsNullOrWhiteSpace(chessId))
            {
                continue;
            }

            result.Add(new WaveEnemyConfigData
            {
                ChessId = chessId,
                GridPosition = new Vector2Int(ParseIntOrDefault(parts[1], 0), ParseIntOrDefault(parts[2], 0))
            });
        }

        return result;
    }

    public static BossInfoData ParseBossInfo(string value)
    {
        BossInfoData result = new BossInfoData();
        string inner = StripOuterBraces(CleanCell(value));
        if (string.IsNullOrWhiteSpace(inner))
        {
            return result;
        }

        List<string> parts = SplitTopLevel(inner, ',');
        if (parts.Count < 3)
        {
            Debug.LogWarning("[StageTableParser] BossInfo needs at least BossChessId,x,y: " + inner);
            return result;
        }

        result.BossChessId = CleanCell(parts[0]);
        result.GridPosition = new Vector2Int(ParseIntOrDefault(parts[1], 0), ParseIntOrDefault(parts[2], GameFlowManager.EnemyNestY));
        for (int i = 3; i < parts.Count; i++)
        {
            List<string> spawnIds = ParseStringList(parts[i]);
            if (spawnIds.Count == 0)
            {
                string spawnId = CleanCell(parts[i]);
                if (!string.IsNullOrWhiteSpace(spawnId))
                {
                    result.SpawnPoolChessIds.Add(spawnId);
                }

                continue;
            }

            result.SpawnPoolChessIds.AddRange(spawnIds);
        }

        return result;
    }

    public static BossSpawnData ParseBossSpawn(string value)
    {
        BossSpawnData result = new BossSpawnData();
        string inner = StripOuterBraces(CleanCell(value));
        if (string.IsNullOrWhiteSpace(inner))
        {
            return result;
        }

        List<string> parts = SplitTopLevel(inner, ',');
        if (parts.Count > 0)
        {
            result.BaseSpawnInterval = Mathf.Max(0.1f, ParseFloatOrDefault(parts[0], result.BaseSpawnInterval));
        }

        if (parts.Count > 1)
        {
            result.EnrageAcceleration = Mathf.Max(0f, ParseFloatOrDefault(parts[1], result.EnrageAcceleration));
        }

        if (parts.Count > 2)
        {
            result.MinSpawnInterval = Mathf.Max(0.1f, ParseFloatOrDefault(parts[2], result.MinSpawnInterval));
        }

        return result;
    }

    public static bool ParseHaveBoss(string value)
    {
        // 策划表规则：0 表示存在母巢，1 表示不存在。这里统一转成代码里更好理解的 true/false。
        if (!TryParseInt(value, out int rawValue))
        {
            return false;
        }

        if (rawValue != 0 && rawValue != 1)
        {
            Debug.LogWarning("[StageTableParser] HaveBoss should be 0(has boss) or 1(no boss), got: " + rawValue);
        }

        return rawValue == 0;
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
                    if (string.Equals(normalizedHeader, NormalizeHeader(fallbackFields[j]), StringComparison.OrdinalIgnoreCase)
                        && !result.ContainsKey(fallbackFields[j]))
                    {
                        result[fallbackFields[j]] = i;
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

    private static int FindHeaderRow(List<List<string>> rows, string requiredField)
    {
        int limit = Mathf.Min(5, rows.Count);
        string normalizedRequired = NormalizeHeader(requiredField);
        for (int rowIndex = 0; rowIndex < limit; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            for (int colIndex = 0; colIndex < row.Count; colIndex++)
            {
                if (string.Equals(NormalizeHeader(row[colIndex]), normalizedRequired, StringComparison.OrdinalIgnoreCase))
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
        return ParseIntOrDefault(GetString(row, columns, fieldName), 0);
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

    private static List<string> SplitTopLevel(string value, params char[] separators)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return result;
        }

        StringBuilder current = new StringBuilder();
        int braceDepth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '{')
            {
                braceDepth++;
            }
            else if (c == '}' && braceDepth > 0)
            {
                braceDepth--;
            }

            if (braceDepth == 0 && IsSeparator(c, separators))
            {
                result.Add(current.ToString());
                current.Length = 0;
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }

    private static bool IsSeparator(char value, char[] separators)
    {
        for (int i = 0; i < separators.Length; i++)
        {
            if (value == separators[i])
            {
                return true;
            }
        }

        return false;
    }

    private static int ParseIntOrDefault(string value, int fallback)
    {
        if (TryParseInt(value, out int result))
        {
            return result;
        }

        return fallback;
    }

    private static bool TryParseInt(string value, out int result)
    {
        string cleaned = CleanCell(value);
        if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        if (float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            result = Mathf.RoundToInt(floatValue);
            return true;
        }

        if (TryParseTrailingDigits(cleaned, out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryParseTrailingDigits(string value, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int endIndex = value.Length - 1;
        while (endIndex >= 0 && char.IsWhiteSpace(value[endIndex]))
        {
            endIndex--;
        }

        int startIndex = endIndex;
        while (startIndex >= 0 && char.IsDigit(value[startIndex]))
        {
            startIndex--;
        }

        startIndex++;
        if (startIndex > endIndex)
        {
            return false;
        }

        string digits = value.Substring(startIndex, endIndex - startIndex + 1);
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static float ParseFloatOrDefault(string value, float fallback)
    {
        string cleaned = CleanCell(value);
        if (float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }

        return fallback;
    }

    private static string StripOuterBraces(string value)
    {
        string cleaned = CleanCell(value);
        while (cleaned.Length >= 2 && cleaned[0] == '{' && cleaned[cleaned.Length - 1] == '}')
        {
            cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
        }

        return cleaned;
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
}
