using System.Collections.Generic;
using System.Globalization;
using Mosaic.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mosaic.Ui
{
    /// <summary>
    /// The cheat window: built in code from the registry, no prefab. Every cheat without text arguments becomes a
    /// button; a number argument becomes a slider when its range is known and a field otherwise. Cheats that take
    /// words (an item name) stay in the console. Bind it to a key with <c>UiLayers.Bind</c>.
    /// </summary>
    public sealed class CheatPanel : UiWindow
    {
        private const float Width = 420;
        private const float RowHeight = 40;

        private static readonly Color Background = new Color(0.08f, 0.09f, 0.11f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.33f, 1);
        private static readonly Color FieldColor = new Color(0.16f, 0.18f, 0.22f, 1);
        private static readonly Color TextColor = new Color(0.92f, 0.93f, 0.95f, 1);
        private static readonly Color DimColor = new Color(0.62f, 0.66f, 0.72f, 1);

        private readonly CheatRegistry _cheats;
        private readonly string _title;
        private readonly List<TMP_InputField> _fields = new List<TMP_InputField>();
        private TMP_Text _status;

        public CheatPanel(CheatRegistry cheats, string title = "Cheats")
        {
            _cheats = cheats ?? throw new System.ArgumentNullException(nameof(cheats));
            _title = title;
        }

        public override bool CapturesPointer => true;

        public override bool CapturesKeyboard
        {
            get
            {
                foreach (var field in _fields)
                    if (field != null && field.isFocused)
                        return true;
                return false;
            }
        }

        protected internal override void Opened()
        {
            _fields.Clear();
            var root = ShowEmpty("CheatPanel");

            var panel = Child(root, "Panel", typeof(Image), typeof(VerticalLayoutGroup));
            panel.anchorMin = new Vector2(1, 0);
            panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1, 0.5f);
            panel.sizeDelta = new Vector2(Width, -40);
            panel.anchoredPosition = new Vector2(-20, 0);
            panel.GetComponent<Image>().color = Background;
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            Text(panel, _title, 26, FontStyles.Bold, TextColor, RowHeight);

            var scroll = DefaultControls.CreateScrollView(new DefaultControls.Resources());
            scroll.transform.SetParent(panel, false);
            scroll.GetComponent<Image>().color = Color.clear;
            scroll.AddComponent<LayoutElement>().flexibleHeight = 1;
            var scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            Object.Destroy(scrollRect.horizontalScrollbar.gameObject);
            scrollRect.horizontalScrollbar = null;
            scrollRect.verticalScrollbar.GetComponent<Image>().color = FieldColor;
            scrollRect.verticalScrollbar.handleRect.GetComponent<Image>().color = ButtonColor;

            var viewport = scrollRect.viewport.gameObject;
            Object.DestroyImmediate(viewport.GetComponent<Mask>());
            Object.DestroyImmediate(viewport.GetComponent<Image>());
            viewport.AddComponent<RectMask2D>();

            var content = scrollRect.content;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8;
            list.padding = new RectOffset(0, 14, 0, 0);
            list.childControlHeight = true;
            list.childControlWidth = true;
            list.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var any = false;
            foreach (var cheat in _cheats.ForWindow)
            {
                AddCheat(content, cheat);
                any = true;
            }

            if (!any)
                Text(content, "No cheats without text arguments. Use the console.", 18, FontStyles.Normal, DimColor, RowHeight);

            _status = Text(panel, "", 18, FontStyles.Normal, DimColor, RowHeight);
        }

        protected internal override void Closed()
        {
            _fields.Clear();
            _status = null;
        }

        private void AddCheat(RectTransform parent, Cheat cheat)
        {
            var group = Child(parent, cheat.Name, typeof(VerticalLayoutGroup));
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var values = new float[cheat.Parameters.Count];
            var button = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            button.name = "Run";
            button.transform.SetParent(group, false);
            button.GetComponent<Image>().color = ButtonColor;
            button.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = cheat.Name;
            label.fontSize = 21;
            label.color = TextColor;
            button.GetComponent<Button>().onClick.AddListener(() => Run(cheat, values));

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = cheat.Parameters[i].Initial;
                AddParameter(group, cheat.Parameters[i], values, i);
            }

            if (cheat.Description.Length > 0)
                Text(group, cheat.Description, 17, FontStyles.Normal, DimColor, 24);
        }

        private void AddParameter(RectTransform parent, CheatParameter parameter, float[] values, int index)
        {
            var row = Child(parent, parameter.Name, typeof(HorizontalLayoutGroup));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            var name = Text(row, parameter.Name, 18, FontStyles.Normal, DimColor, 32);
            name.GetComponent<LayoutElement>().preferredWidth = 100;

            if (parameter.HasRange)
            {
                var slider = DefaultControls.CreateSlider(new DefaultControls.Resources()).GetComponent<Slider>();
                slider.transform.SetParent(row, false);
                slider.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                slider.minValue = parameter.Min;
                slider.maxValue = parameter.Max;
                slider.wholeNumbers = parameter.IsWhole;
                slider.value = values[index];
                slider.fillRect.GetComponent<Image>().color = ButtonColor;
                slider.handleRect.GetComponent<Image>().color = TextColor;
                slider.transform.Find("Background").GetComponent<Image>().color = FieldColor;

                var shown = Text(row, Format(values[index]), 18, FontStyles.Normal, TextColor, 32);
                shown.GetComponent<LayoutElement>().preferredWidth = 56;
                shown.alignment = TextAlignmentOptions.MidlineRight;
                slider.onValueChanged.AddListener(value =>
                {
                    values[index] = value;
                    shown.text = Format(value);
                });
                return;
            }

            var input = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources()).GetComponent<TMP_InputField>();
            input.transform.SetParent(row, false);
            input.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            input.GetComponent<Image>().color = FieldColor;
            input.textComponent.color = TextColor;
            input.textComponent.fontSize = 18;
            input.contentType = parameter.IsWhole ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
            input.text = Format(values[index]);
            input.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    values[index] = value;
                input.text = Format(values[index]);
            });
            _fields.Add(input);
        }

        private void Run(Cheat cheat, float[] values)
        {
            var result = _cheats.Execute(cheat, values);
            if (_status != null)
            {
                _status.text = $"{cheat.Name}: {result.Message}";
                _status.color = result.IsOk ? DimColor : new Color(1f, 0.55f, 0.5f, 1);
            }
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static RectTransform Child(Transform parent, string name, params System.Type[] components)
        {
            var child = new GameObject(name, typeof(RectTransform));
            foreach (var component in components)
                child.AddComponent(component);
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static TMP_Text Text(Transform parent, string text, float size, FontStyles style, Color color, float height)
        {
            var rect = Child(parent, "Text", typeof(TextMeshProUGUI), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = height;
            var label = rect.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }
    }
}
