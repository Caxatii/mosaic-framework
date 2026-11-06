using System.Collections.Generic;
using Mosaic.Ui;
using UnityEditor;
using UnityEngine;

namespace Mosaic.Editor
{
    public abstract class ConfigRefDrawer<TConfig> : PropertyDrawer where TConfig : NumberedConfig
    {
        private static Dictionary<int, TConfig> _byId;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var id = property.FindPropertyRelative("Id");
            var current = Find(id.intValue);

            var color = GUI.color;
            if (id.intValue != 0 && current == null)
                GUI.color = Color.red;

            EditorGUI.BeginProperty(position, label, property);
            var picked = (TConfig)EditorGUI.ObjectField(position, label, current, typeof(TConfig), false);
            if (picked != current)
                id.intValue = picked != null ? picked.Id : 0;
            EditorGUI.EndProperty();

            GUI.color = color;
        }

        private static TConfig Find(int id)
        {
            if (id == 0)
                return null;

            if (_byId == null || !_byId.TryGetValue(id, out var config) || config == null)
            {
                _byId = new Dictionary<int, TConfig>();
                foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(TConfig).Name))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TConfig>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset != null)
                        _byId[asset.Id] = asset;
                }
            }

            return _byId.TryGetValue(id, out var found) ? found : null;
        }
    }
}
