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
        private const int WirelessPowerTowerId = 2202;
        private const int SatelliteSubstationId = 2212;
        // Any conveyor belt (all tiers share collider geometry) - used to measure how
        // far a belt's underside sits below its altitude, for the under-belt check.
        private const int ConveyorBeltId = 2003;

        // DSPCalculator always hands the paster a blueprint saved under this
        // temp filename. We only augment those, never a normal player paste.
        private const string DspCalcMarker = "DSPCalcBPTemp";

        // GROUND TRUTH (decompiled PowerSystem.cs): a consumer is powered when the
        // squared 3D distance from the node to the consumer's plugPos is
        // <= coverRadius^2, and plugPos is the building's ENTITY CENTRE
        // (entityPool[id].pos; for stacked buildings it rises to the top tier but
        // stays at the same x/z). There is NO footprint subtraction - a big machine
        // is powered by a pole up to the FULL coverRadius from its centre, exactly
        // like a 1x1 sorter. So coverage is a plain centre-to-centre circle of
        // radius coverRadius; we only apply a small safety haircut for the
        // tile<->sphere approximation. (An earlier version subtracted each
        // building's half-extent, which over-reported big machines as uncovered and
        // shrank their fill-search so far that no tile fit - removed.)
        // Two safety factors, for two DIFFERENT jobs:
        //  - GridSafety: how tightly poles are PLACED. Tight = dense = best real
        //    coverage. Cell corners land well inside a pole's reach.
        //  - CoverSafety: the reach used by the PLACEMENT backfill when deciding a
        //    spot still needs a pole. Kept CONSERVATIVE (0.85) on purpose: a
        //    conservative check makes the backfill densify aggressively, which is
        //    what yields the best REAL in-game coverage (raising it made the
        //    backfill lazy and real gaps grew).
        // RescueCoverSafety is the ACCURATE reach (0.98 of the real radius), used
        // only to (a) count the honest remaining gaps and (b) aim the satellite
        // rescue at genuine gaps, so it never drops a substation on a machine the
        // teslas already power.
        private const double GridSafety = 0.85;
        private const double CoverSafety = 0.85;
        private const double RescueCoverSafety = 0.98;
        private const double ConnectFactor = 0.95;
        // How far (tiles) a candidate pole tile may sit from the nearest REAL
        // building footprint and still be trusted. Blueprint data has no terrain
        // info, so a free-tile search that drifts this far from anything the
        // player actually built is drifting into untested ground - on a lava/water
        // planet that is often unbuildable, and the game silently drops the pole at
        // paste while our own math still counts it as covering everything nearby.
        private const int BuiltGroundRadius = 3;

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
        // itemId -> does this building consume power (needs to be within a pole's
        // supply area)? Belts do NOT; sorters/machines/stations/labs do.
        private static readonly Dictionary<int, bool> _consumerCache = new Dictionary<int, bool>();
        // itemId -> is this building itself a power node (pole/generator)? Used to
        // seed the min-distance field from poles already in the blueprint.
        private static readonly Dictionary<int, bool> _nodeCache = new Dictionary<int, bool>();
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

            // Tiles occupied ONLY by a belt raised above a pole's collision top are
            // actually buildable under the belt (belt crossings), so we skip
            // reserving them - reclaiming a lot of space in dense blueprints. The
            // threshold is the belt ALTITUDE at which its UNDERSIDE clears the pole
            // collider top: the game (BuildTool_Click) rejects a pole whose build
            // collider box overlaps the belt's, so it is not enough for the belt
            // CENTRE to sit at the pole top - the belt's half-thickness must clear it
            // too. Freeing at just the pole top (the old bug) let poles be placed
            // under belts the game then refused on paste, punching coverage holes.
            float poleTop = PoleTopHeight(TeslaTowerId);
            float beltUnder = BeltUnderClearance();
            float clearanceZ = Plugin.Config.PolesUnderRaisedBelts.Value ? (poleTop + beltUnder) : float.PositiveInfinity;

            // Enforces DSP's minimum inter-pole distance (nodes closer than 3.5
            // tiles are "PowerTooClose" and won't place) across ALL poles. Seeded
            // below with any power node ALREADY present in the blueprint, so a
            // re-paste / cloned blueprint (which the per-instance guard can miss)
            // never drops a new pole on top of an existing one - the only path
            // that produces stacked/duplicate poles.
            var field = new PoleField();

            // 1. Reserve every existing footprint + measure the machine bbox.
            var occupied = new HashSet<long>();
            var elevatedTiles = new HashSet<long>();
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            int skippedElevated = 0;
            for (int i = 0; i < startCount; i++)
            {
                BlueprintBuilding b = buildings[i];
                ReserveFootprint(occupied, b, clearanceZ, ref skippedElevated, elevatedTiles);
                AccumulateBounds(b, ref minX, ref maxX, ref minY, ref maxY);
                if (IsPowerNodeItem(b.itemId))
                    field.Add((int)Math.Round(b.localOffset_x), (int)Math.Round(b.localOffset_y));
            }
            // Tiles freed under a raised belt AND not reserved by any other building -
            // a pole landing here sits under a belt, the case the game most often
            // refuses on paste. Used only by the coverage diagnostic below.
            var underBelt = new HashSet<long>();
            foreach (long t in elevatedTiles) if (!occupied.Contains(t)) underBelt.Add(t);
            if (minX > maxX || minY > maxY) return;

            // Snapshot of REAL building footprints only, taken before any pole is
            // added. Blueprint data carries no terrain info at all - lava, water and
            // cliffs are all invisible to it - so the one thing we actually know is
            // buildable is ground the player already built on. Used to keep a
            // drifted free-tile search from wandering onto untested (possibly
            // unbuildable) terrain; must be a separate snapshot, not the live
            // `occupied` set, since that set also absorbs newly placed poles and a
            // chain of backfill poles could otherwise "confirm" each other's drift
            // into open terrain without ever being near a real building.
            var builtGround = new HashSet<long>(occupied);
            if (skippedElevated > 0)
                DSPCalculatorPlusLog.Info("[poles] freed " + skippedElevated + " tile(s) under raised belts for pole placement "
                    + "(poleTop=" + poleTop.ToString("0.##") + " + beltUnderside=" + beltUnder.ToString("0.##")
                    + " => belt must be at z>=" + clearanceZ.ToString("0.##") + " to build a pole under it).");

            int bbMinX = (int)Math.Floor(minX), bbMaxX = (int)Math.Ceiling(maxX);
            int bbMinY = (int)Math.Floor(minY), bbMaxY = (int)Math.Ceiling(maxY);

            // Spatial hash of every power CONSUMER, so placement can refuse to drop a
            // pole where nothing it powers is in range. Belts loop out past the
            // machines into empty margins; without this the layout dots poles through
            // that empty space just to "cover" a stray belt (redundant, powers
            // nothing - the towers standing in the lava the user spotted).
            var consHash = new Dictionary<long, List<int>>();
            var consX = new List<int>();
            var consY = new List<int>();
            for (int i = 0; i < startCount; i++)
            {
                BlueprintBuilding b = buildings[i];
                if (!IsConsumer(b.itemId)) continue;
                int cx = (int)Math.Round(b.localOffset_x), cy = (int)Math.Round(b.localOffset_y);
                int idx = consX.Count; consX.Add(cx); consY.Add(cy);
                AddIdx(consHash, Key(FloorDiv(cx, Bucket), FloorDiv(cy, Bucket)), idx);
            }

            var poles = new List<BlueprintBuilding>();
            var satCX = new List<int>();
            var satCY = new List<int>();
            float satCover = 0f;
            int satGaps = 0, teslaGaps = 0;
            int satPlaced = 0, teslaPlaced = 0;

            // 2. Satellite pass - wide coverage where the big footprint fits.
            //    Records each placed centre so the tesla pass can skip it.
            if (useSat)
            {
                ItemProto satProto = LDB.items.Select(SatelliteSubstationId);
                PrefabDesc spd = satProto.prefabDesc;
                satCover = spd.powerCoverRadius;
                satGaps = PlaceGrid(SatelliteSubstationId, spd, occupied, builtGround, startCount, poles, field, underBelt,
                          bbMinX, bbMaxX, bbMinY, bbMaxY,
                          recordCX: satCX, recordCY: satCY,
                          skipCX: null, skipCY: null, skipRadius: 0f);
                satPlaced = poles.Count;
            }

            // 3. Tesla pass. In pure-Tesla mode, lay one regular pole row down each
            //    detected machine line (even, one-per-line, how a player does it by
            //    hand) instead of a drifting uniform grid. Falls back to the grid if
            //    the layout has no clear banding, or when substations are also in use
            //    (there the grid's satellite-skip still applies).
            if (useTesla)
            {
                ItemProto teslaProto = LDB.items.Select(TeslaTowerId);
                PrefabDesc tpd = teslaProto.prefabDesc;
                bool crossIsY; List<int> bands;
                if (!useSat && DetectBands(buildings, startCount, bbMinX, bbMaxX, bbMinY, bbMaxY, out crossIsY, out bands))
                {
                    teslaGaps = PlaceLineAligned(TeslaTowerId, tpd, occupied, builtGround, startCount, poles, field, underBelt,
                              consHash, consX, consY, crossIsY, bands,
                              crossIsY ? bbMinX : bbMinY, crossIsY ? bbMaxX : bbMaxY,   // line-axis bounds
                              crossIsY ? bbMinY : bbMinX, crossIsY ? bbMaxY : bbMaxX);  // cross-axis bounds
                    DSPCalculatorPlusLog.Info("[poles] line-aligned placement: rows run along " + (crossIsY ? "X" : "Y")
                        + " (" + bands.Count + " global block-line(s); machine rows detected LOCALLY per line-strip); one pole per machine line.");
                }
                else
                {
                    teslaGaps = PlaceGrid(TeslaTowerId, tpd, occupied, builtGround, startCount, poles, field, underBelt,
                              bbMinX, bbMaxX, bbMinY, bbMaxY,
                              recordCX: null, recordCY: null,
                              skipCX: satCX, skipCY: satCY, skipRadius: (float)(satCover * CoverSafety));
                }
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
            // 4. Coverage backfill with the CONSERVATIVE check (CoverSafety) so it
            //    densifies aggressively = best real coverage. Its own gap count is
            //    pessimistic, so we discard it and recount honestly below.
            // Placement passes cover ALL buildings, belts included. Belts blanket
            // the whole blueprint (they touch every machine), so a fill pole placed
            // for an uncovered belt also powers the machines beside it - belts act
            // as free, dense coverage anchors. This incidental blanketing is what
            // gives the best REAL coverage; dropping it (counting consumers only
            // here) leaves big machines with no nearby free tile uncovered.
            // Backfill now covers CONSUMERS ONLY (consumersOnly=true). Covering belts
            // as anchors was needed only before the bHalf fix (big machines were hard
            // to reach); now CoveredBy measures each consumer's true centre at full
            // radius, so machines are covered directly - and NOT covering belts stops
            // the margin/lava poles that powered nothing.
            int backfilled = 0;
            if (IsUsable(fillPoleId))
            {
                backfilled = BackfillCoverage(buildings, startCount, occupied, builtGround, poles, startCount, field, underBelt,
                                              fillPoleId, CoverSafety, true, true, bbMinX, bbMaxX, bbMinY, bbMaxY, out _);
                // 4a. Relaxed Tesla pass at the REAL reach: for the genuine gaps the
                //     conservative pass couldn't reach (~8 tiles), a free tile a bit
                //     farther (~10) still really covers them - place a cheap Tesla
                //     there before falling back to a Substation.
                backfilled += BackfillCoverage(buildings, startCount, occupied, builtGround, poles, startCount, field, underBelt,
                                               fillPoleId, RescueCoverSafety, true, true, bbMinX, bbMaxX, bbMinY, bbMaxY, out _);
            }

            // 4b. Honest recount + optional satellite rescue, both at the ACCURATE
            //     real reach (RescueCoverSafety) and counting POWER CONSUMERS ONLY
            //     (consumersOnly=true) - belts don't consume power, so the log gap
            //     count reflects the real handful of machines/sorters, not the
            //     thousands of edge belts. This second pass:
            //     - counts only the machines the game truly won't power, and
            //     - if SatelliteRescue is on, drops a few wide Substations on those
            //       genuine gaps (26-tile reach). Because it uses the real reach AND
            //       targets consumers, it never wastes a substation on a belt edge or
            //       a machine teslas already cover.
            bool satUsable = IsUsable(SatelliteSubstationId);
            bool doRescue = Plugin.Config.SatelliteRescue.Value && satUsable && fillPoleId != SatelliteSubstationId;
            int countPoleId = satUsable ? SatelliteSubstationId : fillPoleId;
            int uncovered = 0;
            int rescued = BackfillCoverage(buildings, startCount, occupied, builtGround, poles, startCount, field, underBelt,
                                           countPoleId, RescueCoverSafety, doRescue, true, bbMinX, bbMaxX, bbMinY, bbMaxY, out uncovered);

            if (poles.Count == 0)
            {
                DSPCalculatorPlusLog.Warn("[poles] no free tiles for poles in this blueprint - none added "
                    + "(machines packed with no gaps). Power it manually, or set AutoPowerPoles=Off to silence.");
                return;
            }

            // 4c. Prune pass DISABLED. On maximally dense blueprints the clustered
            //     poles are not actually redundant - free tiles exist only in the
            //     scarce belt aisles, so poles pack together while each still covers
            //     DIFFERENT machines no other pole reaches. Pruning them (even with a
            //     safe-radius + connectivity guard) removed load-bearing poles and
            //     drove the unpowered count UP (4 -> 32), so it is a net loss. Kept
            //     the method for reference but do not call it.
            int pruned = 0;

            // 5. Append to the blueprint building array.
            var merged = new BlueprintBuilding[startCount + poles.Count];
            Array.Copy(buildings, merged, startCount);
            for (int i = 0; i < poles.Count; i++) merged[startCount + i] = poles[i];
            bp.buildings = merged;
            ExpandArea(bp, poles);
            AuditPoleProximity(merged);
            LogWorstCoverage(buildings, startCount, poles, underBelt);
            LogConnectivity(poles, underBelt);

            string fillName = fillPoleId == SatelliteSubstationId ? "Substation" : "Tesla";
            DSPCalculatorPlusLog.Info("[poles] mode=" + mode + " added " + poles.Count + " pole(s) over " + startCount
                + " buildings: " + satPlaced + " Satellite Substation + " + teslaPlaced + " Tesla Tower (grid)"
                + (backfilled > 0 ? " + " + backfilled + " " + fillName + " (backfill)" : "")
                + (rescued > 0 ? " + " + rescued + " Satellite Substation (rescue)" : "")
                + (pruned > 0 ? " - " + pruned + " redundant (pruned)" : "")
                + (uncovered > 0 ? "; " + uncovered + " machine area(s) still UNCOVERED (no free tile within range)" : "; full coverage")
                + ".");
            if (uncovered > 0)
                DSPCalculatorPlusLog.Warn("[poles] " + uncovered + " machine area(s) still have no free tile within reach of any pole "
                    + "(even a wide Substation couldn't fit nearby) - power those few by hand.");
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
        private static int PlaceGrid(int poleId, PrefabDesc pd, HashSet<long> occupied, HashSet<long> builtGround, int indexBase,
            List<BlueprintBuilding> outPoles, PoleField field, HashSet<long> underBelt, int bbMinX, int bbMaxX, int bbMinY, int bbMaxY,
            List<int> recordCX, List<int> recordCY,
            List<int> skipCX, List<int> skipCY, float skipRadius)
        {
            float cover = pd.powerCoverRadius;
            float connect = pd.powerConnectDistance;
            if (cover <= 0.1f) return 0;

            // Grid spacing: tighter than the bare sqrt(2) corner-fit so a cell
            // corner sits comfortably inside the reach even after subtracting a
            // typical machine's footprint (a corner-fit grid left every 3x3
            // machine's far corner exactly AT the reach -> uncovered, dumping
            // too much on the backfill). 1.15 keeps ~0.81*reach at the corner,
            // covering 3x3 machines directly; bigger buildings fall to backfill.
            // Uses GridSafety (tight) so poles are placed densely for solid real coverage.
            double sEff = cover * GridSafety * 1.15;
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
                    if (FindFreeTile(occupied, field, gx, gy, searchR, hx, hy, bbMinX, bbMaxX, bbMinY, bbMaxY, underBelt, out px, out py))
                    {
                        if (!IsNearBuiltGround(builtGround, px, py, BuiltGroundRadius)) { gaps++; continue; }
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

        // ---- line-aligned placement (one pole row per machine line) --------

        /// <summary>
        /// Detects the machine "lines". Histograms CONSUMER positions on each axis
        /// and picks the axis with the larger empty-bin fraction - the belt aisles
        /// that separate production lines - as the CROSS axis, returning the centre
        /// of each occupied band on it. Poles then get one regular row per band
        /// along the other (line) axis, which is how a player lays them by hand.
        /// Returns false when there is no clear banding (caller keeps the uniform
        /// grid), e.g. a solid block or a tiny blueprint.
        /// </summary>
        private static bool DetectBands(BlueprintBuilding[] buildings, int nBuildings,
            int bbMinX, int bbMaxX, int bbMinY, int bbMaxY, out bool crossIsY, out List<int> bands)
        {
            List<int> bandsX; double gapX = HistoBands(buildings, nBuildings, bbMinX, bbMaxX, false, out bandsX);
            List<int> bandsY; double gapY = HistoBands(buildings, nBuildings, bbMinY, bbMaxY, true, out bandsY);
            crossIsY = gapY >= gapX;                 // cross axis = the one with more aisles (empty bins)
            bands = crossIsY ? bandsY : bandsX;
            return bands.Count >= 2 && bands.Count <= 4000; // need real banding, guard against pathological cases
        }

        /// <summary>Histograms consumer positions along one axis into 1-tile bins,
        /// returns the occupied band centres (tolerating a 1-bin lane inside a band)
        /// and the fraction of empty bins (a proxy for how aisle-separated the axis
        /// is).</summary>
        private static double HistoBands(BlueprintBuilding[] buildings, int nBuildings,
            int lo, int hi, bool axisY, out List<int> bands)
        {
            bands = new List<int>();
            int span = hi - lo + 1;
            if (span <= 1) return 0.0;
            var hist = new int[span];
            int consumers = 0;
            for (int i = 0; i < nBuildings; i++)
            {
                BlueprintBuilding b = buildings[i];
                if (!IsConsumer(b.itemId)) continue;
                int c = axisY ? (int)Math.Round(b.localOffset_y) : (int)Math.Round(b.localOffset_x);
                int idx = c - lo;
                if (idx >= 0 && idx < span) { hist[idx]++; consumers++; }
            }
            if (consumers == 0) return 0.0;
            int empty = 0;
            for (int i = 0; i < span; i++) if (hist[i] == 0) empty++;
            // Split on ANY empty bin (gapRun >= 1), so each machine ROW - separated
            // from the next by its 1-tile belt aisle - becomes its own band. (An
            // earlier version tolerated a 1-bin gap and merged dozens of rows into a
            // few mega-bands, which left most lines without their own pole row.) Only
            // rows directly adjacent with no aisle stay merged, and they share a row.
            int start = -1;
            for (int i = 0; i < span; i++)
            {
                if (hist[i] > 0) { if (start < 0) start = i; }
                else if (start >= 0) { bands.Add(lo + (start + i - 1) / 2); start = -1; }
            }
            if (start >= 0) bands.Add(lo + (start + span - 1) / 2);
            return (double)empty / span;
        }

        /// <summary>
        /// Places one pole per machine line, the way a player lays them by hand, using
        /// LOCAL per-strip band detection. A single global cross-axis histogram fails on
        /// a full-planet blueprint: DSPCalculator shelf-packs blocks of differing heights
        /// into stacked block-lines, so machine rows in different blocks sit at different
        /// cross positions and are not phase-aligned - projected onto one axis, nearly
        /// every cross row is occupied somewhere and only the handful of block-line gaps
        /// survive as bands (9 for the whole planet), leaving hundreds of real lines with
        /// no pole. Instead we slice the LINE axis into narrow strips and, within each
        /// strip, histogram ONLY that strip's consumers - so the machine rows actually
        /// present in that strip (one block's rows) show up as occupied cross-runs with
        /// their real aisles between them. Each occupied run gets a pole at the strip's
        /// line-centre (wide solid runs get several across at the cover pitch). Strip
        /// width stays under the reach so a pole covers the whole strip along the line and
        /// connects to the same row's poles in adjacent strips. Every pole is
        /// HasConsumerNear-filtered and snapped to the nearest free (preferably open)
        /// tile. Returns targets that found no free tile (backfill still runs after).
        /// </summary>
        private static int PlaceLineAligned(int poleId, PrefabDesc pd, HashSet<long> occupied, HashSet<long> builtGround, int indexBase,
            List<BlueprintBuilding> outPoles, PoleField field, HashSet<long> underBelt,
            Dictionary<long, List<int>> consHash, List<int> consX, List<int> consY,
            bool crossIsY, List<int> bands, int lineMin, int lineMax, int crossMin, int crossMax)
        {
            float cover = pd.powerCoverRadius;
            float connect = pd.powerConnectDistance;
            if (cover <= 0.1f) return 0;
            int searchR = Math.Max(3, (int)Math.Floor(cover));
            int hx, hy; GetTileHalfExtents(pd, poleId, 0f, out hx, out hy);

            // Strip width along the line axis. One pole per strip covers +/- W/2 along the
            // line, so W must stay under the reach even after cross drift; W also caps the
            // gap between a row's poles in neighbouring strips, which must stay inside the
            // connect distance so the row is one network. cover*GridSafety*1.5 (~13) keeps
            // the worst along-line point near 6.7 and the inter-strip gap well under 22.5.
            double W = cover * GridSafety * 1.5;
            if (connect > 0.1f) W = Math.Min(W, connect * ConnectFactor);
            if (W < 4.0) W = 4.0;
            // Cross pitch for splitting a wide solid run (no aisle) into several poles.
            double Pc = cover * GridSafety * 1.5;

            int lineSpan = Math.Max(1, lineMax - lineMin);
            int nStrips = Math.Max(1, (int)Math.Ceiling(lineSpan / W));
            double sStep = lineSpan / (double)nStrips;   // even strip width, lands on both edges

            // Bucket every consumer into its line-axis strip once.
            var byStrip = new List<int>[nStrips];
            for (int s = 0; s < nStrips; s++) byStrip[s] = new List<int>();
            for (int k = 0; k < consX.Count; k++)
            {
                int lc = crossIsY ? consX[k] : consY[k];      // line-axis coord
                int s = (int)((lc - lineMin) / sStep);
                if (s < 0) s = 0; else if (s >= nStrips) s = nStrips - 1;
                byStrip[s].Add(k);
            }

            int gaps = 0;
            for (int s = 0; s < nStrips; s++)
            {
                List<int> members = byStrip[s];
                if (members.Count == 0) continue;
                int lineCenter = lineMin + (int)Math.Round((s + 0.5) * sStep);

                // Local cross-axis occupancy for THIS strip only - the machine rows
                // actually here, with their real aisles between them.
                int cmin = int.MaxValue, cmax = int.MinValue;
                foreach (int k in members)
                {
                    int cc = crossIsY ? consY[k] : consX[k];
                    if (cc < cmin) cmin = cc;
                    if (cc > cmax) cmax = cc;
                }
                int cspan = cmax - cmin + 1;
                var occ = new bool[cspan];
                foreach (int k in members)
                {
                    int cc = crossIsY ? consY[k] : consX[k];
                    occ[cc - cmin] = true;
                }

                // Walk occupied cross-runs; each run = one machine row (or a solid block).
                int runStart = -1;
                for (int i = 0; i <= cspan; i++)
                {
                    bool here = i < cspan && occ[i];
                    if (here) { if (runStart < 0) runStart = i; continue; }
                    if (runStart < 0) continue;
                    int runLo = cmin + runStart, runHi = cmin + (i - 1);
                    int runSpan = runHi - runLo;
                    // Narrow run (a normal machine row): one pole at its centre. Wide
                    // solid run (no aisle): several poles across it at the cover pitch.
                    int nA = runSpan < Pc ? 0 : (int)Math.Ceiling(runSpan / Pc);
                    double aStep = nA > 0 ? runSpan / (double)nA : 0.0;
                    for (int t = 0; t <= nA; t++)
                    {
                        int c = nA == 0 ? (runLo + runHi) / 2 : runLo + (int)Math.Round(t * aStep);
                        int gx = crossIsY ? lineCenter : c;
                        int gy = crossIsY ? c : lineCenter;
                        if (!HasConsumerNear(consHash, consX, consY, gx, gy, cover)) continue;
                        int px, py;
                        if (FindFreeTile(occupied, field, gx, gy, searchR, hx, hy,
                                         crossIsY ? lineMin : crossMin, crossIsY ? lineMax : crossMax,
                                         crossIsY ? crossMin : lineMin, crossIsY ? crossMax : lineMax,
                                         underBelt, out px, out py))
                        {
                            if (!HasConsumerNear(consHash, consX, consY, px, py, cover)) continue;
                            if (!IsNearBuiltGround(builtGround, px, py, BuiltGroundRadius)) { gaps++; continue; }
                            outPoles.Add(MakePole(poleId, pd.modelIndex, px, py, indexBase + outPoles.Count));
                            MarkOccupied(occupied, px, py, hx, hy);
                            field.Add(px, py);
                        }
                        else gaps++;
                    }
                    runStart = -1;
                }
            }
            return gaps;
        }

        /// <summary>True if any power consumer lies within <paramref name="radius"/>
        /// of (x,y) - i.e. a pole here would actually power something. Spatial-hashed
        /// over the consumer set (Bucket=32 &gt; any pole reach, so scanning +/-1
        /// bucket is exact for radius &lt;= Bucket).</summary>
        private static bool HasConsumerNear(Dictionary<long, List<int>> consHash, List<int> consX, List<int> consY,
            int x, int y, double radius)
        {
            if (consHash == null) return true; // no filter available -> don't block placement
            double r2 = radius * radius;
            int bx = FloorDiv(x, Bucket), by = FloorDiv(y, Bucket);
            for (int dbx = -1; dbx <= 1; dbx++)
                for (int dby = -1; dby <= 1; dby++)
                {
                    List<int> l;
                    if (!consHash.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                    foreach (int c in l)
                    {
                        double dx = x - consX[c], dy = y - consY[c];
                        if (dx * dx + dy * dy <= r2) return true;
                    }
                }
            return false;
        }

        /// <summary>True if (x,y) sits within <see cref="BuiltGroundRadius"/> tiles of
        /// some REAL building footprint (snapshotted before any pole was placed - see
        /// <c>builtGround</c> in <see cref="InjectPoles"/>). The blueprint format has no
        /// terrain data, so this is the only ground we can actually vouch for; a free
        /// tile found only by drifting well past this radius may be lava, water, or a
        /// cliff the game will refuse at paste, leaving a pole our diagnostics counted
        /// but that never gets built. Cheap fixed-radius scan (BuiltGroundRadius=3 -&gt;
        /// 49 lookups), called once per accepted placement.</summary>
        private static bool IsNearBuiltGround(HashSet<long> builtGround, int x, int y, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (builtGround.Contains(Key(x + dx, y + dy))) return true;
            return false;
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
        /// Walks buildings and drops a fill pole near any that no already-placed
        /// pole covers, so coverage is total. Uses a spatial hash of placed poles
        /// so the per-building check stays cheap even at 25k+ buildings. Returns
        /// the number of fill poles added; <paramref name="trulyUncovered"/>
        /// counts spots where not even a fill pole could fit within range.
        ///
        /// <paramref name="consumersOnly"/> decides WHICH buildings the walk
        /// considers. <c>false</c> (the placement passes): every building, belts
        /// included - belts blanket the layout, so covering them incidentally
        /// powers the machines beside them (best real coverage). <c>true</c> (the
        /// honest count + rescue pass): only power consumers, so the reported gap
        /// count and any rescue substations track the real machines that need
        /// power, not the thousands of belts that never do.
        /// </summary>
        private static int BackfillCoverage(BlueprintBuilding[] buildings, int nBuildings, HashSet<long> occupied, HashSet<long> builtGround,
            List<BlueprintBuilding> poles, int startCount, PoleField field, HashSet<long> underBelt, int fillPoleId,
            double coverSafety, bool placeMode, bool consumersOnly,
            int bbMinX, int bbMaxX, int bbMinY, int bbMaxY, out int trulyUncovered)
        {
            trulyUncovered = 0;
            ItemProto fp = LDB.items.Select(fillPoleId);
            if (fp == null || fp.prefabDesc == null) return 0;
            PrefabDesc fpd = fp.prefabDesc;
            float fillCover = fpd.powerCoverRadius;
            if (fillCover <= 0.1f) return 0;
            int fhx, fhy; GetTileHalfExtents(fpd, fillPoleId, 0f, out fhx, out fhy);
            float fillEff = (float)(fillCover * coverSafety);
            // A fill pole can sit anywhere within fillEff of the building's CENTRE
            // and still power it - the game (PowerSystem) measures node -> consumer
            // plugPos (the building's entity centre, entityPool[id].pos) against
            // coverRadius^2 with NO footprint subtraction. So the search reach is
            // fillEff for every building regardless of its size; a big machine is
            // powered by a pole up to fillEff from its centre just like a sorter.
            int searchR = Math.Max(3, (int)Math.Floor((double)fillEff));

            // Spatial hash of already-placed (grid) poles, keyed by effective reach.
            var spatial = new Dictionary<long, List<int>>();
            var poleX = new List<int>();
            var poleY = new List<int>();
            var poleCov = new List<float>();
            for (int i = 0; i < poles.Count; i++)
            {
                BlueprintBuilding p = poles[i];
                AddSpatial(spatial, poleX, poleY, poleCov, (int)p.localOffset_x, (int)p.localOffset_y, (float)(RawCoverOf(p.itemId) * coverSafety));
            }

            // Collect unique uncovered building centres. A building is covered when
            // its centre is within a pole's reach (matches the game: node ->
            // consumer-centre distance vs coverRadius, no footprint term). We use the
            // EXACT fractional localOffset, not a rounded tile: sorters span between
            // tiles so their localOffset is fractional (e.g. x=100.5), and rounding
            // it discarded up to ~0.7 tile of distance - enough to mis-count a sorter
            // sitting right at the coverage margin as powered when the game (which
            // uses the true position) leaves it just outside. Machines sit on integer
            // tiles so they were never affected; this is why only a few sorters, only
            // on big blueprints, slipped through as "covered" yet unpowered in-game.
            var seen = new HashSet<long>();
            var todoX = new List<float>();
            var todoY = new List<float>();
            for (int i = 0; i < nBuildings; i++)
            {
                BlueprintBuilding b = buildings[i];
                // Count-only pass skips belts (they don't need power); placement
                // passes keep them as coverage anchors (see method summary).
                if (consumersOnly && !IsConsumer(b.itemId)) continue;
                float fx = b.localOffset_x, fy = b.localOffset_y;
                long k = Key((int)Math.Round(fx), (int)Math.Round(fy)); // dedup at tile granularity
                if (!seen.Add(k)) continue;
                if (CoveredBy(spatial, poleX, poleY, poleCov, fx, fy)) continue;
                todoX.Add(fx); todoY.Add(fy);
            }

            int added = 0;
            for (int i = 0; i < todoX.Count; i++)
            {
                float fx = todoX[i], fy = todoY[i];
                if (CoveredBy(spatial, poleX, poleY, poleCov, fx, fy)) continue; // covered by a backfill pole added meanwhile
                if (!placeMode) { trulyUncovered++; continue; } // count-only pass: just tally the honest remaining gap
                int gx = (int)Math.Round(fx), gy = (int)Math.Round(fy);
                int px, py;
                if (FindFreeTile(occupied, field, gx, gy, searchR, fhx, fhy, bbMinX, bbMaxX, bbMinY, bbMaxY, underBelt, out px, out py))
                {
                    if (!IsNearBuiltGround(builtGround, px, py, BuiltGroundRadius)) { trulyUncovered++; continue; }
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

        // Covered if the building centre is within a pole's effective reach - the
        // game measures node -> consumer-centre vs coverRadius, no footprint term.
        // Takes the consumer's EXACT (fractional) position; poles are always on
        // integer tiles. Bucketing uses the rounded position (Bucket=32 >> 1 tile,
        // and we scan +/-1 bucket, so sub-tile rounding never drops a candidate).
        private static bool CoveredBy(Dictionary<long, List<int>> spatial, List<int> poleX, List<int> poleY,
            List<float> poleCov, double x, double y)
        {
            int bx = FloorDiv((int)Math.Round(x), Bucket), by = FloorDiv((int)Math.Round(y), Bucket);
            for (int dbx = -1; dbx <= 1; dbx++)
                for (int dby = -1; dby <= 1; dby++)
                {
                    List<int> l;
                    if (!spatial.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                    foreach (int i in l)
                    {
                        double effR = poleCov[i];
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

        /// <summary>True if this building consumes power (must sit inside a pole's
        /// supply area). Belts don't; sorters/machines/stations/labs do. Cached.</summary>
        private static bool IsConsumer(int itemId)
        {
            bool v;
            if (_consumerCache.TryGetValue(itemId, out v)) return v;
            ItemProto p = LDB.items.Select(itemId);
            v = p != null && p.prefabDesc != null && p.prefabDesc.isPowerConsumer;
            _consumerCache[itemId] = v;
            return v;
        }

        /// <summary>True if this building IS a power node (a pole/tower/generator),
        /// i.e. something that itself occupies the pole network. Cached. Used to
        /// keep new poles away from poles already in the blueprint.</summary>
        private static bool IsPowerNodeItem(int itemId)
        {
            bool v;
            if (_nodeCache.TryGetValue(itemId, out v)) return v;
            ItemProto p = LDB.items.Select(itemId);
            v = p != null && p.prefabDesc != null && p.prefabDesc.isPowerNode;
            _nodeCache[itemId] = v;
            return v;
        }

        /// <summary>Raw supply radius of a pole (cached). Callers multiply by the
        /// safety factor appropriate to their use (placement vs honest count).</summary>
        private static float RawCoverOf(int itemId)
        {
            float v;
            if (_coverCache.TryGetValue(itemId, out v)) return v;
            ItemProto p = LDB.items.Select(itemId);
            v = (p != null && p.prefabDesc != null) ? p.prefabDesc.powerCoverRadius : 0f;
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

        private static void ReserveFootprint(HashSet<long> occupied, BlueprintBuilding b, float clearanceZ,
            ref int skippedElevated, HashSet<long> elevatedTiles)
        {
            int hx = 0, hy = 0;
            ItemProto proto = LDB.items.Select(b.itemId);
            if (proto != null && proto.prefabDesc != null)
                GetTileHalfExtents(proto.prefabDesc, b.itemId, b.yaw, out hx, out hy);

            // Reserve each endpoint only if its base sits below a pole's top; a
            // belt segment raised above that leaves the ground buildable under it.
            // Freed (elevated) tiles are also recorded so a later diagnostic can tell
            // which poles ended up UNDER a belt (the tiles the game is most likely to
            // reject on paste).
            int cx = (int)Math.Round(b.localOffset_x);
            int cy = (int)Math.Round(b.localOffset_y);
            if (b.localOffset_z < clearanceZ) MarkOccupied(occupied, cx, cy, hx, hy);
            else { skippedElevated++; if (elevatedTiles != null) elevatedTiles.Add(Key(cx, cy)); }
            int x2 = (int)Math.Round(b.localOffset_x2);
            int y2 = (int)Math.Round(b.localOffset_y2);
            if (x2 != cx || y2 != cy)
            {
                if (b.localOffset_z2 < clearanceZ) MarkOccupied(occupied, x2, y2, hx, hy);
                else { skippedElevated++; if (elevatedTiles != null) elevatedTiles.Add(Key(x2, y2)); }
            }
        }

        /// <summary>
        /// How far a belt's collider UNDERSIDE sits below the belt's altitude
        /// (localOffset_z), in tiles. A belt at altitude z blocks a pole unless
        /// z - thisValue >= poleTop, so we add it to poleTop to get the altitude at
        /// which a belt truly clears a pole. Derived from the belt prefab's build
        /// collider (centre offset + half-height); falls back to a safe 0.5 and adds
        /// a small epsilon so we never sit exactly on the collision boundary.
        /// </summary>
        private static float BeltUnderClearance()
        {
            float under = 0.5f;
            ItemProto belt = LDB.items.Select(ConveyorBeltId);
            if (belt != null && belt.prefabDesc != null)
            {
                ColliderData bc = belt.prefabDesc.buildCollider;
                float u = bc.ext.y - bc.pos.y; // centre-to-underside distance
                if (u > 0.05f) under = u;
            }
            return under + 0.1f;
        }

        /// <summary>Top of a pole's build collider (world units ~ tiles) - the
        /// height a belt must clear for the ground under it to stay buildable.
        /// Falls back to ~2 if the collider is degenerate.</summary>
        private static float PoleTopHeight(int itemId)
        {
            ItemProto p = LDB.items.Select(itemId);
            if (p == null || p.prefabDesc == null) return 2f;
            ColliderData bc = p.prefabDesc.buildCollider;
            float top = bc.pos.y + bc.ext.y;
            return top >= 0.5f ? top : 2f;
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

        /// <summary>
        /// Finds the free tile CLOSEST to the ideal point (gx,gy) within searchR,
        /// honouring building footprints and DSP's min inter-pole distance.
        ///
        /// Searches ring by ring (increasing Chebyshev distance) so the nearest
        /// occupied-free ring is found first, then returns the tile in that ring
        /// with the smallest Euclidean distance to (gx,gy). This matters: the old
        /// version returned the FIRST tile in scan order, which was the ring's
        /// top-left CORNER (its farthest, most diagonal tile). Every pole whose
        /// ideal spot was blocked therefore drifted down-and-left in lockstep,
        /// clustering against each other and leaving the far side of each aisle
        /// unpowered (the visible "row with no pole, pole moved next to the last
        /// one"). Preferring the nearest tile keeps each pole in its own grid
        /// column/aisle slot, so poles stay evenly spread.
        /// </summary>
        private static bool FindFreeTile(HashSet<long> occupied, PoleField field, int gx, int gy, int searchR,
            int phx, int phy, int clampMinX, int clampMaxX, int clampMinY, int clampMaxY,
            HashSet<long> underBelt, out int px, out int py)
        {
            int anyX = 0, anyY = 0; long anyD2 = long.MaxValue; // nearest free tile of any kind (fallback)
            for (int r = 0; r <= searchR; r++)
            {
                int bestX = 0, bestY = 0; long bestD2 = long.MaxValue; // nearest OPEN (not under-belt) tile in this ring
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; // ring only
                        int x = gx + dx, y = gy + dy;
                        if (x < clampMinX || x > clampMaxX || y < clampMinY || y > clampMaxY) continue;
                        // Free of buildings AND >= DSP's minimum inter-pole distance.
                        if (!IsAreaFree(occupied, x, y, phx, phy) || field.TooClose(x, y)) continue;
                        long d2 = (long)dx * dx + (long)dy * dy;
                        if (d2 < anyD2) { anyD2 = d2; anyX = x; anyY = y; }
                        // Prefer a genuinely open ground tile over one under a raised belt:
                        // under-belt tiles are the ones the game is likeliest to reject on
                        // paste, and steering poles onto open aisle tiles also stops them
                        // piling onto the few belt-crossing tiles (the clustering the user saw).
                        bool open = underBelt == null || !underBelt.Contains(Key(x, y));
                        if (open && d2 < bestD2) { bestD2 = d2; bestX = x; bestY = y; }
                    }
                }
                if (bestD2 != long.MaxValue) { px = bestX; py = bestY; return true; } // nearest OPEN tile wins as soon as one is in reach
            }
            // No open tile anywhere in reach - fall back to the nearest under-belt tile.
            if (anyD2 != long.MaxValue) { px = anyX; py = anyY; return true; }
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

        /// <summary>
        /// Post-placement sanity check: scans EVERY power node in the final
        /// blueprint (ours + any already present) in blueprint-local coordinates
        /// and counts pairs closer than DSP's minimum inter-pole distance, and the
        /// subset that are effectively stacked (&lt;~1 tile apart). With the
        /// min-distance field seeded from pre-existing poles this should always be
        /// zero; a non-zero count is the definitive signature of duplicate poles
        /// coming from our side (as opposed to sphere-seam overlap, which is a
        /// spherical-projection artefact invisible in local coordinates). Cheap:
        /// spatial-hashed, O(number of poles).
        /// </summary>
        private static void AuditPoleProximity(BlueprintBuilding[] merged)
        {
            var spatial = new Dictionary<long, List<int>>();
            var xs = new List<int>();
            var ys = new List<int>();
            int tooClose = 0, stacked = 0, nodes = 0;
            for (int i = 0; i < merged.Length; i++)
            {
                BlueprintBuilding b = merged[i];
                if (!IsPowerNodeItem(b.itemId)) continue;
                int x = (int)Math.Round(b.localOffset_x), y = (int)Math.Round(b.localOffset_y);
                int bx = FloorDiv(x, 4), by = FloorDiv(y, 4);
                for (int dbx = -1; dbx <= 1; dbx++)
                    for (int dby = -1; dby <= 1; dby++)
                    {
                        List<int> l;
                        if (!spatial.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                        foreach (int j in l)
                        {
                            double dx = x - xs[j], dy = y - ys[j];
                            double d2 = dx * dx + dy * dy;
                            if (d2 < 13.0) { tooClose++; if (d2 < 1.5) stacked++; }
                        }
                    }
                int idx = xs.Count;
                xs.Add(x); ys.Add(y); nodes++;
                long k = Key(bx, by);
                List<int> bucket;
                if (!spatial.TryGetValue(k, out bucket)) { bucket = new List<int>(); spatial[k] = bucket; }
                bucket.Add(idx);
            }
            if (tooClose > 0)
                DSPCalculatorPlusLog.Warn("[poles][diag] proximity audit: " + tooClose + " pole-pair(s) closer than DSP min distance out of "
                    + nodes + " node(s) (" + stacked + " effectively stacked). In-game these overlap or fail to place - report this line.");
            else
                DSPCalculatorPlusLog.Info("[poles][diag] proximity audit: " + nodes + " node(s), none below DSP min distance (local coords clean; any visible overlap is sphere-seam).");
        }

        /// <summary>
        /// Diagnostic: for every POWER CONSUMER, measures the true (fractional)
        /// distance to its nearest placed pole and logs the worst handful, each with
        /// its local coordinate. This is the decisive discriminator when the log says
        /// "full coverage" but the user still sees an unpowered building:
        ///  - if the worst distance is comfortably below the Tesla reach (10.5), the
        ///    coverage MATH is fine and the fault is a pole that our math counted but
        ///    the game did not actually build (e.g. rejected on paste);
        ///  - if the worst distance is at/over ~10.5, it is a genuine coverage-margin
        ///    gap and the placement needs to densify.
        /// Cheap: spatial-hashed, one nearest-neighbour query per consumer.
        /// </summary>
        private static void LogWorstCoverage(BlueprintBuilding[] buildings, int nBuildings,
            List<BlueprintBuilding> poles, HashSet<long> underBelt)
        {
            if (poles.Count == 0) return;
            // Two spatial hashes: ALL poles, and only poles NOT on an under-belt tile
            // (the ones certain to build in-game). Comparing the worst distance under
            // each tells us whether under-belt poles the game may reject are load-
            // bearing for coverage.
            var spatialAll = new Dictionary<long, List<int>>();
            var spatialSafe = new Dictionary<long, List<int>>();
            var px = new List<int>(); var py = new List<int>(); var safe = new List<bool>();
            int underBeltPoles = 0;
            for (int i = 0; i < poles.Count; i++)
            {
                BlueprintBuilding p = poles[i];
                int x = (int)Math.Round(p.localOffset_x), y = (int)Math.Round(p.localOffset_y);
                bool isSafe = underBelt == null || !underBelt.Contains(Key(x, y));
                if (!isSafe) underBeltPoles++;
                int idx = px.Count; px.Add(x); py.Add(y); safe.Add(isSafe);
                long bk = Key(FloorDiv(x, Bucket), FloorDiv(y, Bucket));
                AddIdx(spatialAll, bk, idx);
                if (isSafe) AddIdx(spatialSafe, bk, idx);
            }

            double worstAll = WorstDistance(buildings, nBuildings, spatialAll, px, py, 0, out int consumers);
            double worstSafe = WorstDistance(buildings, nBuildings, spatialSafe, px, py, 0, out _);
            // Same measurement but taking each consumer at its OTHER endpoint (mode 1)
            // and MIDPOINT (mode 2). The game powers a sorter from its entity centre
            // (= localOffset). If worst-at-endpoint2 or midpoint is much larger (over
            // 10.5) while worst-at-localOffset is fine, we are measuring the wrong end
            // of 2-tile sorters and the game's real plug point is out of reach.
            double worstEnd2 = WorstDistance(buildings, nBuildings, spatialAll, px, py, 1, out _);
            double worstMid = WorstDistance(buildings, nBuildings, spatialAll, px, py, 2, out _);

            DSPCalculatorPlusLog.Info("[poles][diag] worst consumer->nearest-pole distance over " + consumers
                + " consumer(s) (Tesla reach=10.5): " + worstAll.ToString("0.00")
                + " with all poles; " + worstSafe.ToString("0.00") + " if the " + underBeltPoles
                + " under-belt pole(s) are dropped.");
            DSPCalculatorPlusLog.Info("[poles][diag] worst measured at consumer endpoint2=" + worstEnd2.ToString("0.00")
                + ", midpoint=" + worstMid.ToString("0.00")
                + " (if either >10.5 while localOffset is fine, the sorter's real plug point is beyond reach = a position-measurement bug).");
        }

        private static void AddIdx(Dictionary<long, List<int>> spatial, long bk, int idx)
        {
            List<int> l;
            if (!spatial.TryGetValue(bk, out l)) { l = new List<int>(); spatial[bk] = l; }
            l.Add(idx);
        }

        // Largest consumer->nearest-pole distance under a given pole spatial hash.
        // point: 0 = localOffset (the game's plug point), 1 = localOffset2 (other
        // endpoint), 2 = midpoint of the two.
        private static double WorstDistance(BlueprintBuilding[] buildings, int nBuildings,
            Dictionary<long, List<int>> spatial, List<int> px, List<int> py, int point, out int consumers)
        {
            consumers = 0;
            double worst = 0.0;
            for (int i = 0; i < nBuildings; i++)
            {
                BlueprintBuilding b = buildings[i];
                if (!IsConsumer(b.itemId)) continue;
                consumers++;
                double fx, fy;
                if (point == 1) { fx = b.localOffset_x2; fy = b.localOffset_y2; }
                else if (point == 2) { fx = (b.localOffset_x + b.localOffset_x2) * 0.5; fy = (b.localOffset_y + b.localOffset_y2) * 0.5; }
                else { fx = b.localOffset_x; fy = b.localOffset_y; }
                int bx = FloorDiv((int)Math.Round(fx), Bucket), by = FloorDiv((int)Math.Round(fy), Bucket);
                double best = double.MaxValue;
                for (int dbx = -1; dbx <= 1; dbx++)
                    for (int dby = -1; dby <= 1; dby++)
                    {
                        List<int> l;
                        if (!spatial.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                        foreach (int j in l)
                        {
                            double dx = fx - px[j], dy = fy - py[j];
                            double d2 = dx * dx + dy * dy;
                            if (d2 < best) best = d2;
                        }
                    }
                double dist = (best == double.MaxValue) ? 999.0 : Math.Sqrt(best);
                if (dist > worst) worst = dist;
            }
            return worst;
        }

        /// <summary>Power CONNECT distance of a pole (cached). Two nodes join the
        /// same network when within this range of each other.</summary>
        private static float ConnectOf(int itemId)
        {
            ItemProto p = LDB.items.Select(itemId);
            return (p != null && p.prefabDesc != null) ? p.prefabDesc.powerConnectDistance : 0f;
        }

        /// <summary>
        /// Diagnostic: how many SEPARATE power networks the placed poles form, both
        /// for ALL poles and for OPEN poles only (excluding under-belt poles the game
        /// may reject on paste). The mod's promise is a single network the user
        /// energises once. If the ALL-pole graph is one network but the OPEN-only
        /// graph fragments, then some under-belt poles are the sole bridges between
        /// pole rows - and if the game rejects them, the pasted network splits,
        /// stranding whole clusters (which read in-game as "no power network" even
        /// though poles sit right next to them). That is the decisive signature of
        /// the residual unpowered machines. Union-find over poles, linking any two
        /// within their (smaller) connect distance; spatial-hashed.
        /// </summary>
        private static void LogConnectivity(List<BlueprintBuilding> poles, HashSet<long> underBelt)
        {
            int n = poles.Count;
            if (n < 2) return;
            var px = new int[n]; var py = new int[n]; var conn = new float[n]; var open = new bool[n];
            var spatial = new Dictionary<long, List<int>>();
            int bkt = 64; // > largest Tesla/substation connect distance (53.5), so a linkable pole is within one bucket
            for (int i = 0; i < n; i++)
            {
                px[i] = (int)Math.Round(poles[i].localOffset_x);
                py[i] = (int)Math.Round(poles[i].localOffset_y);
                conn[i] = ConnectOf(poles[i].itemId);
                open[i] = underBelt == null || !underBelt.Contains(Key(px[i], py[i]));
                long bk = Key(FloorDiv(px[i], bkt), FloorDiv(py[i], bkt));
                List<int> l;
                if (!spatial.TryGetValue(bk, out l)) { l = new List<int>(); spatial[bk] = l; }
                l.Add(i);
            }

            int compsAll = CountComponents(n, px, py, conn, spatial, bkt, false, open, out int largestAll);
            int compsOpen = CountComponents(n, px, py, conn, spatial, bkt, true, open, out int largestOpen);
            int openCount = 0; for (int i = 0; i < n; i++) if (open[i]) openCount++;

            if (compsAll <= 1)
                DSPCalculatorPlusLog.Info("[poles][diag] connectivity: all " + n + " pole(s) form ONE network.");
            else
                DSPCalculatorPlusLog.Warn("[poles][diag] connectivity: poles split into " + compsAll + " SEPARATE networks (largest "
                    + largestAll + " of " + n + "). Needs bridging poles.");

            if (compsOpen <= 1)
                DSPCalculatorPlusLog.Info("[poles][diag] connectivity (open poles only, " + openCount + "): ONE network - safe against under-belt rejection.");
            else
                DSPCalculatorPlusLog.Warn("[poles][diag] connectivity (open poles only, " + openCount + "): " + compsOpen
                    + " SEPARATE networks (largest " + largestOpen + "). Under-belt poles are bridging these; if the game rejects them the pasted network SPLITS -> stranded clusters read as 'no power network'. This is the residual-unpowered cause.");
        }

        // Counts connected components among poles (optionally only OPEN ones) linked
        // when within their mutual connect distance. Returns component count and
        // largest component size.
        private static int CountComponents(int n, int[] px, int[] py, float[] conn,
            Dictionary<long, List<int>> spatial, int bkt, bool openOnly, bool[] open, out int largest)
        {
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < n; i++)
            {
                if (openOnly && !open[i]) continue;
                int bx = FloorDiv(px[i], bkt), by = FloorDiv(py[i], bkt);
                for (int dbx = -1; dbx <= 1; dbx++)
                    for (int dby = -1; dby <= 1; dby++)
                    {
                        List<int> l;
                        if (!spatial.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                        foreach (int j in l)
                        {
                            if (j <= i) continue;
                            if (openOnly && !open[j]) continue;
                            double dx = px[i] - px[j], dy = py[i] - py[j];
                            double link = Math.Min(conn[i], conn[j]);
                            if (dx * dx + dy * dy <= link * link) Union(parent, i, j);
                        }
                    }
            }
            int comps = 0; largest = 0;
            var size = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                if (openOnly && !open[i]) continue;
                int r = Find(parent, i);
                int s; size.TryGetValue(r, out s); size[r] = s + 1;
            }
            foreach (var kv in size) { comps++; if (kv.Value > largest) largest = kv.Value; }
            return comps;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[ra] = rb;
        }

        /// <summary>
        /// Removes redundant poles. A pole is redundant when every power CONSUMER in
        /// its reach is also within another pole's reach (so dropping it opens no
        /// coverage gap) AND it still has at least two other poles within connect
        /// distance (so the network stays joined). Backfill/late poles are considered
        /// first, since those are the dense-area clusters. Coverage counts are updated
        /// live so two poles sharing the same lone consumer are never both dropped.
        /// Kept poles are re-indexed. Returns how many were removed.
        /// </summary>
        private static int PrunePoles(BlueprintBuilding[] buildings, int nBuildings, List<BlueprintBuilding> poles,
            int startCount, HashSet<long> underBelt)
        {
            int n = poles.Count;
            if (n < 3) return 0;
            var px = new int[n]; var py = new int[n]; var pcov = new float[n]; var pconn = new float[n];
            var open = new bool[n]; // pole sits on an open tile (not under a raised belt)
            var poleHash = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                px[i] = (int)Math.Round(poles[i].localOffset_x);
                py[i] = (int)Math.Round(poles[i].localOffset_y);
                pcov[i] = RawCoverOf(poles[i].itemId);
                pconn[i] = ConnectOf(poles[i].itemId);
                open[i] = underBelt == null || !underBelt.Contains(Key(px[i], py[i]));
                AddIdx(poleHash, Key(FloorDiv(px[i], Bucket), FloorDiv(py[i], Bucket)), i);
            }

            var cxs = new List<int>(); var cys = new List<int>();
            for (int i = 0; i < nBuildings; i++)
            {
                if (!IsConsumer(buildings[i].itemId)) continue;
                cxs.Add((int)Math.Round(buildings[i].localOffset_x));
                cys.Add((int)Math.Round(buildings[i].localOffset_y));
            }
            int m = cxs.Count;
            var conHash = new Dictionary<long, List<int>>();
            for (int c = 0; c < m; c++)
                AddIdx(conHash, Key(FloorDiv(cxs[c], Bucket), FloorDiv(cys[c], Bucket)), c);

            // A consumer counts as "safely covered" by a pole only when it is within
            // a SAFE radius (cover * PruneSafety), NOT the full reach. Pruning keeps
            // every consumer with >=1 OPEN pole inside that safe radius, so it never
            // strips coverage down to the 10.5 edge (an earlier full-radius prune left
            // consumers at exactly 10.50, which the sphere/rounding then tipped past
            // 10.5 in-game -> mass unpowered). safeCount is that per-consumer tally;
            // poleConsumers still uses the FULL radius so removing a pole accounts for
            // every consumer it actually reaches. Under-belt poles never count toward
            // safeCount (they may be rejected on paste).
            const double PruneSafety = 0.86; // safe radius ~9.03 of 10.5 -> ~1.5 tiles headroom
            var poleConsumers = new List<int>[n];
            var safeCount = new int[m];
            for (int i = 0; i < n; i++)
            {
                var list = new List<int>();
                double fullR2 = (double)pcov[i] * pcov[i];
                double safeR2 = pcov[i] * PruneSafety * pcov[i] * PruneSafety;
                int bx = FloorDiv(px[i], Bucket), by = FloorDiv(py[i], Bucket);
                for (int dbx = -1; dbx <= 1; dbx++)
                    for (int dby = -1; dby <= 1; dby++)
                    {
                        List<int> l;
                        if (!conHash.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                        foreach (int c in l)
                        {
                            double dx = cxs[c] - px[i], dy = cys[c] - py[i];
                            double d2 = dx * dx + dy * dy;
                            if (d2 <= fullR2) list.Add(c);
                            if (open[i] && d2 <= safeR2) safeCount[c]++;
                        }
                    }
                poleConsumers[i] = list;
            }

            // Only prune tight clusters: a pole is a removal candidate only if another
            // KEPT open pole sits within this distance (the backfill extras the user
            // sees), so the regular even rows (poles ~11 apart) are never thinned.
            const double ClusterDist2 = 49.0; // 7 tiles

            var removed = new bool[n];
            int removedCount = 0;
            for (int i = n - 1; i >= 0; i--) // backfill/late poles first
            {
                double safeR2 = pcov[i] * PruneSafety * pcov[i] * PruneSafety;
                // Essential if removing this pole would leave any consumer it reaches
                // without a safe-radius OPEN pole. Subtract this pole's own safe
                // contribution to that consumer before testing.
                bool essential = false;
                foreach (int c in poleConsumers[i])
                {
                    double dx = cxs[c] - px[i], dy = cys[c] - py[i];
                    int self = (open[i] && dx * dx + dy * dy <= safeR2) ? 1 : 0;
                    if (safeCount[c] - self <= 0) { essential = true; break; }
                }
                if (essential) continue;

                // Require a close kept OPEN buddy (cluster) AND >=2 connect-range
                // neighbours (stays wired). The buddy limits pruning to visible
                // clusters; the neighbour count preserves connectivity.
                bool hasCloseBuddy = false;
                int neighbors = 0;
                double cd2 = (double)pconn[i] * pconn[i];
                int bx = FloorDiv(px[i], Bucket), by = FloorDiv(py[i], Bucket);
                for (int dbx = -1; dbx <= 1; dbx++)
                    for (int dby = -1; dby <= 1; dby++)
                    {
                        List<int> l;
                        if (!poleHash.TryGetValue(Key(bx + dbx, by + dby), out l)) continue;
                        foreach (int j in l)
                        {
                            if (j == i || removed[j]) continue;
                            double dx = px[j] - px[i], dy = py[j] - py[i];
                            double d2 = dx * dx + dy * dy;
                            if (d2 <= cd2) neighbors++;
                            if (open[j] && d2 <= ClusterDist2) hasCloseBuddy = true;
                        }
                    }
                if (!hasCloseBuddy || neighbors < 2) continue;
                removed[i] = true; removedCount++;
                foreach (int c in poleConsumers[i])
                {
                    double dx = cxs[c] - px[i], dy = cys[c] - py[i];
                    if (open[i] && dx * dx + dy * dy <= safeR2) safeCount[c]--;
                }
            }
            if (removedCount == 0) return 0;

            var kept = new List<BlueprintBuilding>(n - removedCount);
            for (int i = 0; i < n; i++)
                if (!removed[i]) { poles[i].index = startCount + kept.Count; kept.Add(poles[i]); }
            poles.Clear(); poles.AddRange(kept);
            return removedCount;
        }

        private static void LogPoleDiagnosticOnce()
        {
            if (_diagLogged) return;
            _diagLogged = true;
            LogPole("Satellite Substation", SatelliteSubstationId);
            LogPole("Wireless Power Tower", WirelessPowerTowerId);
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
