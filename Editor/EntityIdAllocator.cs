using System.Collections.Generic;
using System.Linq;
using Mosaic.Ui;
using UnityEditor;
using UnityEngine;

namespace Mosaic.Editor
{
    internal sealed class EntityIdAllocator : AssetPostprocessor
    {
        private const string SettingsPath = "ProjectSettings/MosaicEntityIds.txt";

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!imported.Any(path => path.EndsWith(".asset")))
                return;

            var fresh = new HashSet<string>(imported);
            var configs = AssetDatabase.FindAssets("t:" + nameof(NumberedConfig))
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => fresh.Contains(path))
                .ThenBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<NumberedConfig>)
                .Where(config => config != null)
                .ToList();

            var last = Mathf.Max(LoadLast(), configs.Select(config => config.Id).DefaultIfEmpty(0).Max());
            var taken = new HashSet<int>();

            foreach (var config in configs)
            {
                if (config.Id > 0 && taken.Add(config.Id))
                    continue;

                config.AssignId(++last);
                taken.Add(config.Id);
                EditorUtility.SetDirty(config);
                Debug.Log($"Mosaic: {config.name} was assigned id {config.Id}", config);
            }

            SaveLast(last);
        }

        private static int LoadLast() =>
            System.IO.File.Exists(SettingsPath) && int.TryParse(System.IO.File.ReadAllText(SettingsPath), out var last) ? last : 0;

        private static void SaveLast(int last)
        {
            if (LoadLast() != last)
                System.IO.File.WriteAllText(SettingsPath, last.ToString());
        }
    }
}
