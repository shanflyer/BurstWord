using BurstWord.BRG;
using UnityEditor;
using UnityEngine;

namespace BurstWord.Baseline.Editor
{
    [CustomEditor(typeof(BrgDamageTextRenderer))]
    public sealed class BrgRendererInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var renderer = (BrgDamageTextRenderer)target;
            var previousBackend = renderer.renderBackend;
            DrawDefaultInspector();
            if (Application.isPlaying && renderer.isActiveAndEnabled && previousBackend != renderer.renderBackend)
            { renderer.enabled = false; renderer.enabled = true; }
            var animationRenderer = (BrgDamageTextRenderer)target;
            if (GUILayout.Button("编辑 / 预览 GPU 动画")) BrgAnimationEditor.Open(animationRenderer.defaultAnimation, animationRenderer);
            if (!Application.isPlaying) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live rendering statistics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Active backend", renderer.ActiveBackend.ToString());
            EditorGUILayout.LabelField("Backend selection", renderer.BackendReason);
            EditorGUILayout.LabelField("Active labels / glyphs", renderer.ActiveCount + " / " + renderer.ActiveGlyphCount);
            EditorGUILayout.LabelField("Emitted / capacity drops", renderer.EmittedCount + " / " + renderer.DroppedCount);
            EditorGUILayout.LabelField("Draw commands / submitted glyphs", renderer.DrawCommandCount + " / " + renderer.SubmittedGlyphCount);
            EditorGUILayout.LabelField("Missing glyphs / sprites / shaping sources",renderer.MissingGlyphCount+" / "+renderer.MissingSpriteCount+" / "+renderer.UnavailableShapingCount);
        }
        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
