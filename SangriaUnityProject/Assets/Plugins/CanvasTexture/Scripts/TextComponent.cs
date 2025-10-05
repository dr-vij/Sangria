using System;
using UnityEngine;
using TMPro;
using Unity.Mathematics;
using UnityEngine.Serialization;
using ViJApps.CanvasTexture.Utils;

namespace ViJApps.CanvasTexture
{
    public class TextComponent : MonoBehaviour
    {
        [SerializeField] private TextMeshPro m_TMPComponent;
        [SerializeField] private RectTransform m_RectTransform;

        private float m_Rotation = 0f;
        private float2 m_Position = float2.zero;
        private float m_Aspect = 1f;

        public float Aspect
        {
            get => m_Aspect;
            set
            {
                m_Aspect = value;
                transform.localScale = new Vector3(value, 1f, 1f);
            }
        }

        public float Rotation
        {
            get => m_Rotation;
            set
            {
                m_Rotation = value;
                m_RectTransform.localRotation = Quaternion.Euler(0f, 0f, m_Rotation);
                m_RectTransform.localPosition = Vector3.zero;
            }
        }

        public string Text
        {
            get => m_TMPComponent.text;
            set => m_TMPComponent.text = value;
        }

        public Vector2 Position
        {
            get => m_Position;
            set
            {
                m_Position = value;
                m_RectTransform.localPosition = Vector3.zero;
                transform.position = new Vector3(m_Position.x, m_Position.y, 0f);
            }
        }

        public Vector2 SizeDelta
        {
            get => m_RectTransform.sizeDelta;
            set => m_RectTransform.sizeDelta = value;
        }

        public Vector2 Pivot
        {
            get => m_RectTransform.pivot;
            set => m_RectTransform.pivot = value;
        }

        public Vector2 AnchorMin
        {
            get => m_RectTransform.anchorMin;
            set => m_RectTransform.anchorMin = value;
        }

        public Vector2 AnchorMax
        {
            get => m_RectTransform.anchorMax;
            set => m_RectTransform.anchorMax = value;
        }

        public void UpdateText()
        {
            m_TMPComponent.SetVerticesDirty();
            m_TMPComponent.SetLayoutDirty();
            m_TMPComponent.ForceMeshUpdate();
        }

        public Material Material => m_TMPComponent.renderer.material;

        public Renderer Renderer => m_TMPComponent.renderer;

        public void Clear()
        {
            m_TMPComponent.text = string.Empty;
            var settings = TextSettings.Default;
            SetSettings(settings);
        }

        public void SetSettings(TextSettings settings)
        {
            m_TMPComponent.fontSize = m_TMPComponent.font.GetFontSizeToFitWorldSize(settings.FontSize);
            m_TMPComponent.fontWeight = settings.FontWeight;
            m_TMPComponent.fontStyle = settings.FontStyle;
            m_TMPComponent.color = settings.FontColor;
            m_TMPComponent.alignment = settings.TextAlignment;

            //Spacing
            m_TMPComponent.lineSpacing = settings.SpacingOptions.Line;
            m_TMPComponent.wordSpacing = settings.SpacingOptions.Word;
            m_TMPComponent.characterSpacing = settings.SpacingOptions.Character;
            m_TMPComponent.paragraphSpacing = settings.SpacingOptions.Paragraph;

            m_TMPComponent.textWrappingMode =  settings.Wrapping;
            m_TMPComponent.overflowMode = settings.OverflowMode;
        }
    }
}