using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace RigidIdeology
{
    // --- 1. НАСТРОЙКИ МОДА ---
    public class RigidSettings : ModSettings
    {
        public bool allowInReform = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref allowInReform, "allowInReform", false);
        }
    }

    public class RigidMod : Mod
    {
        public static RigidSettings settings;

        public RigidMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<RigidSettings>();
            var harmony = new Harmony("com.helldan.rigidideology.v64");
            harmony.PatchAll();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("RigidIdeo_AllowInReform".Translate(), ref settings.allowInReform);
            listing.End();
        }

        public override string SettingsCategory() => "Rigid Ideology";
    }

    // --- 2. ПРОВЕРКА АКТИВНОСТИ ---
    public static class IdeoCheck
    {
        public static bool IsEditorActive()
        {
            if (Find.WindowStack == null) return false;
            var windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] == null) continue;
                string name = windows[i].GetType().Name;
                if (name.Contains("EditIdeology") || name.Contains("ConfigureIdeo") || name.Contains("FluidIdeo"))
                    return true;
            }
            return false;
        }
    }

    // --- 3. ЛОГИЧЕСКИЙ ПЕРЕХВАТ ---
    [HarmonyPatch(typeof(IdeoUIUtility), "DoPreceptsInt")]
    public static class Patch_DoPreceptsInt
    {
        public static void Prefix(ref IdeoEditMode editMode, bool mainPrecepts)
        {
            if (mainPrecepts)
            {
                // Если в настройках разрешено и идет реформа — выходим, давая ваниле отрисовать всё
                if ((RigidMod.settings?.allowInReform ?? false) && editMode == IdeoEditMode.Reform)
                {
                    return;
                }

                // В остальных случаях (старт игры, просмотр) — блокируем кнопки
                editMode = IdeoEditMode.None;
            }
        }
    }

    // --- 4. БЛОКИРОВКА ОКНА И ВЫПАДАЮЩИХ СПИСКОВ ---
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class Patch_WindowHandler
    {
        public static bool Prefix(Window window)
        {
            if (window == null || !IdeoCheck.IsEditorActive()) return true;

            if (window.GetType().Name == "Dialog_AddPrecept")
            {
                // Если реформа разрешена и мы в процессе — пускаем окно выбора
                if (RigidMod.settings?.allowInReform ?? false)
                {
                    foreach (var w in Find.WindowStack.Windows)
                    {
                        if (w.GetType().Name.Contains("FluidIdeo")) return true;
                    }
                }

                try {
                    var issue = Traverse.Create(window).Field("issue").GetValue<IssueDef>();
                    if (issue != null) {
                        string def = issue.defName.ToLower();
                        // Белый список (Ритуалы, Роли, Реликвии, Здания)
                        if (!def.Contains("ritual") && !def.Contains("role") && !def.Contains("relic") && !def.Contains("building"))
                            return false; 
                    }
                } catch { }
            }
            return true;
        }

        static HashSet<string> blockedPrecepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool dataCollected = false;

        public static void Postfix(Window window)
        {
            if (window == null || !(window is FloatMenu menu) || !IdeoCheck.IsEditorActive()) return;

            // Если реформа разрешена — не блокируем пункты внутри FloatMenu
            if (RigidMod.settings?.allowInReform ?? false)
            {
                foreach (var w in Find.WindowStack.Windows)
                {
                    if (w.GetType().Name.Contains("FluidIdeo")) return;
                }
            }

            if (!dataCollected)
            {
                try {
                    foreach (var def in DefDatabase<PreceptDef>.AllDefsListForReading) {
                        if (typeof(Precept_Ritual).IsAssignableFrom(def.preceptClass) || 
                            typeof(Precept_Role).IsAssignableFrom(def.preceptClass) ||
                            typeof(Precept_Building).IsAssignableFrom(def.preceptClass)) continue;
                        
                        if (!string.IsNullOrEmpty(def.LabelCap))
                            blockedPrecepts.Add(def.LabelCap.ToString().Trim());
                    }
                    dataCollected = true;
                } catch { }
            }

            var t = Traverse.Create(menu).Field("options");
            if (!t.FieldExists()) return;
            var opts = t.GetValue<List<FloatMenuOption>>();
            if (opts == null) return;

            foreach (var opt in opts) {
                string txt = opt.Label.StripTags().Trim();
                int idx = txt.IndexOfAny(new char[] { ':', '(' });
                if (idx > 0) txt = txt.Substring(0, idx).Trim();

                if (blockedPrecepts.Contains(txt)) {
                    opt.action = null;
                    var tr = Traverse.Create(opt);
                    if (tr.Field("disabled").FieldExists()) tr.Field("disabled").SetValue(true);
                    else if (tr.Field("Disabled").FieldExists()) tr.Field("Disabled").SetValue(true);
                }
            }
        }
    }
}