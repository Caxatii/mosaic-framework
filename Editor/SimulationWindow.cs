using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mosaic.Core;
using Mosaic.Ui;
using UnityEditor;
using UnityEngine;
using EntityId = Mosaic.Core.EntityId;

namespace Mosaic.Editor
{
    public sealed class SimulationWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Trace", "World" };

        private int _tab;
        private int _worldIndex;
        private Vector2 _scroll;
        private Vector2 _detailsScroll;
        private string _filter = "";
        private bool _paused;
        private TraceRecord[] _frozen;
        private EntityId? _selected;
        private GUIStyle _mono;

        [MenuItem("Window/Mosaic/Simulation")]
        public static void Open() => GetWindow<SimulationWindow>("Simulation");

        private void OnEnable() => DebugWorlds.Changed += Repaint;

        private void OnDisable() => DebugWorlds.Changed -= Repaint;

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying && !_paused)
                Repaint();
        }

        private void OnGUI()
        {
            _mono ??= new GUIStyle(EditorStyles.label) { font = Font.CreateDynamicFontFromOSFont("Consolas", 12), richText = false };

            var worlds = DebugWorlds.All;
            if (worlds.Count == 0)
            {
                EditorGUILayout.HelpBox("No registered worlds. Enter Play Mode: a world appears here once the game " +
                                        "calls DebugWorlds.Register.", MessageType.Info);
                return;
            }

            _worldIndex = Mathf.Clamp(_worldIndex, 0, worlds.Count - 1);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _worldIndex = EditorGUILayout.Popup(_worldIndex, worlds.Select(w => w.Name).ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(160));
                _tab = GUILayout.Toolbar(_tab, Tabs, EditorStyles.toolbarButton, GUILayout.Width(220));
                GUILayout.FlexibleSpace();
                _filter = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            }

            var world = worlds[_worldIndex];
            if (_tab == 0)
                DrawTrace(world.Context);
            else
                DrawWorld(world);
        }

        private void DrawTrace(GameplayContext context)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var recording = context.Trace != null;
                if (GUILayout.Toggle(recording, "Record", EditorStyles.toolbarButton, GUILayout.Width(60)) != recording)
                    context.Trace = recording ? null : new SimulationTrace(512);

                if (context.Trace != null)
                    context.Trace.IncludeQuiet = GUILayout.Toggle(context.Trace.IncludeQuiet, "Quiet ticks", EditorStyles.toolbarButton, GUILayout.Width(80));

                var paused = GUILayout.Toggle(_paused, "Pause", EditorStyles.toolbarButton, GUILayout.Width(50));
                if (paused != _paused)
                {
                    _paused = paused;
                    _frozen = paused ? context.Trace?.Recent.ToArray() : null;
                }

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    context.Trace?.Clear();
                    _frozen = null;
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    EditorGUIUtility.systemCopyBuffer = string.Join("\n", Records(context).Select(r => r.ToString()));
            }

            if (context.Trace == null)
            {
                EditorGUILayout.HelpBox("Tracing is off. Press Record to enable it for this world (debug only: " +
                                        "it allocates strings for every action).", MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var record in Records(context).Reverse())
            {
                var text = record.ToString();
                if (_filter.Length > 0 && text.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                GUILayout.Label($"#{record.Frame}", EditorStyles.miniBoldLabel);
                GUILayout.Label(text.TrimEnd(), _mono);
            }
            EditorGUILayout.EndScrollView();
        }

        private IEnumerable<TraceRecord> Records(GameplayContext context) =>
            _frozen ?? (IEnumerable<TraceRecord>)context.Trace?.Recent ?? Array.Empty<TraceRecord>();

        private void DrawWorld(DebugWorlds.Entry entry)
        {
            var world = entry.Context.World;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(260)))
                {
                    GUILayout.Label($"Entities: {world.Count}  (filter by name or component)", EditorStyles.miniLabel);
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    foreach (var id in world.Entities.ToList())
                    {
                        var label = $"{id.Index}:{id.Generation}  {entry.DescribeKind(world.Get<Kind>(id)?.Id ?? 0)}";
                        if (_filter.Length > 0 && !Matches(world, id, label))
                            continue;

                        var selected = _selected == id;
                        if (GUILayout.Toggle(selected, label, EditorStyles.miniButtonLeft) && !selected)
                            _selected = id;
                    }
                    EditorGUILayout.EndScrollView();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    if (_selected is not { } selectedId || !world.IsAlive(selectedId))
                    {
                        GUILayout.Label("Select an entity on the left.", EditorStyles.centeredGreyMiniLabel);
                        return;
                    }

                    _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);
                    foreach (var component in world.GetAll(selectedId).OrderBy(c => c.GetType().Name))
                        DrawComponent(component);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private bool Matches(World world, EntityId id, string label) =>
            label.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            world.GetAll(id).Any(c => c.GetType().Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);

        private static void DrawComponent(IModel component)
        {
            var type = component.GetType();
            GUILayout.Label(type.Name, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (property.GetIndexParameters().Length > 0)
                        continue;

                    string value;
                    try
                    {
                        value = Describe(property.GetValue(component));
                    }
                    catch (Exception exception)
                    {
                        value = $"<{exception.GetType().Name}>";
                    }

                    EditorGUILayout.LabelField(property.Name, value);
                }
            }
        }

        private static string Describe(object value) => value switch
        {
            null => "null",
            string text => text,
            System.Collections.ICollection collection => $"{collection.GetType().Name} [{collection.Count}]",
            _ => value.ToString(),
        };
    }
}
