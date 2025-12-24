using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.UI
{
    public class Gizmo_SkipdoorIdleUnified : Gizmo
    {
        private readonly List<CompSkipdoor> comps;

        public Gizmo_SkipdoorIdleUnified(List<CompSkipdoor> comps)
        {
            this.comps = comps;
            this.Order = -100f;
        }

        public override float GetWidth(float maxWidth) { return Mathf.Min(280f, maxWidth); }
        private static float Clamp01(float v) { return Mathf.Clamp01(v); }
        private static float Snap01_10pct(float v)
        {
            return Mathf.Clamp01(Mathf.Round(v * 10f) / 10f);
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            bool multi = comps != null && comps.Count > 1;
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 76f);
            Widgets.DrawWindowBackground(rect);

            // Label
            var labelRect = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 22f);
            if (multi)
                Widgets.Label(labelRect, $"Skipdoors selected: {comps.Count}");
            else
                Widgets.Label(labelRect, "Skipdoor");

            // Compute idle stats (min/max/avg). For single: actual openness too.
            float minIdle = 1f, maxIdle = 0f, sumIdle = 0f;
            float actual = 0f;

            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                float idle = Clamp01(c.IdleOpen);
                if (idle < minIdle) minIdle = idle;
                if (idle > maxIdle) maxIdle = idle;
                sumIdle += idle;
            }
            bool mixed = multi && (maxIdle - minIdle) > 0.0001f;
            float avgIdle = sumIdle / Mathf.Max(1, comps.Count);

            if (!multi) actual = Clamp01(comps[0].ActualOpen);

            // Bars
            var barRect = new Rect(rect.x + 8f, rect.y + 28f, rect.width - 16f, 16f);
            Widgets.DrawAltRect(barRect);

            if (multi)
            {
                // MULTI: Grey placeholder when mixed; yellow when all match. No red bar in multi.
                if (mixed)
                {
                    // grey at average (visual hint)
                    var greyFill = new Rect(barRect.x, barRect.y, barRect.width * avgIdle, barRect.height);
                    GUI.color = Color.gray;
                    GUI.DrawTexture(greyFill, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
                else
                {
                    var yellowFill = new Rect(barRect.x, barRect.y, barRect.width * avgIdle, barRect.height);
                    GUI.color = new Color(1f, 0.88f, 0.1f);
                    GUI.DrawTexture(yellowFill, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }
            else
            {
                // SINGLE: red (actual) under yellow (idle)
                var redFill = new Rect(barRect.x, barRect.y, barRect.width * actual, barRect.height);
                GUI.color = Color.red;
                GUI.DrawTexture(redFill, BaseContent.WhiteTex);
                GUI.color = Color.white;

                var yellowFill = new Rect(barRect.x, barRect.y, barRect.width * comps[0].IdleOpen, barRect.height);
                GUI.color = new Color(1f, 0.88f, 0.1f);
                GUI.DrawTexture(yellowFill, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }

            // Slider (10% steps). Mixed: show average as the handle position.
            var sliderRect = new Rect(rect.x + 8f, rect.y + 50f, rect.width - 16f, 18f);
            float handle = multi ? Snap01_10pct(avgIdle) : Snap01_10pct(comps[0].IdleOpen);
            int shownPct = Mathf.RoundToInt(handle * 100f);
            string label = multi
                ? (mixed ? $"Idle openness (mixed): {shownPct}%" : $"Idle openness: {shownPct}%")
                : $"Idle openness: {shownPct}%";

            float newIdle = Widgets.HorizontalSlider(
                sliderRect,
                handle,  // feed the snapped handle
                0f, 1f, true,
                label, "0%", "100%", 0.1f
            );

            // Only write if the user actually changed it (compare to the same snapped handle)
            if (!Mathf.Approximately(newIdle, handle))
            {
                for (int i = 0; i < comps.Count; i++)
                    comps[i].IdleOpen = newIdle;
            }

            if (Mouse.IsOver(rect))
            {
                string tip = multi
                    ? "Yellow=idle (when all match). Grey=mixed idle values.\nDragging sets the same idle openness on all selected skipdoors.\nRed (actual openness) is hidden in multi-selection."
                    : "Yellow=idle (configured). Red=actual (live, power-weighted).";
                TooltipHandler.TipRegion(rect, tip);
            }

            return new GizmoResult(GizmoState.Clear);
        }
    }
}
