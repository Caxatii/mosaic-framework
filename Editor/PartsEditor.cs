using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Mosaic.Ui;
using UnityEditor;
using UnityEngine;

namespace Mosaic.Editor
{
    [CustomEditor(typeof(NumberedConfig), editorForChildClasses: true)]
    public sealed class PartsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Id", ((NumberedConfig)target).Id);

            DrawPropertiesExcluding(serializedObject, "m_Script", "_id");
            serializedObject.ApplyModifiedProperties();

            foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.IsDefined(typeof(SerializeReference)) && field.GetValue(target) is IList list && ElementOf(field.FieldType) is { } element)
                    if (GUILayout.Button($"Add to {ObjectNames.NicifyVariableName(field.Name)}"))
                        Menu(list, element).ShowAsContext();
        }

        private GenericMenu Menu(IList list, Type element)
        {
            var menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom(element)
                .Where(type => !type.IsAbstract && type.IsDefined(typeof(SerializableAttribute), false))
                .OrderBy(type => type.Namespace).ThenBy(type => type.Name);

            foreach (var type in types)
            {
                var label = new GUIContent($"{type.Namespace?.Replace("Mosaic.", "")}/{type.Name}");
                if (list.Cast<object>().Any(item => item?.GetType() == type))
                    menu.AddDisabledItem(label);
                else
                    menu.AddItem(label, false, () =>
                    {
                        Undo.RecordObject(target, "Add part");
                        list.Add(Activator.CreateInstance(type));
                        EditorUtility.SetDirty(target);
                    });
            }

            return menu;
        }

        private static Type ElementOf(Type listType) =>
            listType.IsGenericType ? listType.GetGenericArguments()[0] : listType.IsArray ? listType.GetElementType() : null;
    }
}
