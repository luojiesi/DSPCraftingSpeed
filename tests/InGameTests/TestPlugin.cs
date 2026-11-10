using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CraftingSpeed.InGameTests
{
    /// <summary>
    /// Drives the real game: loads a save, exercises the mod and writes
    /// BepInEx/CraftingSpeedTests/report.txt plus screenshots, then quits.
    /// Saving is blocked for the whole session.
    /// </summary>
    [BepInPlugin("com.github.jiesiluo.assemblerspeed.tests", "Crafting Speed In-Game Tests", "0.1.0")]
    [BepInDependency(Plugin.Guid)]
    public class TestPlugin : BaseUnityPlugin
    {
        private ConfigEntry<string> saveName;
        private ConfigEntry<bool> quitWhenDone;
        private string outDir;
        private readonly StringBuilder report = new StringBuilder();
        private int passed, failed, skipped;

        private void Awake()
        {
            saveName = Config.Bind("Tests", "SaveName", "干大事6", "Save to load (without extension). It is never written to.");
            quitWhenDone = Config.Bind("Tests", "QuitWhenDone", true, "Quit the game after the tests.");
            outDir = Path.Combine(Paths.BepInExRootPath, "CraftingSpeedTests");
            Directory.CreateDirectory(outDir);
            foreach (string f in Directory.GetFiles(outDir)) File.Delete(f);

            new Harmony("com.github.jiesiluo.assemblerspeed.tests").PatchAll(typeof(NoSaving));
            Line($"Crafting Speed in-game tests, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, save '{saveName.Value}'");
            StartCoroutine(Run());
        }

        private void Update()
        {
            DSPGame.backgroundPause = false; // keep running while the window is in the background
        }

        // ------------------------------------------------------------------ never save

        private static class NoSaving
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(GameSave), nameof(GameSave.SaveCurrentGame))]
            [HarmonyPatch(typeof(GameSave), nameof(GameSave.AutoSave))]
            [HarmonyPatch(typeof(GameSave), nameof(GameSave.AutoSaveAfterErrored))]
            [HarmonyPatch(typeof(GameSave), nameof(GameSave.SaveAsLastExit))]
            private static bool Block(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        // ------------------------------------------------------------------ runner

        private IEnumerator Run()
        {
            yield return WaitFor("main menu", () => UIRoot.instance != null && GameMain.instance != null && !GameMain.isLoading
                                                    && UIRoot.instance.uiMainMenu != null && UIRoot.instance.uiMainMenu.active, 600);
            yield return new WaitForSecondsRealtime(3);

            Line($"Loading save '{saveName.Value}'");
            // Switch away from the menu's demo scene only after this frame has rendered; switching
            // mid-frame makes the game's SectorModel render a destroyed scene (harmless red error).
            yield return new WaitForEndOfFrame();
            DSPGame.StartGame(saveName.Value);
            bool loaded = false;
            yield return WaitFor("save loaded", () => loaded = GameMain.isRunning && !GameMain.isLoading && GameMain.mainPlayer != null
                                                              && GameMain.localPlanet != null && GameMain.localPlanet.factoryLoaded, 600);
            if (!loaded)
            {
                Fail("load", "save did not finish loading");
                Finish();
                yield break;
            }
            yield return new WaitForSecondsRealtime(5);
            CloseAllWindows();

            Context ctx = null;
            yield return Step("setup", () => ctx = Context.Create());
            if (ctx == null) { Finish(); yield break; }

            yield return Step("environment", () => Environment(ctx));
            yield return MeasureThrottle(ctx);
            yield return WindowUI(ctx);
            yield return Step("blueprint copy, clipboard and string", () => BlueprintCopy(ctx));
            yield return Step("prebuild handoff and slot reuse", () => PrebuildHandoff(ctx));
            yield return Step("copy / paste building settings", () => CopySettings(ctx));
            yield return Step("mod save data round trip", () => SaveRoundTrip(ctx));

            Finish();
        }

        private void Finish()
        {
            Line($"\nRESULT: {passed} passed, {failed} failed, {skipped} skipped");
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString(), Encoding.UTF8);
            Logger.LogInfo("CRAFTING SPEED TESTS DONE");
            if (quitWhenDone.Value) StartCoroutine(QuitSoon());
        }

        private static IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(2);
            Application.Quit();
        }

        // ------------------------------------------------------------------ tests

        private sealed class Context
        {
            public Player Player;
            public PlanetData Planet;
            public PlanetFactory Factory;
            public int A, B; // assembler ids (Assemble recipes), A nearest to the player

            public static Context Create()
            {
                var c = new Context { Player = GameMain.mainPlayer, Planet = GameMain.localPlanet };
                c.Factory = c.Planet.factory;
                FactorySystem fs = c.Factory.factorySystem;
                var ids = new List<int>();
                for (int i = 1; i < fs.assemblerCursor; i++)
                {
                    ref AssemblerComponent a = ref fs.assemblerPool[i];
                    if (a.id == i && a.recipeId > 0 && a.recipeType == ERecipeType.Assemble) ids.Add(i);
                }
                if (ids.Count < 2) throw new Exception($"need two assemblers with Assemble recipes on {c.Planet.displayName}, found {ids.Count}");
                Vector3 p = c.Player.position;
                ids.Sort((x, y) => (EntityPos(c, x) - p).sqrMagnitude.CompareTo((EntityPos(c, y) - p).sqrMagnitude));
                // Prefer a nearby machine that is actually working, so throttling can be measured.
                c.A = ids.Take(40).FirstOrDefault(i => fs.assemblerPool[i].replicating);
                if (c.A == 0) c.A = ids[0];
                c.B = ids.First(i => i != c.A);
                return c;
            }

            public ref AssemblerComponent Asm(int id) => ref Factory.factorySystem.assemblerPool[id];
            public int Entity(int id) => Factory.factorySystem.assemblerPool[id].entityId;
            public int Limit(int id) => SpeedLimit.Get(ref Asm(id));
            public void SetLimit(int id, int permille) => SpeedLimit.Set(ref Asm(id), permille);
            public static Vector3 EntityPos(Context c, int id) => c.Factory.entityPool[c.Factory.factorySystem.assemblerPool[id].entityId].pos;
        }

        private void Environment(Context ctx)
        {
            Check(SpeedLimit.Available, "preloader field present (AssemblerComponent.speedMultiplier)");
            var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(AssemblerComponent), nameof(AssemblerComponent.InternalUpdate)));
            Check(info != null && info.Transpilers.Any(t => t.owner == Plugin.Guid), "InternalUpdate transpiler applied");
            Check(Chainloader.PluginInfos.ContainsKey(BlueprintTweaks.BlueprintTweaksPlugin.MODGUID), "BlueprintTweaks loaded");
            Line($"  planet {ctx.Planet.displayName}, assemblers A=#{ctx.A} (recipe {ctx.Asm(ctx.A).recipeId}, replicating={ctx.Asm(ctx.A).replicating}), B=#{ctx.B}");
            Line($"  existing limits loaded from the save: A={SpeedLimit.Format(ctx.Limit(ctx.A))}, B={SpeedLimit.Format(ctx.Limit(ctx.B))}");
        }

        /// <summary>Crafting progress per game tick at 100% and 50% on a real, working machine.</summary>
        private IEnumerator MeasureThrottle(Context ctx)
        {
            Line("\n== throttle in the running game");
            double full = 0, half = 0;
            ctx.SetLimit(ctx.A, 1000);
            yield return Progress(ctx, ctx.A, v => full = v);
            ctx.SetLimit(ctx.A, 500);
            yield return Progress(ctx, ctx.A, v => half = v);
            ctx.SetLimit(ctx.A, 1000);
            if (full <= 0 || half <= 0) { Skip($"A was not crafting steadily (progress/tick: 100% {full:0}, 50% {half:0})"); yield break; }
            double ratio = half / full;
            Line($"  progress per tick: 100% {full:0}, 50% {half:0}, ratio {ratio:0.000}");
            Check(ratio > 0.45 && ratio < 0.55, "50% limit halves crafting progress in game");
        }

        private static IEnumerator Progress(Context ctx, int id, Action<double> result)
        {
            long progress = 0, ticks = 0;
            long lastTick = GameMain.gameTick;
            int lastTime = ctx.Asm(id).time;
            float end = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                long tick = GameMain.gameTick;
                int time = ctx.Asm(id).time;
                if (tick > lastTick && time > lastTime && ctx.Asm(id).replicating)
                {
                    progress += time - lastTime;
                    ticks += tick - lastTick;
                }
                lastTick = tick;
                lastTime = time;
            }
            result(ticks > 0 ? (double)progress / ticks : 0);
        }

        private IEnumerator WindowUI(Context ctx)
        {
            Line("\n== assembler window");
            Player player = ctx.Player;
            Vector3 pos = Context.EntityPos(ctx, ctx.A);
            if ((player.position - pos).magnitude > 12f) player.position = pos + pos.normalized * 2f + player.transform.right * 6f;
            yield return null;
            ctx.SetLimit(ctx.A, 1000);
            player.controller.actionInspect.SetInspectee(EObjectType.None, ctx.Entity(ctx.A));
            yield return Frames(20);

            UIAssemblerWindow w = UIRoot.instance.uiGame.assemblerWindow;
            Check(w.active && w.assemblerId == ctx.A, "assembler window opens for A");
            if (!w.active) yield break;
            Transform row = w.windowTrans.Find("crafting-speed-row");
            Check(row != null && row.gameObject.activeSelf, "speed row is shown");
            yield return Shot("window-100");

            ctx.SetLimit(ctx.A, 375);
            yield return Frames(10);
            Text[] texts = row != null ? row.GetComponentsInChildren<Text>(true) : new Text[0];
            Check(texts.Any(t => t.text == "37.5%"), "row shows 37.5%");
            Check(w.stateText.text.Contains("37.5%"), $"status line mentions the limit ('{w.stateText.text}')");
            Line($"  per-minute readout at 37.5%: '{w.speedText.text}'");
            yield return Shot("window-37.5");

            if (row != null)
            {
                Button Btn(string label) => row.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == label);
                Btn("+")?.onClick.Invoke();
                yield return Frames(3);
                Check(ctx.Limit(ctx.A) == 385, $"'+' adds 1% (now {SpeedLimit.Format(ctx.Limit(ctx.A))})");
                Btn("-")?.onClick.Invoke();
                Btn("-")?.onClick.Invoke();
                yield return Frames(3);
                Check(ctx.Limit(ctx.A) == 365, $"'-' removes 1% (now {SpeedLimit.Format(ctx.Limit(ctx.A))})");
                Btn(Localization.isZHCN ? "重置" : "Reset")?.onClick.Invoke();
                yield return Frames(3);
                Check(ctx.Limit(ctx.A) == 1000, "reset button goes back to 100%");

                InputField input = row.GetComponentInChildren<InputField>(true);
                Check(input != null, "target input exists");
                if (input != null)
                {
                    yield return Frames(3);
                    Line($"  target box at 100%: '{input.text}'");
                    if (double.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fullRate) && fullRate > 0)
                    {
                        input.text = (fullRate / 4).ToString("0.####", CultureInfo.InvariantCulture);
                        input.onEndEdit.Invoke(input.text);
                        yield return Frames(3);
                        int got = ctx.Limit(ctx.A);
                        Check(Math.Abs(got - 250) <= 2, $"target of a quarter of full output gives 25% (got {SpeedLimit.Format(got)})");
                    }
                    else Skip("target box was empty, cannot test it");
                }
                Slider slider = row.GetComponentInChildren<Slider>(true);
                if (slider != null)
                {
                    slider.value = 600;
                    yield return Frames(3);
                    Check(ctx.Limit(ctx.A) == 600, "moving the slider sets the limit");
                }
                yield return Shot("window-after-controls");

                // The handle must reach both ends of the slider and the fill must end under it.
                if (slider != null)
                {
                    var sliderRect = (RectTransform)slider.transform;
                    Rect track = sliderRect.rect;
                    float CenterX(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); return sliderRect.InverseTransformPoint((c[0] + c[2]) / 2f).x; }
                    float RightX(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); return sliderRect.InverseTransformPoint(c[2]).x; }
                    float LeftX(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); return sliderRect.InverseTransformPoint(c[0]).x; }

                    ctx.SetLimit(ctx.A, 1000);
                    yield return Frames(3);
                    float handleRight = RightX(slider.handleRect), handleMid = CenterX(slider.handleRect), fillEnd = RightX(slider.fillRect);
                    Line($"  100%: slider x {track.xMin:0.0}..{track.xMax:0.0}, handle right edge {handleRight:0.0}, centre {handleMid:0.0}, fill ends {fillEnd:0.0}");
                    Check(Mathf.Abs(handleRight - track.xMax) <= 1.5f, "at 100% the handle reaches the right end");
                    Check(Mathf.Abs(fillEnd - handleMid) <= 1.5f, "at 100% the fill ends under the handle");
                    yield return Shot("window-slider-100");

                    ctx.SetLimit(ctx.A, 1);
                    yield return Frames(3);
                    float handleLeft = LeftX(slider.handleRect);
                    Line($"  0.1%: handle left edge {handleLeft:0.0}, centre {CenterX(slider.handleRect):0.0}");
                    Check(Mathf.Abs(handleLeft - track.xMin) <= 1.5f, "at 0.1% the handle reaches the left end");
                    yield return Shot("window-slider-0.1");
                }
            }

            ctx.SetLimit(ctx.A, 1000);
            player.controller.actionInspect.SetInspectee(EObjectType.None, 0);
            CloseAllWindows();
            yield return Frames(5);
        }

        private void BlueprintCopy(Context ctx)
        {
            ctx.SetLimit(ctx.A, 375);
            ctx.SetLimit(ctx.B, 1000);
            var bp = BlueprintData.CreateNew();
            BlueprintUtils.GenerateBlueprintData(bp, ctx.Planet, ctx.Player.controller.actionBuild.planetAux,
                new[] { ctx.Entity(ctx.A), ctx.Entity(ctx.B) }, 2, 0f, null, false);
            Check(bp.buildings != null && bp.buildings.Length == 2, "blueprint generated from A and B");

            Check(LimitOf(bp, 0) == 375 && LimitOf(bp, 1) == null, "copy records A=37.5%, B unlimited");
            BlueprintData clone = bp.Clone();
            Check(clone != null && LimitOf(clone, 0) == 375 && LimitOf(clone, 1) == null, "clipboard copy (string round trip) keeps the limit");
            string str = bp.ToBase64String();
            BlueprintData fromString = BlueprintData.CreateNew(str);
            Check(fromString != null && LimitOf(fromString, 0) == 375, "blueprint string keeps the limit");
            File.WriteAllText(Path.Combine(outDir, "test-blueprint.txt"), str);
            ctx.SetLimit(ctx.A, 1000);
        }

        private static int? LimitOf(BlueprintData bp, int position)
        {
            var limits = AccessTools.Method(typeof(BlueprintSupport), "Lookup").Invoke(null, new object[] { bp });
            if (limits == null) return null;
            var map = (System.Collections.IDictionary)AccessTools.Field(limits.GetType(), "ByBuilding").GetValue(limits);
            return map.Contains(bp.buildings[position]) ? (int?)(int)map[bp.buildings[position]] : null;
        }

        private void PrebuildHandoff(Context ctx)
        {
            PlanetFactory f = ctx.Factory;
            Vector3 basePos = ctx.Player.position + ctx.Player.transform.right * 20f;
            Vector3 pos = basePos.normalized * (ctx.Planet.realRadius + 0.2f);

            int e1 = BuildAssembler(ctx, pos, 250);
            Check(e1 > 0 && SpeedLimit.GetForEntity(f, e1) == 250, $"built ghost receives its pending limit (got {Fmt(SpeedLimit.GetForEntity(f, e1))})");
            int slot = e1 > 0 ? f.entityPool[e1].assemblerId : 0;
            if (e1 > 0) f.RemoveEntityWithComponents(e1, false);

            int e2 = BuildAssembler(ctx, pos, null);
            int slot2 = e2 > 0 ? f.entityPool[e2].assemblerId : 0;
            Line($"  assembler slot first {slot}, second {slot2} (same slot = reuse case covered)");
            Check(e2 > 0 && SpeedLimit.GetForEntity(f, e2) == 1000, $"new assembler in a reused slot starts at 100% (got {Fmt(SpeedLimit.GetForEntity(f, e2))})");
            if (e2 > 0) f.RemoveEntityWithComponents(e2, false);
        }

        private static string Fmt(int? permille) => permille.HasValue ? SpeedLimit.Format(permille.Value) : "none";

        private static int BuildAssembler(Context ctx, Vector3 pos, int? pendingPermille)
        {
            PlanetFactory f = ctx.Factory;
            ItemProto proto = LDB.items.Select(2303);
            var pd = new PrebuildData
            {
                protoId = (short)proto.ID,
                modelIndex = (short)proto.prefabDesc.modelIndex,
                pos = pos, pos2 = pos,
                rot = Maths.SphericalRotation(pos, 0f), rot2 = Maths.SphericalRotation(pos, 0f),
            };
            int prebuildId = f.AddPrebuildDataWithComponents(pd);
            if (pendingPermille.HasValue) PendingSpeeds.Set(f.index, prebuildId, pendingPermille.Value);
            int before = f.entityCursor;
            f.BuildFinally(ctx.Player, prebuildId, autoRefresh: true, flattenTerrain: false);
            for (int e = 1; e < f.entityCursor; e++)
                if (f.entityPool[e].id == e && f.entityPool[e].protoId == proto.ID && (f.entityPool[e].pos - pos).sqrMagnitude < 0.01f)
                    return e;
            return 0;
        }

        private void CopySettings(Context ctx)
        {
            ctx.SetLimit(ctx.A, 420);
            ctx.SetLimit(ctx.B, 1000);
            ctx.Factory.CopyBuildingSetting(ctx.Entity(ctx.A));
            ctx.Factory.PasteBuildingSetting(ctx.Entity(ctx.B));
            Check(ctx.Limit(ctx.B) == 420, $"pasting settings copies the limit (B now {SpeedLimit.Format(ctx.Limit(ctx.B))})");
            ctx.SetLimit(ctx.A, 1000);
            ctx.SetLimit(ctx.B, 1000);
        }

        private void SaveRoundTrip(Context ctx)
        {
            var plugin = (Plugin)Chainloader.PluginInfos[Plugin.Guid].Instance;
            ctx.SetLimit(ctx.A, 123);
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true)) plugin.Export(w);
            ctx.SetLimit(ctx.A, 1000);
            ms.Position = 0;
            using (var r = new BinaryReader(ms)) plugin.Import(r);
            Check(ctx.Limit(ctx.A) == 123, $"export then import restores 12.3% (got {SpeedLimit.Format(ctx.Limit(ctx.A))})");
            ctx.SetLimit(ctx.A, 1000);
        }

        // ------------------------------------------------------------------ helpers

        private IEnumerator Step(string name, Action body)
        {
            Line($"\n== {name}");
            try { body(); }
            catch (Exception e) { Fail(name, e.ToString()); }
            yield return null;
        }

        private IEnumerator WaitFor(string what, Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok;
                try { ok = condition(); } catch { ok = false; }
                if (ok) { Line($"[{Time.realtimeSinceStartup:0}s] {what}"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Line($"[{Time.realtimeSinceStartup:0}s] TIMEOUT waiting for {what}");
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private IEnumerator Shot(string name)
        {
            string path = Path.Combine(outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return Frames(3);
            Line($"  screenshot {name}.png");
        }

        private static void CloseAllWindows()
        {
            try { UIRoot.instance.uiGame.ShutAllFunctionWindow(); } catch { }
        }

        private void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            Line((ok ? "  PASS  " : "  FAIL  ") + what);
        }

        private void Fail(string what, string why)
        {
            failed++;
            Line($"  FAIL  {what}: {why}");
        }

        private void Skip(string why)
        {
            skipped++;
            Line("  SKIP  " + why);
        }

        private void Line(string s)
        {
            report.AppendLine(s);
            Logger.LogInfo(s);
            try { File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString(), Encoding.UTF8); } catch { }
        }
    }
}
