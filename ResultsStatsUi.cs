using BGLib.Polyglot;
using HarmonyLib;
using HMUI;
using LiveHelper.Models;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiveHelper
{
    public sealed class ResultsStatsUi : IDisposable
    {
        private readonly SessionState _state;

        private readonly GameObject _buttonObject;

        private readonly GameObject _panelObject;

        private readonly Button _button;

        private readonly Texture2D[] _textures;

        private readonly Sprite[] _sprites;

        private readonly Material _uiMaterial;

        private readonly TextMeshProUGUI _leftText;

        private readonly TextMeshProUGUI _rightText;

        private bool _shown;

        public ResultsStatsUi(ResultsViewController view, SessionState state)
        {
            _state = state;
            var restartButton = GetField<Button>(view, "_restartButton");
            var scoreText = GetField<TextMeshProUGUI>(view, "_scoreText");
            var comboText = GetField<TextMeshProUGUI>(view, "_comboText");

            try
            {
                // Beat Saber's UI is rendered with a game-specific material. The default Unity UI
                // material renders as white even with a black texture.
                _uiMaterial = CreateUiMaterial(restartButton);
                _textures = new[]
                {
                    CreateTexture(new Color32(0, 0, 0, 225)),
                    CreateTexture(new Color32(255, 69, 94, 255)),
                    CreateTexture(new Color32(66, 209, 255, 255))
                };
                _sprites = _textures.Select(texture => Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                    100, 0, SpriteMeshType.FullRect)).ToArray();
                // Preserve Restart's native curved graphics, label, hover states and raycast
                // target. Only its click handler, caption and width change.
                _buttonObject = UnityEngine.Object.Instantiate(restartButton.gameObject, restartButton.transform.parent, false);
                _buttonObject.SetActive(false);
                _buttonObject.name = "LiveHelper Stats Toggle";
                _button = _buttonObject.GetComponent<Button>();
                _button.onClick = new Button.ButtonClickedEvent();
                _button.onClick.AddListener(Toggle);

                foreach (var localizer in _buttonObject.GetComponentsInChildren<LocalizedTextMeshProUGUI>(true))
                {
                    localizer.enabled = false;
                    UnityEngine.Object.Destroy(localizer);
                }
                var label = _buttonObject.GetComponentInChildren<TextMeshProUGUI>(true)
                    ?? throw new InvalidOperationException("Restart button label was not found");
                label.text = "Detail";

                var buttonRect = (RectTransform)_buttonObject.transform;
                var restartRect = (RectTransform)restartButton.transform;
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                var buttonWidth = Mathf.Min(24, restartRect.rect.width * 0.6f);
                var buttonLayout = _buttonObject.GetComponent<LayoutElement>();
                if (buttonLayout != null)
                {
                    buttonLayout.minWidth = buttonWidth;
                    buttonLayout.preferredWidth = buttonWidth;
                    buttonLayout.flexibleWidth = 0;
                }
                buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, buttonWidth);
                buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, restartRect.rect.height);
                var restartLeft = restartRect.TransformPoint(new Vector3(restartRect.rect.xMin, restartRect.rect.center.y));
                var buttonParent = buttonRect.parent;
                var restartLeftLocal = buttonParent.InverseTransformPoint(restartLeft);
                buttonRect.localPosition = new Vector3(restartLeftLocal.x - (buttonWidth * 0.5f) - 3,
                    restartLeftLocal.y, restartLeftLocal.z);

                _panelObject = new GameObject("LiveHelper Hand Stats", typeof(RectTransform))
                {
                    layer = _buttonObject.layer
                };
                _panelObject.transform.SetParent(view.transform, false);
                var panelRect = (RectTransform)_panelObject.transform;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(53, 30);
                var scoreRect = scoreText.rectTransform;
                var scoreCenter = panelRect.parent.InverseTransformPoint(scoreRect.TransformPoint(scoreRect.rect.center));
                var scoreLeft = panelRect.parent.InverseTransformPoint(
                    scoreRect.TransformPoint(new Vector3(scoreRect.rect.xMin, scoreRect.rect.center.y)));
                panelRect.localPosition = new Vector3(scoreLeft.x - 67f, scoreCenter.y, 0);
                // ImageView writes the same curved-canvas vertex data as the game's text; RawImage
                // remains flat and drifts away from it in VR.
                ConfigureImage(_panelObject.AddComponent<ImageView>(), _sprites[0]);

                CreateAccent(panelRect, 2.2f, _sprites[1]);
                CreateAccent(panelRect, 28f, _sprites[2]);
                _leftText = CreateText(comboText, panelRect, "Left", 3.8f);
                _rightText = CreateText(comboText, panelRect, "Right", 29.6f);
                _panelObject.SetActive(false);
                _buttonObject.SetActive(true);
                Plugin.Instance?.Logger.Info("Results Detail button and curved ImageView panel use " + _uiMaterial.name +
                    " (shader " + _uiMaterial.shader.name + ", layer " + _buttonObject.layer + ")");
            }
            catch
            {
                if (_buttonObject != null)
                {
                    UnityEngine.Object.Destroy(_buttonObject);
                }

                if (_panelObject != null)
                {
                    UnityEngine.Object.Destroy(_panelObject);
                }

                if (_sprites != null)
                {
                    foreach (var sprite in _sprites)
                    {
                        UnityEngine.Object.Destroy(sprite);
                    }
                }

                if (_textures != null)
                {
                    foreach (var texture in _textures)
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }

                if (_uiMaterial != null)
                {
                    UnityEngine.Object.Destroy(_uiMaterial);
                }

                throw;
            }
        }

        private static T GetField<T>(ResultsViewController view, string name) where T : class
        {
            return AccessTools.Field(typeof(ResultsViewController), name)?.GetValue(view) as T
                ?? throw new InvalidOperationException("Results view field was not found: " + name);
        }

        private static Texture2D CreateTexture(Color32 color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static Material CreateUiMaterial(Button restartButton)
        {
            // This is the same source used by BSML's ImageResources.NoGlowMat. Take the material
            // from the graphic child, not the button root.
            Material? source = null;
            foreach (var menu in Resources.FindObjectsOfTypeAll<MainMenuViewController>())
            {
                var solo = AccessTools.Field(typeof(MainMenuViewController), "_soloButton")?.GetValue(menu) as Button;
                var image = solo != null ? solo.transform.Find("Image/Image0")?.GetComponent<Image>() : null;
                if (image != null && IsGameUiMaterial(image.material))
                {
                    source = image.material;
                    break;
                }
            }
            if (source == null)
            {
                source = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(material =>
                    material.name.StartsWith("UINoGlow", StringComparison.Ordinal) && IsGameUiMaterial(material));
            }

            if (source == null)
            {
                source = restartButton.GetComponentsInChildren<Image>(true)
                    .Select(image => image.material).FirstOrDefault(IsGameUiMaterial);
            }

            if (source == null)
            {
                throw new InvalidOperationException("No compatible Beat Saber UI material was found");
            }

            return new Material(source) { name = "LiveHelper UI (" + source.name + ")" };
        }

        private static bool IsGameUiMaterial(Material material)
        {
            return material != null && material != Graphic.defaultGraphicMaterial && material.shader != null &&
                   !material.shader.name.StartsWith("UI/Default", StringComparison.Ordinal);
        }

        private void ConfigureImage(ImageView image, Sprite sprite)
        {
            image.useScriptableObjectColors = false;
            image.gradient = false;
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.material = _uiMaterial;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private void CreateAccent(RectTransform parent, float x, Sprite sprite)
        {
            var accent = new GameObject("Hand Accent", typeof(RectTransform))
            {
                layer = parent.gameObject.layer
            };
            accent.transform.SetParent(parent, false);
            var rect = (RectTransform)accent.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -3);
            rect.sizeDelta = new Vector2(0.65f, 24);
            ConfigureImage(accent.AddComponent<ImageView>(), sprite);
        }

        private static TextMeshProUGUI CreateText(TextMeshProUGUI template, RectTransform parent, string name, float x)
        {
            var item = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            item.name = "LiveHelper " + name;
            foreach (var localizer in item.GetComponentsInChildren<LocalizedTextMeshProUGUI>(true))
            {
                localizer.enabled = false;
                UnityEngine.Object.Destroy(localizer);
            }
            var text = item.GetComponent<TextMeshProUGUI>();
            foreach (var graphic in item.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic != text)
                {
                    graphic.enabled = false;
                }
            }

            text.color = Color.white;
            text.fontSize = 2.8f;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            var rect = text.rectTransform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -3);
            rect.sizeDelta = new Vector2(21.5f, 24);
            return text;
        }

        private void Toggle()
        {
            _shown = !_shown;
            if (_shown)
            {
                var snapshot = _state.Snapshot_Sync;
                _leftText.text = FormatHand("左手", snapshot.left);
                _rightText.text = FormatHand("右手", snapshot.right);
            }
            _panelObject.SetActive(_shown);
        }

        public void Dispose()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(Toggle);
            }

            if (_buttonObject != null)
            {
                UnityEngine.Object.Destroy(_buttonObject);
            }

            if (_panelObject != null)
            {
                UnityEngine.Object.Destroy(_panelObject);
            }

            if (_sprites != null)
            {
                foreach (var sprite in _sprites)
                {
                    UnityEngine.Object.Destroy(sprite);
                }
            }

            if (_textures != null)
            {
                foreach (var texture in _textures)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            if (_uiMaterial != null)
            {
                UnityEngine.Object.Destroy(_uiMaterial);
            }
        }

        private string FormatHand(string name, HandInfo hand)
        {
            return name + "\n" +
                   "完成 " + hand.completed.ToString() + "\n" +
                   "失误 " + hand.missed.ToString() + "\n" +
                   "失误率 " + Percent(hand.missRate) + "\n" +
                   "平均准度 " + (hand.averageAccuracy.HasValue ? Percent(hand.averageAccuracy.Value) : "—");
        }

        private string Percent(double value)
        {
            return value.ToString("0.0") + "%";
        }
    }
}