using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// Group C - auto power poles.
    ///
    /// DSPCalculator never adds electric poles, so a generated blueprint has
    /// no power and the user hand-places poles after pasting. This patch does
    /// it automatically: it intercepts the blueprint on its way to paste mode
    /// (<c>PlayerController.OpenBlueprintPasteMode</c>, filtered to
    /// DSPCalculator's own temp blueprints) and appends power-pole buildings.
    ///
    /// <b>Auto (default) mixes pole types for full coverage:</b> it first drops
    /// Satellite Substations wherever their (large) footprint actually fits -
    /// each covers a huge radius - then fills every remaining uncovered spot
    /// with Tesla Towers, whose 1x1 footprint slots into the belt aisles a
    /// dense layout always has. So the whole blueprint ends up powered even
    /// though substations alone only fit around the open edges.
    ///
    /// Collision-safe by construction: every existing building's footprint is
    /// reserved using the game's own multi-collider build footprint
    /// (<c>PrefabDesc.buildColliders</c>) - the same data the paste collision
    /// check uses - so a pole is only placed on tiles the game also considers
    /// empty. Grid points with no free tile are skipped (counted), never forced.
    ///
    /// Spacing satisfies coverage (cell corner within the pole's supply radius)
    /// and connectivity (adjacent poles within connect distance) so the pasted
    /// poles form one network the user hooks in once.
    ///
    /// Targets a *game* type, so it uses the strongly-typed game API directly
    /// (the mod references Assembly-CSharp). Gated by
    /// <see cref="PluginConfig.AutoPowerPoles"/>.
    /// </summary>
    internal static class PowerPolePatch
    {
        // Stable DSP item IDs. Guarded at runtime by null + isPowerNode +
        // unlock checks, so a wrong/removed ID degrades to "no poles added".
        private const int TeslaTowerId = 2201;
        private const int SatelliteSubstationId = 2212;

        // DSPCalculator always hands the paster a blueprint saved under this
        // temp filename. We only augment those, never a normal player paste.
        private const string DspCalcMarker = "DSPCalcBPTemp";

        // The game powers a consumer when the squared 3D distance from the node
        // to the consumer's PLUG position is <= coverRadius^2 (PowerSystem: node
        // vs plugPos). That true reach is smaller than a naive centre-to-centre
        // circle because it measures to each building's plug/edge and includes
        // pole height. CoverSafety haircuts the radius to absorb pole height +
        // curvature; the backfill additionally subtracts each building's own
        // footprint, so even large stations at the edge stay powered.
        private const double CoverSafety = 0.85;
        private const double ConnectFactor = 0.95;

        // Dedup: OpenBlueprintPasteMode may be called again with the SAME
        // instance; each regenerate builds a fresh BlueprintData, so keying on
        // instance identity augments every new result exactly once.
        private static readonly ConditionalWeakTable<BlueprintData, object> _processed =
            new ConditionalWeakTable<BlueprintData, object>();
        private static readonly object Marker = new object();

        // itemId -> unrotated build-footprint half-extent in tiles {hx, hy}.
        private static readonly Dictionary<int, int[]> _footCache = new Dictionary<int, int[]>();
        // itemId -> pole supply radius (tiles).
        private static readonly Dictionary<int, float> _coverCache = new Dictionary<int, float>();
        // Spatial-hash bucket size for the coverage backfill. Must exceed the
        // largest pole supply radius (~26.5) so a covering pole is at most one
        // bucket away from any building it covers.
        private const int Bucket = 32;

        private static bool _diagLogged;

        public static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(PlayerController), "OpenBlueprintPasteMode",
                new[] { typeof(BlueprintData), typeof(string), typeof(bool) });
            if (target == null)
            {
                DSPCalculatorPlusLog.Warn("[poles] PlayerController.OpenBlueprintPasteMode(BlueprintData,string,bool) not found - power-pole feature disabled.");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PowerPolePatch), nameof(PastePrefix)));
            DSPCalculatorPlusLog.Info("[poles] Group C (auto power poles) patch installed on PlayerController.OpenBlueprintPasteMode.");
        }

        // Harmony binds by parameter name to the original ('blueprint','fullPath').
        private static void PastePrefix(BlueprintData blueprint, string fullPath)
        {
            try
            {
                var mode = Plugin.Config.AutoPowerPoles.Value;
                if (mode == PowerPoleType.Off) return;
                if (blueprint == null || blueprint.buildings == null || blueprint.buildings.Length == 0) return;
                if (string.IsNullOrEmpty(fullPath) || fullPath.IndexOf(DspCalcMarker, StringComparison.OrdinalIgnoreCase) < 0)
                    return; // not a DSPCalculator blueprint - leave every other paste untouched
                object existing;
                if (_processed.TryGetValue(blueprint, out existing)) return; // already augmented this instance
                _processed.Add(blueprint, Marker);

                InjectPoles(blueprint, mode);
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Error("[poles] injection failed (blueprint pasted without added poles): " + ex);
            }
        }

        private static void InjectPoles(BlueprintData bp, PowerPoleType mode)
        {
            LogPoleDiagnosticOnce();

            bool useSat = (mode == PowerPoleType.SatelliteSubstation) && IsUsable(SatelliteSubstationId);
            // Tesla always does the grid-fill + backfill (cheap, 1x1, fits
            // anywhere), so coverage is guaranteed in BOTH modes; substations
            // only ADD wide coverage where they fit. Fall back to substation-only
            // if Tesla somehow isn't usable.
            bool useTesla = IsUsable(TeslaTowerId);

            if (!useSat && !useTesla)
            {
                DSPCalculatorPlusLog.Warn("[poles] no usable pole for mode " + mode + " (item unavailable or not unlocked) - none added. "
                    + "Unlock the pole, pick a different AutoPowerPoles value, or set it to Off.");
                return;
            }

            BlueprintBuilding[] buildings = bp.buildings;
            int startCount = buildings.Length;

            // 1. Reserve every existing footprint + measure the machine bbox.
            var occupied = new HashSet<long>();
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < startCount; i++)
            {
                BlueprintBuilding b = buildings[i];
                ReserveFootprint(occupied, b);
                AccumulateBounds(b, ref minX, ref maxX, ref minY, ref maxY);
            }
            if (minX > maxX || minY > maxY) return;

            int bbMinX = (int)Math.Floor(minX), bbMaxX = (int)Math.Ceiling(maxX);
            int bbMinY = (int)Math.Floor(minY), bbMaxY = (int)Math.Ceiling(maxY);

            var poles = new List<BlueprintBuilding>();
            var satCX = new List<int>();
            var satCY = new List<int>();
            float satCover = 0f;
            int satGaps = 0, teslaGaps = 0;
            int satPlaced = 0, teslaPlaced = 0;
            // Enforces DSP's minimum inter-pole distance (nodes closer than 3.5
            // tiles are "PowerTooClose" and won't place) across ALL poles.
            var field = new PoleField();

            // 2. Satellite pass - wide coverage where the big footprint fits.
            //    Records each placed centre so the tesla pass can skip it.
            if (useSat)
            {
                ItemProto satProto = LDB.items.Select(SatelliteSubstationId);
                PrefabDesc spd = satProto.prefabDesc;
                satCover = spd.powerCoverRadius;
                satGaps = PlaceGrid(SatelliteSubstationId, spd, occupied, startCount, poles, field,
                          bbMinX, bbMaxX, bbMinY, bbMaxY,
                          recordCX: satCX, recordCY: satCY,
                          skipCX: null, skipCY: null, skipRadius: 0f);
                satPlaced = poles.Count;
            }

            // 3. Tesla pass - fill everything the satellites don't already cover.
            if (useTesla)
            {
                ItemProto teslaProto = LDB.items.Select(TeslaTowerId);
                PrefabDesc tpd = teslaProto.prefabDesc;
                teslaGaps = PlaceGrid(TeslaTowerId, tpd, occupied, startCount, poles, field,
                          bbMinX, bbMaxX, bbMinY, bbMaxY,
                          recordCX: null, recordCY: null,
                          skipCX: satCX, skipCY: satCY, skipRadius: (float)(satCover * CoverSafety));
                teslaPlaced = poles.Count - satPlaced;
            }

            // 4. Coverage backfill - GUARANTEE every building is within a pole's
            //    radius. The grid can miss spots (blocked cells, coverage-skip
            //    edges); this walks every building and drops a fill pole near any
            //    that no placed pole covers. Fill pole = Tesla for Auto/Tesla
            //    (1x1, fits anywhere), Substation for substation-only mode.
            int fillPoleId = (mode == PowerPoleType.SatelliteSubstation)
                ? SatelliteSubstationId
                : (useTesla ? TeslaTowerId : SatelliteSubstationId);
            int backfilled = 0, uncovered = 0;
            if (IsUsable(fillPoleId))
                backfilled = BackfillCoverage(buildings, startCount, occupied, poles, startCount, field,
                                              fillPoleId, bbMinX, bbMaxX, bbMinY, bbMaxY, out uncovered);

            if (poles.Count == 0)
            {
                DSPCalculatorPlusLog.Warn("[poles] no free tiles for poles in this blueprint - none added "
                    + "(machines packed with no gaps). Power it manually, or set AutoPowerPoles=Off to silence.");
                return;
            }

            // 5. Append to the blueprint building array.
            var merged = new BlueprintBuilding[startCount + poles.Count];
            Array.Copy(buildings, merged, startCount);
            for (int i = 0; i < poles.Count; i++) merged[startCount + i] = poles[i];
            bp.buildings = merged;
            ExpandArea(bp, poles);

            string fillName = fillPoleId == SatelliteSubstationId ? "Substation" : "Tesla";
            DSPCalculatorPlusLog.Info("[poles] mode=" + mode + " added " + poles.Count + " pole(s) over " + startCount
                + " buildings: " + satPlaced + " Satellite Substation + " + teslaPlaced + " Tesla Tower (grid)"
                + (backfilled > 0 ? " + " + backfilled + " " + fillName + " (backfill)" : "")
                + (uncovered > 0 ? "; " + uncovered + " machine area(s) still UNCOVERED (no free tile within range)" : "; full coverage")
                + ".");
            if (uncovered > 0)
                DSPCalculatorPlusLog.Warn("[poles] " + uncovered + " machine area(s) had no free tile for even a 1x1 pole "
                    + "(extremely dense layout) - those spots need manual power.");
        }

        // ---- grid placement ------------------------------------------------

        /// <summary>
        /// Places poles of one type on a coverage grid across the bbox, each
        /// snapped to the nearest free tile; returns the number of grid points
        /// that had no free tile. Placed centres are appended to
        /// <paramref name="recordCX"/>/<paramref name="recordCY"/> when given.
        /// When <paramref name="skipCX"/>/<paramref name="skipCY"/> +
        /// <paramref name="skipRadius"/> are supplied, grid points safely inside
        /// one of those existing poles' radius are skipped, so a fill pass only
        /// adds poles where coverage is actually missing.
        /// </summary>
        private static int PlaceGrid(int poleId, PrefabDesc pd, HashSet<long> occupied, int indexBase,
            List<BlueprintBuilding> outPoles, PoleField field, int bbMinX, int bbMaxX, int bbMinY, int bbMaxY,
            List<int> recordCX, List<int> recordCY,
            List<int> skipCX, List<int> skipCY, float skipRadius)
        {
            float cover = pd.powerCoverRadius;
            float connect = pd.powerConnectDistance;
            if (cover <= 0.1f) return 0;

            // Place poles so every cell corner is within the (haircut) reach:
            // corner distance = spacing/sqrt(2), so spacing = effReach*sqrt(2).
            double sEff = cover * CoverSafety * 1.41421356;
            if (connect > 0.1f) sEff = Math.Min(sEff, connect * ConnectFactor);
            if (sEff < 1.0) sEff = 1.0;

            int spanX = Math.Max(0, bbMaxX - bbMinX);
            int spanY = Math.Max(0, bbMaxY - bbMinY);
            int nx = Math.Max(1, (int)Math.Ceiling(spanX / sEff));
            int ny = Math.Max(1, (int)Math.Ceiling(spanY / sEff));
            double cellW = spanX / (double)nx;
            double cellH = spanY / (double)ny;
            // Search out to the pole's own coverage radius: a pole placed that
            // far from the ideal grid point still covers that point's cell, so
            // we drift into an aisle/border rather than giving up (which left
            // uncovered spots). Backfill (below) then guarantees the rest.
            int searchR = Math.Max((int)Math.Ceiling(Math.Max(cellW, cellH) * 0.6), (int)Math.Floor(cover));

            int hx, hy;
            GetTileHalfExtents(pd, poleId, 0f, out hx, out hy);

            // Skip a fill point only when it is safely (minus ~one cell) inside a
            // recorded pole's radius, so no edge tile is left unpowered.
            bool doSkip = skipCX != null && skipCX.Count > 0 && skipRadius > 0.1f;
            double skipR2 = 0.0;
            if (doSkip)
            {
                double safe = skipRadius - Math.Max(cellW, cellH) * 0.6;
                if (safe < 0) safe = 0;
                skipR2 = safe * safe;
            }

            int gaps = 0;
            for (int ix = 0; ix < nx; ix++)
            {
                int gx = bbMinX + (int)Math.Round((ix + 0.5) * cellW);
                for (int iy = 0; iy < ny; iy++)
                {
                    int gy = bbMinY + (int)Math.Round((iy + 0.5) * cellH);

                    if (doSkip && IsCovered(gx, gy, skipCX, skipCY, skipR2)) continue;

                    int px, py;
                    if (FindFreeTile(occupied, field, gx, gy, searchR, hx, hy, bbMinX, bbMaxX, bbMinY, bbMaxY, out px, out py))
                    {
                        outPoles.Add(MakePole(poleId, pd.modelIndex, px, py, indexBase + outPoles.Count));
                        MarkOccupied(occupied, px, py, hx, hy);
                        field.Add(px, py);
                        if (recordCX != null && recordCY != null) { recordCX.Add(px); recordCY.Add(py); }
                    }
                    else gaps++;
                }
            }
            return gaps;
        }

        private static bool IsCovered(int x, int y, List<int> cx, List<int> cy, double r2)
        {
            if (r2 <= 0.0) return false;
            for (int i = 0; i < cx.Count; i++)
            {
                double dx = x - cx[i];
                double dy = y - cy[i];
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        /// <summary>
        /// Walks every building and drops a fill pole near any that no
        /// already-placed pole covers, so coverage is total. Uses a spatial
        /// hash of placed poles so the per-building check stays cheap even at
        /// 25k+ buildings. Returns the number of fill poles added;
        /// <paramref name="trulyUncovered"/> counts machine spots where not
        /// even a fill pole could fit within range.
        /// </summary>
        private static int BackfillCoverage(BlueprintBuilding[] buildings, int nBuildings, HashSet<long> occupied,
            List<BlueprintBuilding> poles, int startCount, PoleField field, int fillPoleId,
            int bbMinX, int bbMaxX, int bbMinY, int bbMaxY, out int trulyUncovered)
        {
            trulyUncovered = 0;
            ItemProto fp = LDB.items.Select(fillPoleId);
            if (fp == null || fp.prefabDesc == null) return 0;
            PrefabDesc fpd = fp.prefabDesc;
            float fillCover = fpd.powerCoverRadius;
            if (fillCover <= 0.1f) return 0;
            int fhx, fhy; GetTileHalfExtents(fpd, fillPoleId, 0f, out fhx, out fhy);
            float fillEff = (float)(fillCover * CoverSafety);
            // Keep the fill pole close enough that the uncovered building is
            // comfortably inside its reach (even a large station's footprint).
            int searchR = Math.Max(2, (int)Math.Floor(fillEff * 0.6));

            // Spatial hash of already-placed (grid) poles, keyed by effective reach.
            var spatial = new Dictionary<long, List<int>>();
            var poleX = new List<int>();
            var poleY = new List<int>();
            var poleCov = new List<float>();
            for (int i = 0; i < poles.Count; i++)
            {
                BlueprintBuilding p = poles[i];
                AddSpatial(spatial, poleX, poleY, poleCov, (int)p.localOffset_x, (int)p.localOffset_y, EffCoverOf(p.itemId));
            }

            // Collect unique uncovered building centre tiles (a building is
            // covered only if its centre is within reach MINUS its own footprint,
            // approximating the game's node->plug distance for big buildings).
            var seen = new HashSet<long>();
            var todo = new List<int>(); // flattened x,y,bHalf triples
            for (int i = 0; i < nBuildings; i++)
            {
                BlueprintBuilding b = buildings[i];
                int x = (int)Math.Round(b.localOffset_x), y = (int)Math.Round(b.localOffset_y);
                long k = Key(x, y);
                if (!seen.Add(k)) continue;
                int bHalf = BuildingHalf(b);
                if (CoveredBy(spatial, poleX, poleY, poleCov, x, y, bHalf)) continue;
                todo.Add(x); todo.Add(y); todo.Add(bHalf);
            }

            int added = 0;
            for (int i = 0; i < todo.Count; i += 3)
            {
                int x = todo[i], y = todo[i + 1], bHalf = todo[i + 2];
                if (CoveredBy(spatial, poleX, poleY, poleCov, x, y, bHalf)) continue; // covered by a backfill pole added meanwhile
                int px, py;
                if (FindFreeTile(occupied, field, x, y, searchR, fhx, fhy, bbMinX, bbMaxX, bbMinY, bbMaxY, out px, out py))
                {
                    poles.Add(MakePole(fillPoleId, fpd.modelIndex, px, py, startCount + poles.Count));
                    MarkOccupied(occupied, px, py, fhx, fhy);
                    AddSpatial(spatial, poleX, poleY, poleCov, px, py, fillEff);
                    field.Add(px, py);
                    added++;
                }
                else trulyUncovered++;
            }
            return added;
        }

        private static int BuildingHalf(BlueprintBuilding b)
        {
            ItemProto proto = LDB.items.Select(b.itemId);
            if (proto == null || proto.prefabDesc == null) return 0;
            int hx, hy; GetTileHalfExtents(proto.prefabDesc, b.itemId, b.yaw, out hx, out hy);
            return Math.Max(hx, hy);
        }

        private static void AddSpatial(Dictionary<long, List<int>> spatial, List<int> poleX, List<int> poleY,
            List<float> poleCov, int x, int y, float cov)
        {
            int idx = poleX.Count;
            poleX.Add(x); poleY.Add(y); poleCov.Add(cov);
            long bk = Key(FloorDiv(x, Bucket), FloorDiv(y, Bucket));
            List<int> l;
            if (!spatial.TryGetValue(bk, out l)) { l = new List<int>(); spatial[bk] = l; }
            l.Add(idx);
        }

        // Covered if the building centre is within (pole effective reach - the
        // building's own footprint half-extent) of some pole.
        private static bool CoveredBy(Dictionary<long, List<int>> spatial, List<int> poleX, List<int> poleY,
            List<float> poleCov, int x, int y, int bHalf)
        {
            int bx = FloorDiv(x, Bucket), by = FloorDiv(y, Bucket);
            for (int dbx = -1; dbx <= 1; dbx++)
                for (int dby = -1; dby <= 1; dby++)
                {
                    List<int> l;
                    if (!spatial.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                    foreach (int i in l)
                    {
                        double effR = poleCov[i] - bHalf;
                        if (effR <= 0) continue;
                        double dx = x - poleX[i], dy = y - poleY[i];
                        if (dx * dx + dy * dy <= effR * effR) return true;
                    }
                }
            return false;
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        private static float EffCoverOf(int itemId)
        {
            float v;
            if (_coverCache.TryGetValue(itemId, out v)) return v;
            ItemProto p = LDB.items.Select(itemId);
            float raw = (p != null && p.prefabDesc != null) ? p.prefabDesc.powerCoverRadius : 0f;
            v = (float)(raw * CoverSafety);
            _coverCache[itemId] = v;
            return v;
        }

        // ---- pole selection ------------------------------------------------

        private static bool IsUsable(int itemId)
        {
            ItemProto proto = LDB.items.Select(itemId);
            if (proto == null || proto.prefabDesc == null || !proto.prefabDesc.isPowerNode) return false;
            if (GameMain.history != null && !GameMain.history.ItemUnlocked(itemId)) return false;
            return true;
        }

        // ---- geometry helpers ----------------------------------------------

        /// <summary>Half-extent, in whole tiles, a building reserves each side
        /// of its centre, from the game's real build footprint. Rotated
        /// 90/270 swaps axes. Cached per item.</summary>
        private static void GetTileHalfExtents(PrefabDesc pd, int itemId, float yaw, out int hx, out int hy)
        {
            int[] baseHalf;
            if (!_footCache.TryGetValue(itemId, out baseHalf))
            {
                float ex, ez;
                MeasureFootprint(pd, out ex, out ez);
                baseHalf = new[]
                {
                    Math.Max(0, (int)Math.Floor(ex + 0.5f - 1e-3f)),
                    Math.Max(0, (int)Math.Floor(ez + 0.5f - 1e-3f)),
                };
                _footCache[itemId] = baseHalf;
            }
            hx = baseHalf[0];
            hy = baseHalf[1];
            int q = ((int)Math.Round(yaw / 90f)) & 3;
            if (q == 1 || q == 3) { int t = hx; hx = hy; hy = t; }
        }

        /// <summary>Horizontal half-extents (world units ~ tiles) of a
        /// building's footprint, from all its build colliders - the same data
        /// the game's paste-collision check uses. Falls back to the single
        /// build collider, then to a 1x1 tile.</summary>
        private static void MeasureFootprint(PrefabDesc pd, out float ex, out float ez)
        {
            ex = 0f; ez = 0f;
            ColliderData[] bcs = pd.buildColliders;
            if (bcs != null)
            {
                for (int i = 0; i < bcs.Length; i++)
                {
                    ColliderData c = bcs[i];
                    float rx = Math.Abs(c.pos.x) + Math.Max(c.ext.x, c.radius);
                    float rz = Math.Abs(c.pos.z) + Math.Max(c.ext.z, c.radius);
                    if (rx > ex) ex = rx;
                    if (rz > ez) ez = rz;
                }
            }
            if (ex < 0.1f && ez < 0.1f)
            {
                ex = Math.Max(pd.buildCollider.ext.x, pd.buildCollider.radius);
                ez = Math.Max(pd.buildCollider.ext.z, pd.buildCollider.radius);
            }
            if (ex < 0.1f && ez < 0.1f) { ex = 0.5f; ez = 0.5f; }
        }

        private static void ReserveFootprint(HashSet<long> occupied, BlueprintBuilding b)
        {
            int hx = 0, hy = 0;
            ItemProto proto = LDB.items.Select(b.itemId);
            if (proto != null && proto.prefabDesc != null)
                GetTileHalfExtents(proto.prefabDesc, b.itemId, b.yaw, out hx, out hy);

            int cx = (int)Math.Round(b.localOffset_x);
            int cy = (int)Math.Round(b.localOffset_y);
            MarkOccupied(occupied, cx, cy, hx, hy);
            int x2 = (int)Math.Round(b.localOffset_x2);
            int y2 = (int)Math.Round(b.localOffset_y2);
            if (x2 != cx || y2 != cy) MarkOccupied(occupied, x2, y2, hx, hy);
        }

        private static void AccumulateBounds(BlueprintBuilding b, ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            float lx = Math.Min(b.localOffset_x, b.localOffset_x2);
            float hx = Math.Max(b.localOffset_x, b.localOffset_x2);
            float ly = Math.Min(b.localOffset_y, b.localOffset_y2);
            float hy = Math.Max(b.localOffset_y, b.localOffset_y2);
            if (lx < minX) minX = lx;
            if (hx > maxX) maxX = hx;
            if (ly < minY) minY = ly;
            if (hy > maxY) maxY = hy;
        }

        private static bool FindFreeTile(HashSet<long> occupied, PoleField field, int gx, int gy, int searchR,
            int phx, int phy, int clampMinX, int clampMaxX, int clampMinY, int clampMaxY,
            out int px, out int py)
        {
            for (int r = 0; r <= searchR; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; // ring only
                        int x = gx + dx, y = gy + dy;
                        if (x < clampMinX || x > clampMaxX || y < clampMinY || y > clampMaxY) continue;
                        // Free of buildings AND >= DSP's minimum inter-pole distance.
                        if (IsAreaFree(occupied, x, y, phx, phy) && !field.TooClose(x, y)) { px = x; py = y; return true; }
                    }
                }
            }
            px = gx; py = gy;
            return false;
        }

        private static bool IsAreaFree(HashSet<long> occupied, int cx, int cy, int hx, int hy)
        {
            for (int dx = -hx; dx <= hx; dx++)
                for (int dy = -hy; dy <= hy; dy++)
                    if (occupied.Contains(Key(cx + dx, cy + dy))) return false;
            return true;
        }

        private static void MarkOccupied(HashSet<long> occupied, int cx, int cy, int hx, int hy)
        {
            for (int dx = -hx; dx <= hx; dx++)
                for (int dy = -hy; dy <= hy; dy++)
                    occupied.Add(Key(cx + dx, cy + dy));
        }

        private static BlueprintBuilding MakePole(int poleId, int modelIndex, int x, int y, int index)
        {
            var b = new BlueprintBuilding();
            b.index = index;
            b.areaIndex = 0;
            b.localOffset_x = x;
            b.localOffset_y = y;
            b.localOffset_z = 0f;
            b.localOffset_x2 = x;
            b.localOffset_y2 = y;
            b.localOffset_z2 = 0f;
            b.yaw = 0f;
            b.yaw2 = 0f;
            b.itemId = (short)poleId;
            b.modelIndex = (short)modelIndex;
            b.parameters = new int[0];
            return b;
        }

        private static void ExpandArea(BlueprintData bp, List<BlueprintBuilding> poles)
        {
            int maxX = 0, maxY = 0;
            foreach (BlueprintBuilding p in poles)
            {
                if (p.localOffset_x + 1 > maxX) maxX = (int)p.localOffset_x + 1;
                if (p.localOffset_y + 1 > maxY) maxY = (int)p.localOffset_y + 1;
            }
            if (maxX > bp.dragBoxSize_x) bp.dragBoxSize_x = maxX;
            if (maxY > bp.dragBoxSize_y) bp.dragBoxSize_y = maxY;
            if (bp.areas != null && bp.areas.Length > 0)
            {
                if (bp.dragBoxSize_x > bp.areas[0].width) bp.areas[0].width = bp.dragBoxSize_x;
                if (bp.dragBoxSize_y > bp.areas[0].height) bp.areas[0].height = bp.dragBoxSize_y;
            }
        }

        private static void LogPoleDiagnosticOnce()
        {
            if (_diagLogged) return;
            _diagLogged = true;
            LogPole("Satellite Substation", SatelliteSubstationId);
            LogPole("Tesla Tower", TeslaTowerId);
        }

        private static void LogPole(string label, int itemId)
        {
            ItemProto proto = LDB.items.Select(itemId);
            if (proto == null || proto.prefabDesc == null)
            {
                DSPCalculatorPlusLog.Info("[poles][diag] " + label + " (item " + itemId + ") proto not found.");
                return;
            }
            PrefabDesc pd = proto.prefabDesc;
            int hx, hy;
            GetTileHalfExtents(pd, itemId, 0f, out hx, out hy);
            bool unlocked = GameMain.history == null || GameMain.history.ItemUnlocked(itemId);
            DSPCalculatorPlusLog.Info("[poles][diag] " + label + " (item " + itemId + "): powerNode=" + pd.isPowerNode
                + " cover=" + pd.powerCoverRadius.ToString("0.#") + " connect=" + pd.powerConnectDistance.ToString("0.#")
                + " footprint=" + (2 * hx + 1) + "x" + (2 * hy + 1) + " unlocked=" + unlocked + ".");
        }

        private static long Key(int x, int y)
        {
            return ((long)(x + 1000000)) * 4000000L + (y + 1000000);
        }

        /// <summary>
        /// Tracks placed pole positions and enforces DSP's minimum inter-pole
        /// distance: the game marks a non-generator power node "PowerTooClose"
        /// (unbuildable) when another node is within sqrt(12.25)=3.5 tiles
        /// (BuildTool_Click). We require >= sqrt(13) with a small margin for the
        /// node's power-point offset. Spatial-hashed so the check stays cheap.
        /// </summary>
        private sealed class PoleField
        {
            private const double MinDist2 = 13.0;
            private const int Bkt = 4; // >= sqrt(MinDist2), so a too-close pole is within one bucket
            private readonly Dictionary<long, List<int>> _b = new Dictionary<long, List<int>>();
            private readonly List<int> _x = new List<int>();
            private readonly List<int> _y = new List<int>();

            public void Add(int x, int y)
            {
                int idx = _x.Count;
                _x.Add(x); _y.Add(y);
                long k = Key(FloorDiv(x, Bkt), FloorDiv(y, Bkt));
                List<int> l;
                if (!_b.TryGetValue(k, out l)) { l = new List<int>(); _b[k] = l; }
                l.Add(idx);
            }

            public bool TooClose(int x, int y)
            {
                int bx = FloorDiv(x, Bkt), by = FloorDiv(y, Bkt);
                for (int dbx = -1; dbx <= 1; dbx++)
                    for (int dby = -1; dby <= 1; dby++)
                    {
                        List<int> l;
                        if (!_b.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                        foreach (int i in l)
                        {
                            double dx = x - _x[i], dy = y - _y[i];
                            if (dx * dx + dy * dy < MinDist2) return true;
                        }
                    }
                return false;
            }
        }
    }
}
