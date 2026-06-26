using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class UnitCSVPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (UnitCSVImporter.IsSilentBatchImportRunning)
        {
            return;
        }

        if (StageMapCSVImporter.IsSilentMapNodeImportRunning)
        {
            return;
        }

        if (TraitCSVImporter.IsSilentTraitImportRunning)
        {
            return;
        }

        if (ContainsShopCsv(importedAssets)
            || ContainsShopCsv(deletedAssets)
            || ContainsShopCsv(movedAssets)
            || ContainsShopCsv(movedFromAssetPaths))
        {
            ShopManagerCSVImporter.ExecuteSilentShopImport();
        }

        if (ContainsTraitCsv(importedAssets)
            || ContainsTraitCsv(deletedAssets)
            || ContainsTraitCsv(movedAssets)
            || ContainsTraitCsv(movedFromAssetPaths)
            || ContainsTraitIcon(importedAssets))
        {
            TraitCSVImporter.ExecuteSilentTraitImport();
        }

        if (ContainsMapNodeCsv(importedAssets))
        {
            StageMapCSVImporter.ExecuteSilentMapNodeImport();
        }

        if (importedAssets == null || importedAssets.Length == 0)
        {
            return;
        }

        if (ContainsUnitIcon(importedAssets))
        {
            UnitCSVImporter.ExecuteSilentBatchImport("Assets/Data");
            return;
        }

        string sourceFolder = string.Empty;
        for (int i = 0; i < importedAssets.Length; i++)
        {
            string assetPath = importedAssets[i];
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string fileName = Path.GetFileName(assetPath);
            if (string.Equals(fileName, UnitCSVImporter.PlayerCsvFileName, StringComparison.Ordinal)
                || string.Equals(fileName, UnitCSVImporter.EnemyCsvFileName, StringComparison.Ordinal))
            {
                sourceFolder = Path.GetDirectoryName(assetPath);
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return;
        }

        string normalizedSourceFolder = sourceFolder.Replace('\\', '/').TrimEnd('/');
        if (UnitCSVImporter.ExecuteSilentBatchImport(normalizedSourceFolder))
        {
            Debug.Log("<color=#00FF66>[Hot Reload Complete]</color> Unit CSV data and art assets were synchronized.");
        }
    }

    private static bool ContainsShopCsv(string[] assetPaths)
    {
        if (assetPaths == null)
        {
            return false;
        }

        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string fileName = Path.GetFileName(assetPath);
            if (string.Equals(fileName, "ShopPool.csv", StringComparison.Ordinal)
                || string.Equals(fileName, "ShopProbability.csv", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUnitIcon(string[] assetPaths)
    {
        if (assetPaths == null)
        {
            return false;
        }

        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith("Assets/Resources/Units/Icons/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extension = Path.GetExtension(normalizedPath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMapNodeCsv(string[] assetPaths)
    {
        if (assetPaths == null)
        {
            return false;
        }

        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            string fileName = Path.GetFileName(normalizedPath);
            if (string.Equals(fileName, "MapNode.csv", StringComparison.Ordinal)
                && string.Equals(normalizedPath, StageMapCSVImporter.MapNodeCsvPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTraitCsv(string[] assetPaths)
    {
        if (assetPaths == null)
        {
            return false;
        }

        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            string fileName = Path.GetFileName(normalizedPath);
            if ((string.Equals(fileName, "Trait.csv", StringComparison.Ordinal)
                    && string.Equals(normalizedPath, TraitCSVImporter.TraitCsvPath, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fileName, "TraitTier.csv", StringComparison.Ordinal)
                    && string.Equals(normalizedPath, TraitCSVImporter.TraitTierCsvPath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTraitIcon(string[] assetPaths)
    {
        if (assetPaths == null)
        {
            return false;
        }

        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith(TraitCSVImporter.TraitIconFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extension = Path.GetExtension(normalizedPath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
