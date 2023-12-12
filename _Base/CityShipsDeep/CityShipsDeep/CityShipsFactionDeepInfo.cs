using Arcen.AIW2.Core;
using System;

using System.Text;
using Arcen.Universal;
using Arcen.AIW2.External.BulkPathfinding;

namespace Arcen.AIW2.External
{
    public sealed class CityShipsFactionDeepInfo : ExternalFactionDeepInfoRoot, IBulkPathfinding
    {
        public CityShipsFactionBaseInfo BaseInfo;
        public static CityShipsFactionDeepInfo Instance = null;
        private bool playAudioEffectForCommand = false;
        private static readonly List<Planet> WorkingPlanetsList = List<Planet>.Create_WillNeverBeGCed(100, "TemplarFactionDeepInfo-WorkingPlanetsList");
        public override void DoAnyInitializationImmediatelyAfterFactionAssigned()
        {
            this.BaseInfo = this.AttachedFaction.GetExternalBaseInfoAs<CityShipsFactionBaseInfo>();
            Instance = this;
        }

        protected override void Cleanup()
        {
            Instance = null;
            BaseInfo = null;
            WorkingMetalGeneratorList.Clear();
        }

        protected override int MinimumSecondsBetweenLongRangePlannings => 1; //dont like waiting or decolliding here
        private readonly List<Planet> workingAllowedSpawnPlanets = List<Planet>.Create_WillNeverBeGCed(30, "AzarEmpireFactionDeepInfo-workingAllowedSpawnPlanets");

        public override void DoPerSecondLogic_Stage3Main_OnMainThreadAndPartOfSim_HostOnly(ArcenHostOnlySimContext Context)
        {
            if (!AttachedFaction.HasDoneInvasionStyleAction &&
                     (AttachedFaction.InvasionTime > 0 && AttachedFaction.InvasionTime <= World_AIW2.Instance.GameSecond))
            {
                //debugCode = 500;
                //Lets default to just putting the nanocaust hive on a completely random non-player non-ai-king planet
                //TODO: improve this
                GameEntityTypeData entityData = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, "CityShipsCityShipTier1");

                Planet spawnPlanet = null;
                {
                    workingAllowedSpawnPlanets.Clear();
                    int preferredHomeworldDistance = 6;
                    if (this.BaseInfo.SeedNearPlayer && World_AIW2.Instance.PlayerOwnedPlanets != 0)
                    {
                        World_AIW2.Instance.DoForPlanetsSingleThread(false, delegate (Planet planet)
                        {
                            if (planet.GetControllingFactionType() == FactionType.Player)
                            {
                                workingAllowedSpawnPlanets.Add(planet);
                                return DelReturn.Continue;
                            }

                            return DelReturn.Continue;
                        });
                    }
                    else if (!this.BaseInfo.SeedNearPlayer)
                    {
                        do
                        {
                            //debugCode = 600;
                            World_AIW2.Instance.DoForPlanetsSingleThread(false, delegate (Planet planet)
                            {
                                if (planet.GetControllingFactionType() == FactionType.Player)
                                    return DelReturn.Continue;
                                if (planet.GetFactionWithSpecialInfluenceHere().Type != FactionType.NaturalObject && preferredHomeworldDistance >= 6) //don't seed over a minor faction if we are finding good spots
                                {
                                    return DelReturn.Continue;
                                }
                                if (planet.IsPlanetToBeDestroyed || planet.HasPlanetBeenDestroyed)
                                    return DelReturn.Continue;
                                if (planet.PopulationType == PlanetPopulationType.AIBastionWorld ||
                                        planet.IsZenithArchitraveTerritory)
                                {
                                    return DelReturn.Continue;
                                }
                                //debugCode = 800;
                                if (planet.OriginalHopsToAIHomeworld >= preferredHomeworldDistance &&
                                        (planet.OriginalHopsToHumanHomeworld == -1 ||
                                        planet.OriginalHopsToHumanHomeworld >= preferredHomeworldDistance))
                                    workingAllowedSpawnPlanets.Add(planet);
                                return DelReturn.Continue;
                            });

                            preferredHomeworldDistance--;
                            //No need to go past the first loop if we are to seed near the player
                            if (preferredHomeworldDistance == 0)
                                break;
                        } while (workingAllowedSpawnPlanets.Count < 24);
                    }

                    //debugCode = 900;
                    //if (workingAllowedSpawnPlanets.Count == 0)
                    //    throw new Exception("Unable to find a place to spawn the Azaran Empire");

                    if (workingAllowedSpawnPlanets.Count != 0)
                    {
                        // This is not actually random unless we set the seed ourselves.
                        // Since other processing happening before us tends to set the seed to the same value repeatedly.
                        Context.RandomToUse.ReinitializeWithSeed(Engine_Universal.PermanentQualityRandom.Next() + AttachedFaction.FactionIndex);
                        spawnPlanet = workingAllowedSpawnPlanets[Context.RandomToUse.Next(0, workingAllowedSpawnPlanets.Count)];

                        // we don't make a new planet unlike the AE code
                        //spawnPlanet = CreateSpawnPlanet(Context, spawnPlanet);
                        PlanetFaction pFaction = spawnPlanet.GetPlanetFactionForFaction(AttachedFaction);
                        ArcenPoint spawnLocation = spawnPlanet.GetSafePlacementPointAroundPlanetCenter(Context, entityData, FInt.FromParts(0, 200), FInt.FromParts(0, 600));

                        var azarKing = GameEntity_Squad.CreateNew_ReturnNullIfMPClient(pFaction, entityData, entityData.MarkFor(pFaction),
                                                    pFaction.FleetUsedAtPlanet, 0, spawnLocation, Context, "CityShipsCityShip");
                        AttachedFaction.HasDoneInvasionStyleAction = true;

                        //Initialize out new unit's data (this is important)
                        CityShipsPerUnitBaseInfo kingData = azarKing.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");
                        kingData.Prosperity = 1000;
                        kingData.MetalStored = 0;
                        kingData.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                        kingData.planetToBuildOn = null;
                        kingData.target = -1;
                        kingData.toBuildDefenseOrEcon = 0;
                        //maybe add the one it wants to defend, although not sure if necessary

                        SquadViewChatHandlerBase chatHandlerOrNull = ChatClickHandler.CreateNewAs<SquadViewChatHandlerBase>("ShipGeneralFocus");
                        if (chatHandlerOrNull != null)
                            chatHandlerOrNull.SquadToView = LazyLoadSquadWrapper.Create(azarKing);

                        string planetStr = "";
                        if (spawnPlanet.GetDoHumansHaveVision())
                        {
                            planetStr = " from " + spawnPlanet.Name;
                        }

                        var str = string.Format("<color=#{0}>{1}</color> are invading{2}!", AttachedFaction.FactionCenterColor.ColorHexBrighter, AttachedFaction.GetDisplayName(), planetStr);
                        World_AIW2.Instance.QueueChatMessageOrCommand(str, ChatType.LogToCentralChat, chatHandlerOrNull);
                    }


                }


            }
            try
            {
                if (ArcenNetworkAuthority.DesiredStatus == DesiredMultiplayerStatus.Client)
                    return; //only on host (don't bother doing anything as a client)
                HandleDefensesSim(Context);
                HandleEconomicUnitsSim(Context);
                HandleCityShipsSim(Context);
            }
            catch
            {
                //ArcenDebugging.ArcenDebugLogSingleLine("Hit exception in City Ships Stage 3 debugCode " + debugCode + " " + e.ToString(), Verbosity.DoNotShow);
            }
        }
        private string GetTagForShips(GameEntity_Squad entity, ArcenHostOnlySimContext Context)
        {
            FInt aip = FactionUtilityMethods.Instance.GetCurrentAIP();
            string tag = "";

            if (entity.TypeData.GetHasTag("CityShipsOutpost") || entity.TypeData.GetHasTag("CityShipsCityShipTier1"))
            {
                    tag = "CityShipsStrikecraft";
            }
            if (entity.TypeData.GetHasTag("CityShipsStarhold") || entity.TypeData.GetHasTag("CityShipsCityShipTier2"))
            {
                if (Context.RandomToUse.Next(0, 100) < 60)
                {
                    tag = "CityShipsStrikecraft";
                }
                else
                {
                    tag = "CityShipsGuardian";
                }
            }
            if (entity.TypeData.GetHasTag("CityShipsCitadel") || entity.TypeData.GetHasTag("CityShipsCityShipTier3") || entity.TypeData.GetHasTag("CityShipsCityShipTier4"))
            {
                int random = Context.RandomToUse.Next(0, 100);
                if (random < 30)
                {
                    tag = "CityShipsStrikecraft";
                }
                else if (random < 80)
                {
                    tag = "CityShipsGuardian";
                }
                else
                    tag = "CityShipsDire";
            }
            return tag;
        }

        public Planet FindEnemiesInCastleRange(GameEntity_Squad defense)
        {
            bool tracing = this.tracing_shortTerm = Engine_AIW2.TraceAtAll && Engine_AIW2.TracingFlags.Has(ArcenTracingFlags.Templar);
            ArcenCharacterBuffer tracingBuffer = tracing ? ArcenCharacterBuffer.GetFromPoolOrCreate("CityShips-FindEnemiesInCastleRange-trace", 10f) : null;

            //we can help on friendly or neutral planets with enemies and friends
            Planet foundPlanet = null;
            bool verboseDebug = false;
            short range = 1;
            if (defense.TypeData.GetHasTag("CityShipsStarhold") || defense.TypeData.GetHasTag("CityShipsCityShipTier2"))
                range = 2;
            if (defense.TypeData.GetHasTag("CityShipsCitadel") || defense.TypeData.GetHasTag("CityShipsCityShipTier3") || defense.TypeData.GetHasTag("CityShipsCityShipTier4"))
                range = 4;
            if (tracing && verboseDebug)
                tracingBuffer.Add(defense.ToStringWithPlanet() + " Is checking for enemies within " + range + " hops").Add("\n");
            defense.Planet.DoForPlanetsWithinXHops(range, delegate (Planet planet, Int16 Distance)
            {
                if (verboseDebug && tracing)
                    tracingBuffer.Add("\tChecking whether there are enemies on " + planet.Name).Add("\n");

                var pFaction = planet.GetStanceDataForFaction(AttachedFaction);
                int hostileStrength = pFaction[FactionStance.Hostile].TotalStrength;
                if (planet == defense.Planet && hostileStrength > 0)
                {
                    //always go for enemies on our planet
                    foundPlanet = planet;
                    if (verboseDebug && tracing)
                        tracingBuffer.Add("\t\tfound enemies right here\n");

                    return DelReturn.Break;
                }

                if (hostileStrength < 2000)
                {
                    if (verboseDebug && tracing)
                        tracingBuffer.Add("\t\tNo enemies\n");
                    return DelReturn.Continue;
                }
                int friendlyStrength = pFaction[FactionStance.Friendly].TotalStrength + pFaction[FactionStance.Self].TotalStrength;
                if (friendlyStrength < 2000)
                {
                    if (verboseDebug && tracing)
                        tracingBuffer.Add("\t\tNo friends\n");

                    return DelReturn.Continue;
                }
                if (friendlyStrength < hostileStrength)
                {
                    //if on a remote planet, never go where we are outnumbered
                    //Always come out for our own planet though
                    if (verboseDebug && tracing)
                        tracingBuffer.Add("\t\tWe are outnumbered\n");
                    return DelReturn.Continue;
                }
                if (verboseDebug && tracing)
                    tracingBuffer.Add("\tFOUND " + planet.Name + " with friendly strength " + friendlyStrength + " and hostile strength " + hostileStrength).Add("\n");

                foundPlanet = planet;
                return DelReturn.Break;
            },
              delegate (Planet secondaryPlanet)
              {
                  var spFaction = secondaryPlanet.GetStanceDataForFaction(AttachedFaction);
                  int hostileStrength = spFaction[FactionStance.Hostile].TotalStrength;
                  int friendlystrength = spFaction[FactionStance.Friendly].TotalStrength + spFaction[FactionStance.Self].TotalStrength;
                  if (hostileStrength > 10 * 1000 &&
                       hostileStrength > friendlystrength / 2)
                      return PropogationEvaluation.SelfButNotNeighbors;

                  return PropogationEvaluation.Yes;
              });
            #region Tracing
            if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
            if (tracing)
            {
                tracingBuffer.ReturnToPool();
                tracingBuffer = null;
            }
            #endregion

            return foundPlanet;
        }

        public int GetMaxStrengthForDefense(GameEntity_Squad entity)
        {
            int baseStrength = 2000;

           /* if (highestDifficulty.Difficulty >= 7)
                baseStrength += 500;
            if (highestDifficulty.Difficulty >= 9)
                baseStrength += 500;*/

            if (entity.TypeData.GetHasTag("CityShipsStarhold"))
            {
                baseStrength = 6000;
            }
            if (entity.TypeData.GetHasTag("CityShipsCitadel"))
            {
                baseStrength = 12000;
            }
            FInt aip = FactionUtilityMethods.Instance.GetCurrentAIP();
            CityShipsPerUnitBaseInfo data = entity.GetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
            int output = (baseStrength + (baseStrength * data.Prosperity / 1000));
            return output;
        }

        public void HandleDefensesSim(ArcenHostOnlySimContext Context)
        {
            List<SafeSquadWrapper> defenses = this.BaseInfo.Defenses.GetDisplayList();
            List<SafeSquadWrapper> defensesDefenders = this.BaseInfo.DefensesDefenders.GetDisplayList();
            bool localDefenseActive = false;
            for (int i = 0; i < defenses.Count; i++)
            {
                GameEntity_Squad entity = defenses[i].GetSquad();
                if (entity == null)
                    continue;
                int mult = 1;
                if (entity.TypeData.GetHasTag("CityShipsStarhold"))
                    mult = 3;
                if (entity.TypeData.GetHasTag("CityShipsCitadel"))
                    mult = 8;
                //debugCode = 200;
                CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                //if (tracing && verboseDebug)
                //    tracingBuffer.Add("Processing " + entity.ToStringWithPlanet()).Add(" our current strength ").Add(data.GetTotalStrengthInside(entity)).Add(" and max allowed strength: ").Add(GetMaxStrengthForCastle(entity)).Add(" our current metal: " + data.MetalStored).Add("\n");
                if (data.GetTotalStrengthInside(entity) < GetMaxStrengthForDefense(entity))
                {
                    //debugCode = 300;
                    //lets buy some new ships
                    //Income is Prosperity / (1000 / Difficulty.)
                    //Then higher tier structures get much more
                    int income = data.Prosperity / (1000 / this.BaseInfo.Intensity) * mult;
                    var pFaction = entity.Planet.GetStanceDataForFaction(entity.GetFactionOrNull_Safe());
                    /*if (pFaction[FactionStance.Hostile].TotalStrength <
                         pFaction[FactionStance.Friendly].TotalStrength + pFaction[FactionStance.Self].TotalStrength)
                        data.MetalStored += income; //Castles on planets with lots of enemies will stop making units so you can't just park a fleet on the planet and farm endlessly*/
                    data.MetalStored += income;
                    //if (tracing && verboseDebug)
                    //    tracingBuffer.Add("\tincome: " + income).Add("\n");

                    while (data.MetalStored > 0)
                    {
                        //debugCode = 400;
                        //if (tracing && verboseDebug)
                        //    tracingBuffer.Add("\t" + entity.ToStringWithPlanet() + " has < " + GetMaxStrengthForCastle(entity) + " strength, so we get " + income + " metal, giving us a total of " + data.MetalStored).Add("\n");

                        //debugCode = 500;
                        string tag = GetTagForShips(entity, Context);
                        GameEntityTypeData entityData = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, tag);
                        if (entityData == null)
                        {
                            ArcenDebugging.ArcenDebugLogSingleLine("Failed to find entityData for " + entity.ToStringWithPlanet() + " that wants to build a " + tag, Verbosity.DoNotShow);
                            continue;
                        }
                        //debugCode = 600;
                        data.MetalStored -= entityData.CostForAIToPurchase;
                        if (data.ShipsInside[entityData] > 0)
                            data.ShipsInside[entityData]++;
                        else
                            data.ShipsInside[entityData] = 1;
                        //if (tracing && verboseDebug)
                        //    tracingBuffer.Add("\t\tAfter purchasing a " + entityData.GetDisplayName() + " we have " + data.MetalStored + " metal left\n");
                    }
                }
                //debugCode = 1000;
                data.UpdateShipsInside_ForUI(entity);
                //If there are enemies in range, spawn all ships inside us

                Planet planetToHelp = FindEnemiesInCastleRange(entity);
                data.PlanetDefenseWantsToHelp = planetToHelp; //this can be null
                if (planetToHelp != null)
                {
                    //debugCode = 1100;
                    localDefenseActive = true;
                    PlanetFaction pFaction = entity.PlanetFaction;
                    int shipsDeployed = 0;
                    data.ShipsInside.DoFor(delegate (KeyValuePair<GameEntityTypeData, int> pair)
                    {
                        //debugCode = 1200;
                        shipsDeployed += data.ShipsInside[pair.Key];
                        for (int j = 0; j < data.ShipsInside[pair.Key]; j++)
                        {
                            ArcenPoint spawnLocation = entity.Planet.GetSafePlacementPoint_AroundEntity(Context, pair.Key, entity, FInt.FromParts(0, 005), FInt.FromParts(0, 030));
                            int markLevel = (int)entity.CurrentMarkLevel;

                            GameEntity_Squad newEntity = GameEntity_Squad.CreateNew_ReturnNullIfMPClient(pFaction, pair.Key, (byte)markLevel,
                                                                                                          pFaction.Faction.LooseFleet, 0, spawnLocation, Context, "CityShips-ExistingDefenses");  //is fine, main sim thread
                            if (newEntity != null)
                            {
                                newEntity.ShouldNotBeConsideredAsThreatToHumanTeam = true; //just in case
                                newEntity.Orders.SetBehaviorDirectlyInSim(EntityBehaviorType.Attacker_Full); //is fine, main sim thread
                                CityShipsPerUnitBaseInfo newData = newEntity.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");
                                newData.DefenseMode = true;
                                newData.HomeDefense = LazyLoadSquadWrapper.Create(entity);
                            }
                        }

                        return DelReturn.Continue;
                    });
                    //if (tracing && shipsDeployed > 0)
                    //    tracingBuffer.Add("\t" + entity.ToStringWithPlanet() + " has detected nearby enemies on " + planetToHelp.Name + " and is spawning " + shipsDeployed + " ships.\n");


                    data.ShipsInside.Clear();
                }
                else
                {
                    //debugCode = 1300;
                    //If there are no enemies in range and we have a ship close to us, absorb it
                    //Note we can go over the limit here, or absorb random ships from other castles; that's fine.
                    int range = 100;
                    for (int j = 0; j < defensesDefenders.Count; j++)
                    {
                        GameEntity_Squad ship = defensesDefenders[j].GetSquad();
                        if (ship == null)
                            continue;
                        if (ship.Planet != entity.Planet)
                            continue;
                        if (Mat.DistanceBetweenPointsImprecise(entity.WorldLocation, ship.WorldLocation) < range)
                        {
                            if (data.ShipsInside[ship.TypeData] > 0)
                                data.ShipsInside[ship.TypeData]++;
                            else
                                data.ShipsInside[ship.TypeData] = 1;
                            ship.Despawn(Context, true, InstancedRendererDeactivationReason.AFactionJustWarpedMeOut);
                        }
                    }
                }
                if (data.Prosperity > ((10500 * (mult - 1)) + (1500 * entity.CurrentMarkLevel * mult)) && entity.CurrentMarkLevel != 7) //this is the mult we defined earlier
                {
                    entity.SetCurrentMarkLevel((byte)(entity.CurrentMarkLevel + 1));
                }
                if (data.Prosperity > 10500 * mult)
                {
                    //transform it
                    if (entity.TypeData.GetHasTag("CityShipsOutpost"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsStarhold");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                    if (entity.TypeData.GetHasTag("CityShipsStarhold"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsCitadel");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                }
            }
            BaseInfo.AnyDefensesActive = localDefenseActive;
        }

        public void HandleEconomicUnitsSim(ArcenHostOnlySimContext Context)
        {
            List<SafeSquadWrapper> economicUnits = this.BaseInfo.EconomicUnits.GetDisplayList();
            for (int i = 0; i < economicUnits.Count; i++)
            {
                GameEntity_Squad entity = economicUnits[i].GetSquad();
                if (entity == null)
                    continue;
                int mult = 1;
                if (entity.TypeData.GetHasTag("CityShipsTown"))
                {
                    mult = 3;
                }  
                if (entity.TypeData.GetHasTag("CityShipsCity"))
                {
                    mult = 8;
                }
                if (entity.TypeData.GetHasTag("CityShipsMetropolis"))
                {
                    mult = 24;
                }
                CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                if (data.MetalStored < (1 * data.Prosperity * BaseInfo.Intensity * mult)){
                    int income = data.Prosperity / (1000 / this.BaseInfo.Intensity) * mult;
                    data.MetalStored += income;
                }
                data.Prosperity += 1;
                if (data.Prosperity > ((10500 * (mult - 1)) + (1500 * entity.CurrentMarkLevel * mult)) && entity.CurrentMarkLevel != 7) //this is the mult we defined earlier
                {
                    entity.SetCurrentMarkLevel((byte)(entity.CurrentMarkLevel + 1));
                }
                if (data.Prosperity > 10500 * mult)
                {
                    //transform it
                    if (entity.TypeData.GetHasTag("CityShipsVillage"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsTown");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                    if (entity.TypeData.GetHasTag("CityShipsTown"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsCity");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                    if (entity.TypeData.GetHasTag("CityShipsCity"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsMetropolis");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                }
            }
        }

        public void HandleCityShipsSim(ArcenHostOnlySimContext Context)
        {
            WorkingPlanetsList.Clear();
            List<SafeSquadWrapper> cityShips = this.BaseInfo.CityShips.GetDisplayList();
            List<SafeSquadWrapper> defensesDefenders = this.BaseInfo.DefensesDefenders.GetDisplayList();
            List<SafeSquadWrapper> defenses = this.BaseInfo.Defenses.GetDisplayList();
            List<SafeSquadWrapper> economicUnits = this.BaseInfo.EconomicUnits.GetDisplayList();

            bool localDefenseActive = false;

            for (int i = 0; i < cityShips.Count; i++)
            {
                GameEntity_Squad entity = cityShips[i].GetSquad();
                CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                int mult = 1;
                if (entity.TypeData.GetHasTag("CityShipsCityShipTier2"))
                    mult = 3;
                if (entity.TypeData.GetHasTag("CityShipsCityShipTier3"))
                    mult = 8;
                if (entity.TypeData.GetHasTag("CityShipsCityShipTier4"))
                    mult = 24;
                int income = data.Prosperity / (1000 / this.BaseInfo.Intensity) * mult;
                data.MetalStored += income;
                //Do not spawn or buy stuff if below a certain amount of metal
                if (data.GetTotalStrengthInside(entity) < GetMaxStrengthForDefense(entity) && data.MetalStored > (1 * data.Prosperity * BaseInfo.Intensity) * mult)
                {
                    //debugCode = 300;
                    //lets buy some new ships
                    //Income is Prosperity / 1000 * Difficulty.
                    //Then higher tier structures get much more

                    var pFaction = entity.Planet.GetStanceDataForFaction(entity.GetFactionOrNull_Safe());
                    /*if (pFaction[FactionStance.Hostile].TotalStrength <
                         pFaction[FactionStance.Friendly].TotalStrength + pFaction[FactionStance.Self].TotalStrength)
                        data.MetalStored += income; //Castles on planets with lots of enemies will stop making units so you can't just park a fleet on the planet and farm endlessly*/
                    //if (tracing && verboseDebug)
                    //    tracingBuffer.Add("\tincome: " + income).Add("\n");

                    //make sure we don't take all the metal
                    while (data.MetalStored > (1 * data.Prosperity * BaseInfo.Intensity) * mult)
                    {
                        //debugCode = 400;
                        //if (tracing && verboseDebug)
                        //    tracingBuffer.Add("\t" + entity.ToStringWithPlanet() + " has < " + GetMaxStrengthForCastle(entity) + " strength, so we get " + income + " metal, giving us a total of " + data.MetalStored).Add("\n");

                        //debugCode = 500;
                        string tag = GetTagForShips(entity, Context);
                        GameEntityTypeData entityData = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, tag);
                        if (entityData == null)
                        {
                            ArcenDebugging.ArcenDebugLogSingleLine("Failed to find entityData for " + entity.ToStringWithPlanet() + " that wants to build a " + tag, Verbosity.DoNotShow);
                            continue;
                        }
                        //debugCode = 600;
                        data.MetalStored -= entityData.CostForAIToPurchase;
                        if (data.ShipsInside[entityData] > 0)
                            data.ShipsInside[entityData]++;
                        else
                            data.ShipsInside[entityData] = 1;
                        //if (tracing && verboseDebug)
                        //    tracingBuffer.Add("\t\tAfter purchasing a " + entityData.GetDisplayName() + " we have " + data.MetalStored + " metal left\n");
                    }
                }
                data.UpdateShipsInside_ForUI(entity);
                //If there are enemies in range, spawn all ships inside us

                Planet planetToHelp = FindEnemiesInCastleRange(entity);
                data.PlanetDefenseWantsToHelp = planetToHelp; //this can be null
                if (planetToHelp != null)
                {
                    //debugCode = 1100;
                    localDefenseActive = true;
                    PlanetFaction pFaction = entity.PlanetFaction;
                    int shipsDeployed = 0;
                    data.ShipsInside.DoFor(delegate (KeyValuePair<GameEntityTypeData, int> pair)
                    {
                        //debugCode = 1200;
                        shipsDeployed += data.ShipsInside[pair.Key];
                        for (int j = 0; j < data.ShipsInside[pair.Key]; j++)
                        {
                            ArcenPoint spawnLocation = entity.Planet.GetSafePlacementPoint_AroundEntity(Context, pair.Key, entity, FInt.FromParts(0, 005), FInt.FromParts(0, 030));
                            int markLevel = (int)entity.CurrentMarkLevel;

                            GameEntity_Squad newEntity = GameEntity_Squad.CreateNew_ReturnNullIfMPClient(pFaction, pair.Key, (byte)markLevel,
                                                                                                          pFaction.Faction.LooseFleet, 0, spawnLocation, Context, "CityShips-ExistingDefenses");  //is fine, main sim thread
                            if (newEntity != null)
                            {
                                newEntity.ShouldNotBeConsideredAsThreatToHumanTeam = true; //just in case
                                newEntity.Orders.SetBehaviorDirectlyInSim(EntityBehaviorType.Attacker_Full); //is fine, main sim thread
                                CityShipsPerUnitBaseInfo newData = newEntity.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");
                                newData.DefenseMode = true;
                                newData.HomeDefense = LazyLoadSquadWrapper.Create(entity);
                            }
                        }

                        return DelReturn.Continue;
                    });
                    //if (tracing && shipsDeployed > 0)
                    //    tracingBuffer.Add("\t" + entity.ToStringWithPlanet() + " has detected nearby enemies on " + planetToHelp.Name + " and is spawning " + shipsDeployed + " ships.\n");


                    data.ShipsInside.Clear();
                }
                else
                {
                    //debugCode = 1300;
                    //If there are no enemies in range and we have a ship close to us, absorb it
                    //Note we can go over the limit here, or absorb random ships from other castles; that's fine.
                    int range = 2500;
                    for (int j = 0; j < defensesDefenders.Count; j++)
                    {
                        GameEntity_Squad ship = defensesDefenders[j].GetSquad();
                        if (ship == null)
                            continue;
                        if (ship.Planet != entity.Planet)
                            continue;
                        if (Mat.DistanceBetweenPointsImprecise(entity.WorldLocation, ship.WorldLocation) < range)
                        {
                            if (data.ShipsInside[ship.TypeData] > 0)
                                data.ShipsInside[ship.TypeData]++;
                            else
                                data.ShipsInside[ship.TypeData] = 1;
                            ship.Despawn(Context, true, InstancedRendererDeactivationReason.AFactionJustWarpedMeOut);
                        }
                    }
                }
                //select a target to go to
                if(data.target == -1 && data.planetToBuildOn == null) //if we have no target and no ongoing mission to build something
                {
                    //choose whether to make a defense or economic unit
                    data.toBuildDefenseOrEcon = Context.RandomToUse.Next(0, 2);
                    //either we don't have enough structures and 80% chance of success the roll and thus 10% of building a new thing anyways
                    if (BaseInfo.Defenses.Count >= 1 && BaseInfo.EconomicUnits.Count >= 1 && Context.RandomToUse.Next(10) <= 8)
                    {
                        if (data.toBuildDefenseOrEcon == 0) //defense
                        {
                            data.target = Context.RandomToUse.Next(BaseInfo.Defenses.Count);
                        }
                        else //economic unit
                        {
                            data.target = Context.RandomToUse.Next(BaseInfo.EconomicUnits.Count);  
                        }
                    }
                    else
                    {
                        //we have like no fucking structures to path to which is cringe
                        //prepare to make some
                        
                        entity.Planet.DoForPlanetsWithinXHops(-1, delegate (Planet planet, Int16 Distance)
                        {
                            if(!(planet.PopulationType == PlanetPopulationType.AIBastionWorld || planet.PopulationType == PlanetPopulationType.AIHomeworld))
                            {
                                //debugCode = 400;
                                var pFaction = planet.GetStanceDataForFaction(AttachedFaction);

                                if (data.toBuildDefenseOrEcon == 0) //we build defense
                                {
                                    for (int j = 0; j < defenses.Count; j++)
                                    {
                                        if (defenses[j].Planet == planet)
                                        {
                                            //if (tracing && verboseDebug)
                                            //    tracingBuffer.Add("\t\tNo, we have a castle already").Add("\n");

                                            return DelReturn.Continue;
                                        }
                                    }
                                }
                                else if (data.toBuildDefenseOrEcon == 1) //we build econ
                                {
                                    for (int j = 0; j < economicUnits.Count; j++)
                                    {
                                        if (economicUnits[j].Planet == planet)
                                        {
                                            //if (tracing && verboseDebug)
                                            //    tracingBuffer.Add("\t\tNo, we have a castle already").Add("\n");

                                            return DelReturn.Continue;
                                        }
                                    }
                                }
                                WorkingPlanetsList.Add(planet);
                                if (WorkingPlanetsList.Count > 5)
                                    return DelReturn.Break;
                            }
                            
                            /*for (int i = 0; i < constructors.Count; i++)
                            {
                                GameEntity_Squad ship = constructors[i].GetSquad();
                                if (ship == null)
                                    continue;
                                //make sure we don't have a constructor going here
                                TemplarPerUnitBaseInfo constructorData = ship.TryGetExternalBaseInfoAs<TemplarPerUnitBaseInfo>();
                                if (constructorData.PlanetIdx == planet.Index)
                                {
                                    if (tracing && verboseDebug)
                                        tracingBuffer.Add("\t\tNo, we have a constructor en route already").Add("\n");

                                    return DelReturn.Continue;
                                }
                            }*/

                            
                            return DelReturn.Continue;
                        });
                        if (WorkingPlanetsList.Count == 0)
                            continue;
                        //We prefer further away planets (To make sure the Templar expand rapidly, and also
                        //to give the player more chances to kill Constructors
                        WorkingPlanetsList.Sort(delegate (Planet L, Planet R)
                        {
                            //sort from "furthest away" to "closest"
                            int lHops = L.GetHopsTo(entity.Planet);
                            int rHops = R.GetHopsTo(entity.Planet);
                            return rHops.CompareTo(lHops);
                        });

                        for (int j = 0; j < WorkingPlanetsList.Count; j++)
                        {
                            if (Context.RandomToUse.Next(0, 100) < 40)
                                data.planetToBuildOn = WorkingPlanetsList[j];
                        }
                        data.planetToBuildOn = WorkingPlanetsList[0];
                    }
                }
                else //we check whether or not we can achieve our objective
                {
                    if (data.target != -1) //if our goal is steal candy
                    {
                        try //I use try here to catch exceptions related to the target disappearing
                        {
                            if (data.toBuildDefenseOrEcon == 0)
                            {
                                GameEntity_Squad target = BaseInfo.Defenses.GetDisplayList()[data.target].GetSquad();
                                if (target.TypeData.GetHasTag("CityShipsDefense") && Mat.DistanceBetweenPointsImprecise(entity.WorldLocation, target.WorldLocation) < 3300)
                                {
                                    //Spend metal, gain prosperity, and also give the defense some metal.
                                    CityShipsPerUnitBaseInfo targetData = target.GetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                                    targetData.Prosperity += data.MetalStored / 20;
                                    targetData.MetalStored += data.MetalStored / 5;
                                    data.MetalStored = 0;
                                    //stop moving
                                    GameCommand moveCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.MoveManyToOnePoint_NPCVisitTargetOnPlanet], GameCommandSource.AnythingElse);
                                    moveCommand.PlanetOrderWasIssuedFrom = entity.Planet.Index;
                                    moveCommand.RelatedPoints.Add(entity.WorldLocation);
                                    moveCommand.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                                    World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, moveCommand, false);
                                    //reset our data for next cycle
                                    data.toBuildDefenseOrEcon = 0;
                                    data.target = -1;
                                    data.planetToBuildOn = null;
                                    data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                                }
                            }
                            else
                            {
                                GameEntity_Squad target = BaseInfo.EconomicUnits.GetDisplayList()[data.target].GetSquad();
                                if (target.TypeData.GetHasTag("CityShipsEconomicUnit") && Mat.DistanceBetweenPointsImprecise(entity.WorldLocation, target.WorldLocation) < 2800)
                                {

                                    //Spend metal, gain prosperity, but then also steal some, and get the target's stockpiled metal.
                                    CityShipsPerUnitBaseInfo targetData = target.GetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                                    targetData.Prosperity += data.MetalStored / 20;
                                    data.MetalStored = data.MetalStored / 2; //do not spend all metal lest i get brain damage
                                    int transferredProsperity = Convert.ToInt32(targetData.Prosperity * 0.03);
                                    data.Prosperity += transferredProsperity; //3% prosperity is transferred from the target unit to the city ship
                                    targetData.Prosperity -= transferredProsperity;
                                    data.MetalStored += targetData.MetalStored;
                                    targetData.MetalStored = 0;
                                    //stop moving
                                    GameCommand moveCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.MoveManyToOnePoint_NPCVisitTargetOnPlanet], GameCommandSource.AnythingElse);
                                    moveCommand.PlanetOrderWasIssuedFrom = entity.Planet.Index;
                                    moveCommand.RelatedPoints.Add(entity.WorldLocation);
                                    moveCommand.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                                    World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, moveCommand, false);
                                    //reset our data for next cycle
                                    data.toBuildDefenseOrEcon = 0;
                                    data.target = -1;
                                    data.planetToBuildOn = null;
                                    data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                                }
                            }
                        }
                        catch //reset our stuff in case the target disappears
                        {
                            data.toBuildDefenseOrEcon = 0;
                            data.target = -1;
                            data.planetToBuildOn = null;
                            data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                        }
                        
                    }
                    else if(data.planetToBuildOn != null) //we are on a mission to build something
                    {
                        if(data.locationToBuildOn != ArcenPoint.ZeroZeroPoint)
                        {
                            if (Mat.DistanceBetweenPointsImprecise(entity.WorldLocation, data.locationToBuildOn) < 2800)
                            {
                                GameEntityTypeData unitToBuild;
                                PlanetFaction pFaction = entity.PlanetFaction;
                                if (data.toBuildDefenseOrEcon == 0)
                                {
                                    unitToBuild = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, "CityShipsOutpost");
                                }
                                else
                                {
                                    unitToBuild = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, "CityShipsVillage");
                                }
                                GameEntity_Squad newEntity = GameEntity_Squad.CreateNew_ReturnNullIfMPClient(pFaction, unitToBuild, (byte)1,
                                                                                                      pFaction.Faction.LooseFleet, 0, data.locationToBuildOn, Context, "CityShips-CityShip");
                                CityShipsPerUnitBaseInfo newData = newEntity.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");
                                newData.Prosperity = 1000;
                                newData.MetalStored = 0;
                                //reset our data  for next cycle
                                data.toBuildDefenseOrEcon = 0;
                                data.target = -1;
                                data.planetToBuildOn = null;
                                data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                                //stop moving
                                GameCommand moveCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.MoveManyToOnePoint_NPCVisitTargetOnPlanet], GameCommandSource.AnythingElse);
                                moveCommand.PlanetOrderWasIssuedFrom = entity.Planet.Index;
                                moveCommand.RelatedPoints.Add(entity.WorldLocation);
                                moveCommand.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                                World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, moveCommand, false);
                            }
                        }
                    }
                    else //something blew up so we're gonna reset
                    {
                        data.toBuildDefenseOrEcon = 0;
                        data.target = -1;
                        data.planetToBuildOn = null;
                        data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                    }
                }
                //HEHEHA formula go brrr
                if (data.Prosperity > ((17500 * (mult - 1)) + (2500 * entity.CurrentMarkLevel * mult)) && entity.CurrentMarkLevel != 7) //this is the mult we defined earlier
                {
                    entity.SetCurrentMarkLevel((byte)(entity.CurrentMarkLevel + 1));
                }
                if(data.Prosperity > 17500 * mult)
                {
                    //transform it
                    if (entity.TypeData.GetHasTag("CityShipsCityShipTier1"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsCityShipTier2");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                    if (entity.TypeData.GetHasTag("CityShipsCityShipTier2"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsCityShipTier3");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                    if (entity.TypeData.GetHasTag("CityShipsCityShipTier3"))
                    {
                        SpawnNewCityShipEntity(Context, false, entity, "CityShipsCityShipTier4");
                        entity.Despawn(Context, true, InstancedRendererDeactivationReason.IAmTransforming);
                    }
                }
            }
            BaseInfo.AnyDefensesActive = localDefenseActive;
        }

        public static readonly List<SafeSquadWrapper> UnassignedShipsLRP = List<SafeSquadWrapper>.Create_WillNeverBeGCed(150, "CityShipsFactionDeepInfo-UnassignedShipsLRP");
        public static readonly DictionaryOfLists<Planet, SafeSquadWrapper> UnassignedShipsByPlanetLRP = DictionaryOfLists<Planet, SafeSquadWrapper>.Create_WillNeverBeGCed(100, 60, "CityShipsFactionDeepInfo-UnassignedShipsByPlanetLRP");
        public static readonly DictionaryOfLists<Planet, SafeSquadWrapper> InCombatShipsNeedingOrdersLRP = DictionaryOfLists<Planet, SafeSquadWrapper>.Create_WillNeverBeGCed(100, 60, "CityShipsFactionDeepInfo-InCombatShipsNeedingOrdersLRP");
        public readonly List<SafeSquadWrapper> DefensesDefendersLRP = List<SafeSquadWrapper>.Create_WillNeverBeGCed(150, "CityShipsFactionDeepInfo-DefensesDefendersLRP");
        public readonly List<SafeSquadWrapper> DefensesDefendersRetreatingLRP = List<SafeSquadWrapper>.Create_WillNeverBeGCed(150, "CityShipsFactionDeepInfo-DefensesDefendersRetreatingLRP");
        public readonly List<SafeSquadWrapper> CityShipsLRP = List<SafeSquadWrapper>.Create_WillNeverBeGCed(150, "CityShipsFactionDeepInfo-CityShipsLRP");
        public static readonly List<SafeSquadWrapper> WorkingMetalGeneratorList = List<SafeSquadWrapper>.Create_WillNeverBeGCed(150, "CityShipsFactionDeepInfo-WorkingMetalGeneratorList");

        public override void DoLongRangePlanning_OnBackgroundNonSimThread_Subclass(ArcenLongTermIntermittentPlanningContext Context)
        {
            //bool tracing = this.tracing_longTerm = Engine_AIW2.TraceAtAll && Engine_AIW2.TracingFlags.Has(ArcenTracingFlags.CityShips);
            //ArcenCharacterBuffer tracingBuffer = tracing ? ArcenCharacterBuffer.GetFromPoolOrCreate("CityShips-DoLongRangePlanning_OnBackgroundNonSimThread_Subclass-trace", 10f) : null;
            //int debugCode = 0;
            PerFactionPathCache pathingCacheData = PerFactionPathCache.GetCacheForTemporaryUse_MustReturnToPoolAfterUseOrLeaksMemory();
            try
            {

                int highestDifficulty = FactionUtilityMethods.Instance.GetHighestAIDifficulty();
                //debugCode = 100;
                UnassignedShipsLRP.Clear();
                UnassignedShipsByPlanetLRP.Clear();
                InCombatShipsNeedingOrdersLRP.Clear();
                CityShipsLRP.Clear();
                DefensesDefendersLRP.Clear();
                DefensesDefendersRetreatingLRP.Clear();
                int totalShips = 0;
                int totalStrength = 0;
                //Iterate over all our units to figure out if any need orders
                AttachedFaction.DoForEntities(delegate (GameEntity_Squad entity)
                {
                    //debugCode = 200;
                    if (entity == null)
                        return DelReturn.Continue;

                    //debugCode = 101;
                    if (entity.TypeData.GetHasTag("CityShipsCityShip"))
                    {
                        //debugCode = 300;
                        CityShipsLRP.Add(entity);
                        return DelReturn.Continue;
                    }
                    if (!entity.TypeData.IsMobileCombatant)
                        return DelReturn.Continue;
                    CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                    if (data == null)
                        return DelReturn.Continue; //race against sim I expect
                    //debugCode = 400;
                    if (data.DefenseMode)
                    {
                        //We are defensive units
                        DefensesDefendersLRP.Add(entity);
                        return DelReturn.Continue;
                    }
                    //debugCode = 500;
                    totalShips++;
                    totalStrength += entity.GetStrengthOfSelfAndContents();
                    if (entity.HasQueuedOrders())
                    {
                        //debugCode = 600;
                        //entity is doing something already (either attacking a target or en route somewhere)
                        Planet eventualDestinationOrNull = entity.Orders.GetFinalDestinationOrNull();
                        if (eventualDestinationOrNull == null && entity.HasExplicitOrders())
                            return DelReturn.Continue; //we are going somewhere on this planet, that's not an FRD order (like we have specifically chosen a target), let this ship keep doing what it's doing
                        if (eventualDestinationOrNull != null)
                        {
                            var factionData = eventualDestinationOrNull.GetStanceDataForFaction(AttachedFaction);
                            if (factionData[FactionStance.Hostile].TotalStrength > 0 ||
                                 eventualDestinationOrNull.GetControllingFaction().GetIsHostileTowards(AttachedFaction))
                                return DelReturn.Continue; //if the planet we are going has enemies, keep going there
                        }
                    }

                    //debugCode = 700;

                    if (entity.Planet.GetControllingOrInfluencingFaction().GetIsHostileTowards(AttachedFaction))
                    {
                        //This entity is on an enemy planet but doesn't have orders to attack a specific valuable target
                        //This means we will always kill a command stations before moving on
                        //debugCode = 800;
                        InCombatShipsNeedingOrdersLRP[entity.Planet].Add(entity);

                        return DelReturn.Continue;
                    }
                    //okay, we're on a friendly planet instead
                    if (entity.Planet.GetControllingOrInfluencingFaction().GetIsFriendlyTowards(AttachedFaction))
                    {
                        var factionData = entity.Planet.GetStanceDataForFaction(AttachedFaction);
                        if (factionData[FactionStance.Hostile].TotalStrength > 3000) //if there is at least 3 strength there that we are enemies to
                        {
                            //This entity is on an allied planet with at least 3 enemy strength there, but doesn't have orders to attack a specific valuable target
                            //So go kill some dudes
                            //debugCode = 900;
                            InCombatShipsNeedingOrdersLRP[entity.Planet].Add(entity);

                            return DelReturn.Continue;
                        }
                    }
                    //This unit doesn't have any active orders and isn't on an enemy planet. Find an enemy planet
                    //debugCode = 1000;
                    UnassignedShipsLRP.Add(entity);
                    UnassignedShipsByPlanetLRP[entity.Planet].Add(entity);
                    return DelReturn.Continue;
                });
                //debugCode = 1100;
                HandleCityShipsLRP(Context, pathingCacheData);

                //debugCode = 1300;
                //if (tracing && totalShips > 0)
                //   tracingBuffer.Add(totalShips + " with strength " + (totalStrength / 1000) + " in the City Ships Assault right now.\n");

                //First, handle the ships not in combat
                //Here's the rule. First we pick our preferred planets (player or civil war AI planets), then we sort them
                //We prefer close and weak player planets.
                List<Planet> preferredTargets = Planet.GetTemporaryPlanetList("CityShips-DoLongRangePlanning_OnBackgroundNonSimThread_Subclass-preferredTargets", 10f);
                List<Planet> fallbackTargets = Planet.GetTemporaryPlanetList("CityShips-DoLongRangePlanning_OnBackgroundNonSimThread_Subclass-fallbackTargets", 10f);

                UnassignedShipsByPlanetLRP.DoFor(delegate (KeyValuePair<Planet, List<SafeSquadWrapper>> pair)
                {
                    //debugCode = 1400;
                    Planet startPlanet = pair.Key;
                    //if (tracing)
                    //    tracingBuffer.Add(pair.Value.Count + " ships on " + startPlanet.Name + " are looking for a target\n");
                    preferredTargets.Clear();
                    fallbackTargets.Clear();
                    //debugCode = 1500;
                    startPlanet.DoForPlanetsWithinXHops(-1, delegate (Planet planet, Int16 Distance)
                    {
                        //debugCode = 1600;
                        if (IsPreferredTarget(planet, Context))
                        {
                            preferredTargets.Add(planet);
                            return DelReturn.Continue;
                        }

                        if (IsFallbackTarget(planet, Context))
                            fallbackTargets.Add(planet);

                        return DelReturn.Continue;
                    }, delegate (Planet secondaryPlanet)
                    {
                        //debugCode = 1700;
                        //don't path through hostile planets.
                        if (secondaryPlanet.GetControllingOrInfluencingFaction().GetIsHostileTowards(AttachedFaction))
                            return PropogationEvaluation.SelfButNotNeighbors;
                        return PropogationEvaluation.Yes;
                    });
                    //debugCode = 1800;
                    /*if (tracing)
                    {
                        tracingBuffer.Add("\tWe have " + preferredTargets.Count + " preferred targets\n");
                        for (int i = 0; i < preferredTargets.Count; i++)
                            tracingBuffer.Add("\t\t" + preferredTargets[i].Name).Add("\n");
                        if (fallbackTargets.Count > 0)
                        {
                            tracingBuffer.Add("\tWe have " + fallbackTargets.Count + " fallback targets\n");
                            for (int i = 0; i < fallbackTargets.Count; i++)
                                tracingBuffer.Add("\t\t" + fallbackTargets[i].Name).Add("\n");
                        }
                    }*/
                    //debugCode = 1900;
                    //choose the target. First try to get a preferred planet
                    Planet target = null;
                    if (preferredTargets.Count > 0)
                    {
                        //debugCode = 2000;
                        //if (tracing)
                        //    tracingBuffer.Add("\tAttempting to pick a preferredTarget\n");
                        preferredTargets.Sort(delegate (Planet L, Planet R)
                        {
                            //debugCode = 2100;
                            //To sort the planets, we factor how scary a planet is and how far it is away. We prefer nearer and weaker targets
                            //TODO if desired: also say "if this has an AIP increaser, want to attack it a bit more"
                            //that would make it a tad more evil
                            var lFactionData = L.GetStanceDataForFaction(AttachedFaction);
                            var rFactionData = R.GetStanceDataForFaction(AttachedFaction);
                            int lhops = startPlanet.GetHopsTo(L);
                            int rhops = startPlanet.GetHopsTo(R);
                            int lEnemyStrength = lFactionData[FactionStance.Hostile].TotalStrength;
                            int rEnemyStrength = rFactionData[FactionStance.Hostile].TotalStrength;
                            int lFriendlyStrength = lFactionData[FactionStance.Friendly].TotalStrength +
                                lFactionData[FactionStance.Self].TotalStrength;
                            int rFriendlyStrength = rFactionData[FactionStance.Friendly].TotalStrength +
                                rFactionData[FactionStance.Self].TotalStrength;

                            int lstrength = lEnemyStrength - lFriendlyStrength;
                            int rstrength = rEnemyStrength - rFriendlyStrength;
                            FInt factorPerHop = FInt.FromParts(5, 000);
                            if (highestDifficulty > 7)
                                factorPerHop = FInt.One; //on higher difficulties, the AI prefers to focus exclusively on weaker targets
                            int lVal = (lhops * factorPerHop).IntValue * lstrength;
                            int rVal = (rhops * factorPerHop).IntValue * rstrength;

                            return lVal.CompareTo(rVal);
                        });
                        //debugCode = 2200;
                        for (int i = 0; i < preferredTargets.Count; i++)
                        {
                            int percentToUse = 50;
                            if (highestDifficulty > 7)
                                percentToUse = 80; //more focus on higher difficulties
                            if (Context.RandomToUse.Next(0, 100) < percentToUse)
                            {
                                //weighted choice
                                target = preferredTargets[i];
                                break;
                            }
                        }
                        if (target == null) //if we didn't pick one randomly, just take the best
                            target = preferredTargets[0];
                    }

                    //debugCode = 2300;
                    if (target == null && fallbackTargets.Count > 0)
                    {
                        //debugCode = 2400;
                        //this is very similar to the preferredTargets code above
                        //if (tracing)
                        //    tracingBuffer.Add("\tAttempting to pick a fallbackTarget\n");
                        fallbackTargets.Sort(delegate (Planet L, Planet R)
                        {
                            var lFactionData = L.GetStanceDataForFaction(AttachedFaction);
                            var rFactionData = R.GetStanceDataForFaction(AttachedFaction);
                            int lhops = startPlanet.GetHopsTo(L);
                            int rhops = startPlanet.GetHopsTo(R);
                            int lEnemyStrength = lFactionData[FactionStance.Hostile].TotalStrength;
                            int rEnemyStrength = rFactionData[FactionStance.Hostile].TotalStrength;
                            int lFriendlyStrength = lFactionData[FactionStance.Friendly].TotalStrength +
                                lFactionData[FactionStance.Self].TotalStrength;
                            int rFriendlyStrength = rFactionData[FactionStance.Friendly].TotalStrength +
                                rFactionData[FactionStance.Self].TotalStrength;

                            int lstrength = lEnemyStrength - lFriendlyStrength;
                            int rstrength = rEnemyStrength - rFriendlyStrength;
                            FInt factorPerHop = FInt.FromParts(0, 500);

                            int lVal = (lhops / factorPerHop).IntValue * lstrength;
                            int rVal = (rhops / factorPerHop).IntValue * rstrength;

                            return lVal.CompareTo(rVal);
                        });
                        for (int i = 0; i < fallbackTargets.Count; i++)
                        {
                            if (Context.RandomToUse.Next(0, 100) < 50)
                            {
                                target = fallbackTargets[i];
                                break;
                            }
                        }
                        if (target == null) //if we didn't pick one randomly, just take the best
                            target = fallbackTargets[0];
                    }


                    //debugCode = 2500;
                    if (target == null)
                        return DelReturn.Continue;
                    /*if (tracing)
                    {
                        tracingBuffer.Add("\tSending " + pair.Value.Count + " ships from " + startPlanet.Name + " to attack " + target.Name).Add("\n");
                    }
                    debugCode = 2600;*/
                    AttackTargetPlanet(startPlanet, target, pair.Value, Context, pathingCacheData);
                    return DelReturn.Continue;
                });

                Planet.ReleaseTemporaryPlanetList(preferredTargets);
                Planet.ReleaseTemporaryPlanetList(fallbackTargets);

                //debugCode = 3000;
                //This is a ship on a planet controlled by our enemies. Pick a target and go after it.
                List<SafeSquadWrapper> targetSquads = GameEntity_Squad.GetTemporarySquadList("CityShips-DoLongRangePlanning_OnBackgroundNonSimThread_Subclass-targetSquads", 10f);

                //debugCode = 3100;
                InCombatShipsNeedingOrdersLRP.DoFor(delegate (KeyValuePair<Planet, List<SafeSquadWrapper>> pair)
                {
                    //debugCode = 3200;
                    PlanetFaction pFaction = pair.Key.GetControllingPlanetFaction();
                    Planet planet = pair.Key;
                    targetSquads.Clear();
                    //if (tracing)
                    //    tracingBuffer.Add("Finding a target for " + pair.Value.Count + " ships on " + pair.Key.Name).Add(" if necessary\n");

                    int rand = Context.RandomToUse.Next(0, 100);
                    var factionData = planet.GetStanceDataForFaction(AttachedFaction);
                    int enemyStrength = factionData[FactionStance.Hostile].TotalStrength;
                    int friendlyStrength = factionData[FactionStance.Self].TotalStrength + factionData[FactionStance.Friendly].TotalStrength;

                    if (enemyStrength < friendlyStrength / 10) //Relentless waves can tachyon blast planets to prevent a small number of cloaked units from disrupting things. This is particularly possible with civilian industry cloaked defensive structures
                        FactionUtilityMethods.Instance.TachyonBlastPlanet(planet, AttachedFaction, Context);


                    if (planet.GetControllingFactionType() == FactionType.Player)
                    {
                        //This is a player planet, so lets see if we can do anything clever/sneaky

                        Planet kingPlanet = null;
                        if (FactionUtilityMethods.Instance.IsPlanetAdjacentToPlayerKing(planet, out kingPlanet))
                        {
                            //First, if we are adjacent to a player homeworld then go for that if we can
                            var neighborFactionData = kingPlanet.GetStanceDataForFaction(AttachedFaction);
                            StrengthData_PlanetFaction_Stance neighborHostileStrengthData = neighborFactionData[FactionStance.Hostile];
                            int neighborHostileStrengthTotal = neighborHostileStrengthData.TotalStrength;
                            if (neighborHostileStrengthTotal < (friendlyStrength) / 2)
                            {
                                //if we are too much weaker than the AI homeworld, don't bother. Only if we might make things interesting
                                GameEntity_Other thisWormhole = planet.GetWormholeTo(kingPlanet);
                                bool foundBlockingShield = FactionUtilityMethods.Instance.IsShieldBlockingWormholeToPlanet(planet, kingPlanet, AttachedFaction);
                                if (!foundBlockingShield)
                                {
                                    //if (tracing)
                                    //    tracingBuffer.Add("\tWe are going to attack the player king on ").Add(kingPlanet.Name).Add("\n");

                                    GameCommand sneakCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetWormholePath_AIRaidKing], GameCommandSource.AnythingElse);
                                    //debugCode = 3300;
                                    for (int j = 0; j < pair.Value.Count; j++)
                                    {
                                        sneakCommand.RelatedEntityIDs.Add(pair.Value[j].PrimaryKeyID);
                                    }

                                    //debugCode = 3400;
                                    if (sneakCommand != null && sneakCommand.RelatedEntityIDs.Count > 0)
                                    {
                                        sneakCommand.RelatedString = "AI_WAVE_GOKING";
                                        sneakCommand.ToBeQueued = false;
                                        sneakCommand.RelatedIntegers.Add(kingPlanet.Index);
                                        World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, sneakCommand, playAudioEffectForCommand);
                                        return DelReturn.Continue;
                                    }
                                }
                            }
                        }
                        if (Context.RandomToUse.Next(0, 100) < 25)
                        {
                            //debugCode = 3500;
                            GameEntity_Squad target = planet.GetControllingCityCenterOrNull() ?? planet.GetCommandStationOrNull();
                            if (target != null)
                            {
                                //debugCode = 3600;
                                GameCommand commandStationAttackCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.Attack], GameCommandSource.AnythingElse);
                                commandStationAttackCommand.ToBeQueued = false;

                                commandStationAttackCommand.RelatedIntegers4.Add(target.PrimaryKeyID);
                                //debugCode = 3700;
                                for (int j = 0; j < pair.Value.Count; j++)
                                {
                                    commandStationAttackCommand.RelatedEntityIDs.Add(pair.Value[j].PrimaryKeyID);
                                }
                                //debugCode = 3800;
                                World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, commandStationAttackCommand, playAudioEffectForCommand);
                                //if (tracing) tracingBuffer.Add("\n").Add("Threat of " + commandStationAttackCommand.RelatedEntityIDs.Count + " units attacking command station");
                                commandStationAttackCommand = null;
                                return DelReturn.Continue;
                            }
                        }
                        if (enemyStrength > friendlyStrength)
                        {
                            //if we don't think we can comfortably win this (and remember, we are at a disadvantage when attacking a player)
                            //then see if we can bypass and find a weaker target
                            //debugCode = 3900;
                            rand = Context.RandomToUse.Next(0, 100); //recalculate the random number
                            if (rand < 50)
                            {
                                //debugCode = 4000;
                                //If this planet seems pretty tough for me, see if there are any adjacent weaker player planets and go for those
                                //TODO: we should actually use a List here and select randomly in case there are multiple good options
                                //We might also want to enhance Helper_RetreatThreat to incorporate this style of 'sneaking past player defenses'
                                Planet newTarget = null;
                                int weakestPlanetNeighborStrength = 0;
                                planet.DoForLinkedNeighbors(false, delegate (Planet neighbor)
                                {
                                    if (neighbor.GetControllingFactionType() != FactionType.Player)
                                        return DelReturn.Continue;
                                    //debugCode = 4100;
                                    var neighborFactionData = neighbor.GetStanceDataForFaction(AttachedFaction);
                                    StrengthData_PlanetFaction_Stance neighborHostileStrengthData = neighborFactionData[FactionStance.Hostile];
                                    int neighborHostileStrengthTotal = neighborHostileStrengthData.TotalStrength - friendlyStrength;
                                    if (neighborHostileStrengthTotal > enemyStrength)
                                        return DelReturn.Continue; //not interested in more heavily defeneded planets

                                    //debugCode = 4200;
                                    if (neighborHostileStrengthTotal < weakestPlanetNeighborStrength && !FactionUtilityMethods.Instance.IsShieldBlockingWormholeToPlanet(planet, neighbor, AttachedFaction))
                                    {
                                        weakestPlanetNeighborStrength = neighborHostileStrengthTotal;
                                        newTarget = neighbor;
                                    }
                                    return DelReturn.Continue;
                                });
                                //debugCode = 4300;
                                if (newTarget != null)
                                {
                                    //debugCode = 4400;
                                    //if (tracing) tracingBuffer.Add("\n").Add("Threat bypass to weaker planet: " + newTarget.Name).Add("\n");
                                    GameCommand sneakCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetWormholePath_AIRaidKing], GameCommandSource.AnythingElse);
                                    for (int j = 0; j < pair.Value.Count; j++)
                                    {
                                        sneakCommand.RelatedEntityIDs.Add(pair.Value[j].PrimaryKeyID);
                                    }

                                    //debugCode = 4500;
                                    if (sneakCommand.RelatedEntityIDs.Count > 0)
                                    {
                                        sneakCommand.RelatedString = "AI_T_CHUNKS";
                                        sneakCommand.ToBeQueued = false;
                                        sneakCommand.RelatedIntegers.Add(newTarget.Index);
                                        World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, sneakCommand, playAudioEffectForCommand);
                                        sneakCommand = null;
                                        return DelReturn.Continue;
                                    }
                                }
                            }
                        }
                    }
                   /* if (tracing)
                        tracingBuffer.Add("No fancy behaviours. Find some targets\n");
                    debugCode = 4600;*/
                    //We haven't done any of the fancy behaviours, so the default behaviour is "Just fight"
                    //Exception: if the player has anything particularly toothsome on this planet, always kill that first
                    planet.DoForEntities(EntityRollupType.KingUnitsOnly, delegate (GameEntity_Squad entity)
                    {
                        //debugCode = 4610;
                        if (!entity.PlanetFaction.Faction.GetIsHostileTowards(AttachedFaction))
                            return DelReturn.Continue;
                        targetSquads.Add(entity);
                        return DelReturn.Break; ;
                    });
                    //debugCode = 4620;
                    if (targetSquads.Count == 0)
                    {
                        //debugCode = 4630;
                        if (planet == null)
                            ArcenDebugging.ArcenDebugLogSingleLine("??", Verbosity.DoNotShow);
                        //if we already had a target then it's a king, so don't bother
                        planet.DoForEntities(EntityRollupType.AIPOnDeath, delegate (GameEntity_Squad entity)
                        {
                            //debugCode = 4640;
                            if (!entity.PlanetFaction.Faction.GetIsHostileTowards(AttachedFaction))
                                return DelReturn.Continue;

                            targetSquads.Add(entity);
                            return DelReturn.Continue;
                        });
                    }
                    if (targetSquads.Count > 0)
                    {
                        //we know we have a tasty target
                        //debugCode = 4700;
                        GameEntity_Squad target = targetSquads[Context.RandomToUse.Next(0, targetSquads.Count)].GetSquad();
                        if (target != null)
                        {
                            /*if (tracing)
                            {
                                tracingBuffer.Add("We are attacking " + target.ToStringWithPlanet() + ", one of " + targetSquads.Count + " target(s).\n");
                                bool debug = false;
                                if (tracing && debug)
                                {
                                    for (int i = 0; i < targetSquads.Count; i++)
                                    {
                                        tracingBuffer.Add(i + ": " + targetSquads[i].ToStringWithPlanet() + ".\n");
                                    }
                                }
                            }*/

                            GameCommand attackCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.Attack], GameCommandSource.AnythingElse);
                            attackCommand.RelatedIntegers4.Add(target.PrimaryKeyID);
                            for (int j = 0; j < pair.Value.Count; j++)
                            {
                                attackCommand.RelatedEntityIDs.Add(pair.Value[j].PrimaryKeyID);
                            }

                            World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, attackCommand, false);
                            return DelReturn.Continue;
                        }
                    }

                    if (rand < 25)
                    {
                        //The AI is allowed to send its fast and cloaked units after your metal generators
                        GameEntity_Squad target = FactionUtilityMethods.Instance.GetRandomMetalGeneratorOnPlanet(planet, Context, WorkingMetalGeneratorList, true);
                        if (target == null)
                            return DelReturn.Continue;
                        GameCommand attackCommand = null;
                        for (int j = 0; j < pair.Value.Count; j++)
                        {
                            GameEntity_Squad entity = pair.Value[j].GetSquad();
                            if (entity == null)
                                continue;
                            if (entity.GetMaxCloakingPoints() > 0 ||
                                 entity.CalculateSpeed(true) >= 1300)
                            {
                                if (attackCommand == null)
                                    attackCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.Attack], GameCommandSource.AnythingElse);
                                attackCommand.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                            }
                        }
                        if (attackCommand != null)
                        {
                            //if (tracing)
                            //    tracingBuffer.Add(attackCommand.RelatedEntityIDs.Count + " Fast/cloaked units are going after " + target.ToStringWithPlanet());
                            attackCommand.RelatedIntegers4.Add(target.PrimaryKeyID);
                            World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, attackCommand, false);
                        }
                    }
                    //if (tracing)
                    //    tracingBuffer.Add("Very boring; just fight very genericallys\n");

                    {
                        GameCommand attackCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetBehavior_FromFaction_NoPraetorianGuard], GameCommandSource.AnythingElse);
                        attackCommand.RelatedMagnitude = (int)EntityBehaviorType.Attacker_Full;
                        for (int j = 0; j < pair.Value.Count; j++)
                        {
                            attackCommand.RelatedEntityIDs.Add(pair.Value[j].PrimaryKeyID);
                        }
                    }

                    return DelReturn.Continue;
                });

                GameEntity_Squad.ReleaseTemporarySquadList(targetSquads);

                if (DefensesDefendersLRP.Count > 0)
                {
                    //if (tracing)
                    //    tracingBuffer.Add("We have " + CastleDefendersLRP.Count + " defense ships. AnyCastlesActive: " + BaseInfo.AnyCastlesActive + "\n");
                    if (!BaseInfo.AnyDefensesActive)
                    {
                        DefensesDefendersRetreatingLRP.Clear();
                        DefensesDefendersRetreatingLRP.AddRange(DefensesDefendersLRP);
                    }
                    else
                    {
                        //Lets find some enemies to fight!
                        for (int i = 0; i < DefensesDefendersLRP.Count; i++)
                        {
                            //debugCode = 300;
                            GameEntity_Squad entity = DefensesDefendersLRP[i].GetSquad();
                            if (entity == null)
                                continue;
                            CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                            GameEntity_Squad defense = data.HomeDefense.GetSquad();
                            if (defense == null)
                            {
                                if(BaseInfo.CityShips.GetDisplayList().Count != 0)
                                {
                                    //try to rally to the city ship (or a random one).
                                    data.HomeDefense = LazyLoadSquadWrapper.Create(BaseInfo.CityShips.GetDisplayList()[Context.RandomToUse.Next(BaseInfo.CityShips.Count)].GetSquad());
                                    defense = data.HomeDefense.GetSquad();
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            if (entity.HasQueuedOrders())
                                continue; //we're going someplace, so just go
                            CityShipsPerUnitBaseInfo defenseData = defense.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                            if (defenseData == null)
                            {
                                //ArcenDebugging.ArcenDebugLogSingleLine("confusingly, we have no castleData for " + castle.ToStringWithPlanet(), Verbosity.DoNotShow );
                                //Unclear why we have this happen sometimes?
                                continue;
                            }
                            Planet planetToHelp = defenseData.PlanetDefenseWantsToHelp;
                            if (planetToHelp == null)
                            {
                                DefensesDefendersRetreatingLRP.Clear();
                                DefensesDefendersRetreatingLRP.Add(entity);
                                continue;
                            }
                            //we have a ship without orders that wants to go to a planet
                            if (entity.Planet != planetToHelp)
                            {
                                PathBetweenPlanetsForFaction pathCache = PathingHelper.FindPathFreshOrFromCache(AttachedFaction, "CityShipsLRP", entity.Planet, planetToHelp, PathingMode.Default, Context, pathingCacheData);
                                if (pathCache != null && pathCache.PathToReadOnly.Count > 0)
                                {
                                    GameCommand command = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetWormholePath_NPCSingleSpecialMission], GameCommandSource.AnythingElse);
                                    //debugCode = 1000;
                                    command.RelatedString = "Tmp_ToAttack";
                                    command.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                                    command.ToBeQueued = false;
                                    for (int k = 0; k < pathCache.PathToReadOnly.Count; k++)
                                        command.RelatedIntegers.Add(pathCache.PathToReadOnly[k].Index);
                                    World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, command, false);
                                }
                            }
                        }
                    }
                    if (DefensesDefendersRetreatingLRP != null &&
                         DefensesDefendersRetreatingLRP.Count > 0)
                    {
                    /*    if (tracing)
                            tracingBuffer.Add("To retreat: " + CastleDefendersRetreatingLRP.Count + " ships\n");*/
                        ReturnShipsToCastles(DefensesDefendersRetreatingLRP, Context, pathingCacheData);
                    }

                }

            }
            catch (Exception e)
            {
                //ArcenDebugging.ArcenDebugLogSingleLine("Hit exception in CityShipsLogic LRP. debugCode " + debugCode + " " + e.ToString(), Verbosity.ShowAsError);
            }
            finally
            {
                FactionUtilityMethods.Instance.FlushUnitsFromReinforcementPointsOnAllRelevantPlanets(AttachedFaction, Context, 5f);
                pathingCacheData.ReturnToPool();

                /*if (tracing)
                {
                    #region Tracing
                    if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine("CityShipsLogic " + AttachedFaction.FactionIndex + ". " + tracingBuffer.ToString(), Verbosity.DoNotShow);
                    if (tracing)
                    {
                        tracingBuffer.ReturnToPool();
                        tracingBuffer = null;
                    }
                    #endregion
                }*/
            }
        }
        public void HandleCityShipsLRP(ArcenLongTermIntermittentPlanningContext Context, PerFactionPathCache PathCacheData)
        {
            int debugCode = 0;
            try
            {
                debugCode = 100;
                for (int i = 0; i < CityShipsLRP.Count; i++)
                {
                    debugCode = 200;
                    GameEntity_Squad entity = CityShipsLRP[i].GetSquad();
                    if (entity == null)
                        continue;
                    CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                    if (entity.HasQueuedOrders())
                        continue;
                    if(data.target != -1)
                    {
                        try
                        {
                            GameEntity_Squad target;
                            if (data.toBuildDefenseOrEcon == 0)
                            {
                                target = BaseInfo.Defenses.GetDisplayList()[data.target].GetSquad();
                            }
                            else
                            {
                                target = BaseInfo.EconomicUnits.GetDisplayList()[data.target].GetSquad();
                            }
                            Planet destPlanet = World_AIW2.Instance.GetPlanetByIndex((short)target.Planet.Index);
                            if (destPlanet == null)
                                continue; //should only be by race with sim
                            if (destPlanet != entity.Planet)
                                SendShipToPlanet(entity, destPlanet, Context, PathCacheData);//go to the planet
                            else
                            {
                                SendShipToLocation(entity, target.WorldLocation, Context);
                            }
                        }
                        catch
                        {
                            data.toBuildDefenseOrEcon = 0;
                            data.target = -1;
                            data.planetToBuildOn = null;
                            data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                        }
                        
                    }
                    else if(data.planetToBuildOn != null)
                    {
                        Planet destPlanet = World_AIW2.Instance.GetPlanetByIndex((short)data.planetToBuildOn.Index);
                        if (destPlanet == null)
                            continue; //should only be by race with sim
                        if (destPlanet != entity.Planet)
                        {
                            SendShipToPlanet(entity, destPlanet, Context, PathCacheData);//go to the planet
                            data.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
                        }  
                        if(destPlanet == entity.Planet && data.locationToBuildOn == ArcenPoint.ZeroZeroPoint)
                        {
                            GameEntityTypeData unitToBuild;
                            if (data.toBuildDefenseOrEcon == 0)
                            {
                                unitToBuild = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, "CityShipsOutpost");
                            }
                            else
                            {
                                unitToBuild = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, "CityShipsVillage");
                            }
                            data.locationToBuildOn = entity.Planet.GetSafePlacementPoint_AroundEntity(Context, unitToBuild, entity, FInt.FromParts(0, 200), FInt.FromParts(0, 650));
                        }
                        if(data.locationToBuildOn != ArcenPoint.ZeroZeroPoint)
                        {
                            SendShipToLocation(entity, data.locationToBuildOn, Context);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ArcenDebugging.ArcenDebugLogSingleLine("Hit exception in HandleConstructorsLRP debugCode " + debugCode + " " + e.ToString(), Verbosity.DoNotShow);
            }
        }
        public void SendShipToPlanet(GameEntity_Squad entity, Planet destination, ArcenLongTermIntermittentPlanningContext Context, PerFactionPathCache PathCacheData)
        {
            PathBetweenPlanetsForFaction pathCache = PathingHelper.FindPathFreshOrFromCache(entity.PlanetFaction.Faction, "CityShipsSendShipToPlanet", entity.Planet, destination, PathingMode.Shortest, Context, PathCacheData);
            if (pathCache != null && pathCache.PathToReadOnly.Count > 0)
            {
                GameCommand command = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetWormholePath_NPCSingleUnit], GameCommandSource.AnythingElse);
                command.RelatedString = "CityShips_Dest";
                command.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                for (int k = 0; k < pathCache.PathToReadOnly.Count; k++)
                    command.RelatedIntegers.Add(pathCache.PathToReadOnly[k].Index);
                World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, command, false);
            }
        }
        public void SendShipToLocation(GameEntity_Squad entity, ArcenPoint dest, ArcenLongTermIntermittentPlanningContext Context)
        {
            GameCommand moveCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.MoveManyToOnePoint_NPCVisitTargetOnPlanet], GameCommandSource.AnythingElse);
            moveCommand.PlanetOrderWasIssuedFrom = entity.Planet.Index;
            moveCommand.RelatedPoints.Add(dest);
            moveCommand.RelatedEntityIDs.Add(entity.PrimaryKeyID);
            World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, moveCommand, false);
        }
        public bool IsPreferredTarget(Planet planet, ArcenLongTermIntermittentPlanningContext Context)
        {
            //Whether this is an enemy controlled planet. Note that we try not to overkill planets too badly
            bool tracing = this.tracing_longTerm = Engine_AIW2.TraceAtAll && Engine_AIW2.TracingFlags.Has(ArcenTracingFlags.Templar);
            ArcenCharacterBuffer tracingBuffer = tracing ? ArcenCharacterBuffer.GetFromPoolOrCreate("CityShips-IsPreferredTarget-trace", 10f) : null;
            bool debug = false;
            if (tracing && debug)
                tracingBuffer.Add("\tchecking " + planet.Name + " for preferredness\n");
            Faction controllingFaction = planet.GetControllingFaction();
            bool foundNecro = false;
            planet.DoForEntities("Necropolis", delegate (GameEntity_Squad necro)
            {
                if (necro != null)
                {
                    foundNecro = true;
                    return DelReturn.Break;
                }
                return DelReturn.Continue;
            });

            bool playerOwned = controllingFaction.Type == FactionType.Player || foundNecro;
            if (!playerOwned)
            {
                if (tracing && debug)
                    tracingBuffer.Add("\t\tNot owned by primary enemy (ie player). Owned by " + controllingFaction.GetDisplayName() + "\n");
                #region Tracing
                if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
                if (tracing)
                {
                    tracingBuffer.ReturnToPool();
                    tracingBuffer = null;
                }
                #endregion
                return false;
            }

            if (!controllingFaction.GetIsHostileTowards(AttachedFaction) && !foundNecro)
            {
                if (tracing && debug)
                    tracingBuffer.Add("\t\t" + controllingFaction.GetDisplayName() + " is friendly\n");
                #region Tracing
                if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
                if (tracing)
                {
                    tracingBuffer.ReturnToPool();
                    tracingBuffer = null;
                }
                #endregion
                return false;
            }
            var factionData = planet.GetStanceDataForFaction(AttachedFaction);
            int enemyStrength = factionData[FactionStance.Hostile].TotalStrength;
            int friendlyStrength = factionData[FactionStance.Friendly].TotalStrength + factionData[FactionStance.Self].TotalStrength;
            if (enemyStrength * 5 < friendlyStrength)
            {
                if (tracing && debug)
                    tracingBuffer.Add("\t\t" + enemyStrength + " < " + (friendlyStrength * 5) + ", so discard\n");
                #region Tracing
                if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
                if (tracing)
                {
                    tracingBuffer.ReturnToPool();
                    tracingBuffer = null;
                }
                #endregion
                #region Tracing
                if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
                if (tracing)
                {
                    tracingBuffer.ReturnToPool();
                    tracingBuffer = null;
                }
                #endregion
                return false; //if we outnumber 7 to 1, don't bother attacking
            }
            return true;
        }
        public bool IsFallbackTarget(Planet planet, ArcenLongTermIntermittentPlanningContext Context)
        {
            bool tracing = this.tracing_longTerm = Engine_AIW2.TraceAtAll && Engine_AIW2.TracingFlags.Has(ArcenTracingFlags.Templar);
            ArcenCharacterBuffer tracingBuffer = tracing ? ArcenCharacterBuffer.GetFromPoolOrCreate("CityShips-IsFallbackTarget-trace", 10f) : null;
            Faction controllingFaction = planet.GetControllingOrInfluencingFaction();

            if (!controllingFaction.GetIsHostileTowards(AttachedFaction))
            {
                if (tracing)
                {
                    tracingBuffer.ReturnToPool();
                    tracingBuffer = null;
                }
                return false;
            }

            var factionData = planet.GetStanceDataForFaction(AttachedFaction);
            int enemyStrength = factionData[FactionStance.Hostile].TotalStrength;
            int friendlyStrength = factionData[FactionStance.Friendly].TotalStrength + factionData[FactionStance.Self].TotalStrength;
            #region Tracing
            if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
            if (tracing)
            {
                tracingBuffer.ReturnToPool();
                tracingBuffer = null;
            }
            #endregion

            if (enemyStrength < friendlyStrength * 7)
            {
                return false; //if we outnumber 7 to 1, don't bother attacking
            }

            return true;
        }
        public void AttackTargetPlanet(Planet start, Planet destination, List<SafeSquadWrapper> ships, ArcenLongTermIntermittentPlanningContext Context, PerFactionPathCache PathCacheData)
        {
            if (ships.Count <= 0)
                return;
            int debugCode = 0;
            try
            {
                bool tracing = this.tracing_longTerm = Engine_AIW2.TraceAtAll && Engine_AIW2.TracingFlags.Has(ArcenTracingFlags.Templar);
                ArcenCharacterBuffer tracingBuffer = tracing ? ArcenCharacterBuffer.GetFromPoolOrCreate("CityShips-AttackTargetPlanet-trace", 10f) : null;
                debugCode = 100;
                PathBetweenPlanetsForFaction pathCache = PathingHelper.FindPathFreshOrFromCache(AttachedFaction, "CityShipsAttackTargetPlanet", start, destination, PathingMode.Safest, Context, PathCacheData);
                int gatheringDistance = -1;
                debugCode = 200;
                if (pathCache != null && pathCache.PathToReadOnly.Count > 0)
                {
                    debugCode = 400;
                    if (tracing)
                        tracingBuffer.Add("\tpath count " + pathCache.PathToReadOnly.Count + " and gatheringDistance " + gatheringDistance);
                    debugCode = 500;
                    if (pathCache.PathToReadOnly.Count > gatheringDistance || gatheringDistance == -1)
                    {
                        debugCode = 600;
                        //if we are coming from a long ways away, fly a little faster so the CityShips are less spread out
                        //don't particularly care about speed limits here
                        //int speedToUse = CalculateSpeed(ships, Context);
                        GameCommand speedCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.CreateSpeedGroup_FireteamAttack], GameCommandSource.AnythingElse);
                        for (int j = 0; j < ships.Count; j++)
                            speedCommand.RelatedEntityIDs.Add(ships[j].PrimaryKeyID);
                        //int exoGroupSpeed = speedToUse;
                        speedCommand.RelatedBool = true;
                        World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, speedCommand, false);
                    }
                    debugCode = 700;
                    GameCommand command = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetWormholePath_UtilRaidSpecific], GameCommandSource.AnythingElse);
                    command.RelatedString = "CityShips_Planetary_Movement";
                    for (int k = 0; k < ships.Count; k++)
                        command.RelatedEntityIDs.Add(ships[k].PrimaryKeyID);
                    //determine whether we are actually going to the target, or just getting close so we are ready to strike
                    debugCode = 800;
                    if (gatheringDistance == -1)
                    {
                        debugCode = 900;
                        if (tracing)
                            tracingBuffer.Add("\tJust attack the planet you are on now, please\n");

                        for (int k = 0; k < pathCache.PathToReadOnly.Count; k++)
                            command.RelatedIntegers.Add(pathCache.PathToReadOnly[k].Index);
                    }
                    else
                    {
                        debugCode = 1000;
                        int hopsForwardToMove = pathCache.PathToReadOnly.Count - gatheringDistance;
                        if (hopsForwardToMove >= pathCache.PathToReadOnly.Count || hopsForwardToMove < 0)
                            throw new Exception("huh?");
                        if (tracing)
                            tracingBuffer.Add("\tMove forward only " + hopsForwardToMove + " hops from " + start.Name + " -> " + pathCache.PathToReadOnly[hopsForwardToMove - 1].Name + " en route to " + destination.Name + "\n");
                        debugCode = 1100;
                        for (int k = 0; k < hopsForwardToMove; k++)
                            command.RelatedIntegers.Add(pathCache.PathToReadOnly[k].Index);
                    }

                    World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, command, false);
                }
                #region Tracing
                if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
                if (tracing)
                {
                    tracingBuffer.ReturnToPool();
                    tracingBuffer = null;
                }
                #endregion

            }
            catch (Exception e)
            {
                ArcenDebugging.ArcenDebugLogSingleLine("Hit exception in CityShips AttackTargetPlanet debugCode " + debugCode + " " + e.ToString(), Verbosity.ShowAsError);
            }
        }

        public static readonly DictionaryOfLists<SafeSquadWrapper, SafeSquadWrapper> GoingHome = DictionaryOfLists<SafeSquadWrapper, SafeSquadWrapper>.Create_WillNeverBeGCed(40, 40, "CityShipsFactionDeepInfo-GoingHome");
        public void ReturnShipsToCastles(List<SafeSquadWrapper> ships, ArcenLongTermIntermittentPlanningContext Context, PerFactionPathCache PathCacheData)
        {
            bool tracing = this.tracing_longTerm = Engine_AIW2.TraceAtAll && Engine_AIW2.TracingFlags.Has(ArcenTracingFlags.Templar);
            ArcenCharacterBuffer tracingBuffer = tracing ? ArcenCharacterBuffer.GetFromPoolOrCreate("CityShips-ReturnShipsToCastles-trace", 10f) : null;
            int debugCode = 0;
            try
            {
                debugCode = 100;
                GoingHome.Clear();
                if (tracing)
                    tracingBuffer.Add("We have " + ships.Count + " ships that need to return to castles.\n");

                //This isn't the most efficient code, but it's hopefully run infrequently (only when under attack) and without too many ships
                for (int i = 0; i < ships.Count; i++)
                {
                    debugCode = 200;
                    GameEntity_Squad entity = ships[i].GetSquad();
                    if (entity == null)
                        continue;

                    CityShipsPerUnitBaseInfo data = entity.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                    GameEntity_Squad defense = data.HomeDefense.GetSquad();
                    if (defense == null)
                    {
                        if (BaseInfo.CityShips.GetDisplayList().Count != 0)
                        {
                            //try to rally to the city ship (or a random one).
                            data.HomeDefense = LazyLoadSquadWrapper.Create(BaseInfo.CityShips.GetDisplayList()[Context.RandomToUse.Next(BaseInfo.CityShips.Count)].GetSquad());
                            defense = data.HomeDefense.GetSquad();
                        }
                        else
                        {
                            continue;
                        }
                    }
                    GoingHome.Get(defense).Add(entity);
                }
                debugCode = 300;
                bool verboseDebug = false;
                GoingHome.DoFor(delegate (KeyValuePair<SafeSquadWrapper, List<SafeSquadWrapper>> pair)
                {
                    debugCode = 400;
                    if (tracing && verboseDebug)
                        tracingBuffer.Add("We have " + pair.Value.Count + " ships that need to return to " + pair.Key.ToStringWithPlanet() + ".\n");

                    //can be batched with some work if necessary
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        debugCode = 500;
                        GameEntity_Squad defense = pair.Key.GetSquad();
                        if (defense == null)
                            break;
                        GameEntity_Squad entity = pair.Value[i].GetSquad();
                        if (entity == null)
                            continue;
                        if (entity.Planet != defense.Planet &&
                             entity.Orders != null && entity.Orders.GetFinalDestinationOrNull() == defense.Planet)
                            continue; //we're already going to the planet we want to

                        if (entity.Planet == defense.Planet && !entity.HasQueuedOrders())
                        {
                            GameCommand moveCommand = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.MoveManyToOnePoint_NPCVisitTargetOnPlanet], GameCommandSource.AnythingElse);
                            moveCommand.PlanetOrderWasIssuedFrom = entity.Planet.Index;
                            moveCommand.RelatedPoints.Add(defense.WorldLocation);
                            moveCommand.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                            World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, moveCommand, false);
                        }
                        else
                        {
                            PathBetweenPlanetsForFaction pathCache = PathingHelper.FindPathFreshOrFromCache(AttachedFaction, "CityShipsReturnShipsToCastles", entity.Planet, defense.Planet, PathingMode.Default, Context, PathCacheData);
                            if (pathCache != null && pathCache.PathToReadOnly.Count > 0)
                            {
                                GameCommand command = GameCommand.Create(BaseGameCommand.CommandsByCode[BaseGameCommand.Code.SetWormholePath_NPCSingleSpecialMission], GameCommandSource.AnythingElse);
                                debugCode = 600;
                                command.RelatedString = "Spr_ToDefense";
                                command.RelatedEntityIDs.Add(entity.PrimaryKeyID);
                                command.ToBeQueued = false;
                                for (int k = 0; k < pathCache.PathToReadOnly.Count; k++)
                                    command.RelatedIntegers.Add(pathCache.PathToReadOnly[k].Index);
                                World_AIW2.Instance.QueueGameCommand(this.AttachedFaction, command, false);
                            }
                        }
                    }
                    return DelReturn.Continue;
                });
                debugCode = 700;
            }
            catch (Exception e)
            {
                ArcenDebugging.ArcenDebugLogSingleLine("Hit exception in ReturnShipsToCastles debugCode " + debugCode + " " + e.ToString(), Verbosity.DoNotShow);
            }
            #region Tracing
            if (tracing && !tracingBuffer.GetIsEmpty()) ArcenDebugging.ArcenDebugLogSingleLine(tracingBuffer.ToString(), Verbosity.DoNotShow);
            if (tracing)
            {
                tracingBuffer.ReturnToPool();
                tracingBuffer = null;
            }
            #endregion
        }
        public void SpawnNewCityShipEntity(ArcenHostOnlySimContext Context, bool debug, GameEntity_Squad entity, string entityToSpawnTag)
        {
            CityShipsPerUnitBaseInfo tData = entity.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");

            GameEntityTypeData entityData;
            entityData = GameEntityTypeDataTable.Instance.GetRandomRowWithTag(Context, entityToSpawnTag);
            ArcenPoint spawnLocation = entity.WorldLocation;
            PlanetFaction pFaction = entity.Planet.GetPlanetFactionForFaction(AttachedFaction);
            if (entityData == null)
                ArcenDebugging.ArcenDebugLogSingleLine("BUG: no " + entityToSpawnTag + " tag found", Verbosity.DoNotShow);
            GameEntity_Squad newEntity = GameEntity_Squad.CreateNew_ReturnNullIfMPClient(pFaction, entityData, 1,
                pFaction.FleetUsedAtPlanet, 0, spawnLocation, Context, "CtyShipGenericSpawn");
            if (newEntity == null)
                return;

            //harvester.Orders.SetBehaviorDirectlyInSim(EntityBehaviorType.Attacker_Full);
            CityShipsPerUnitBaseInfo newData = newEntity.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");
            newData.DefenseMode = tData.DefenseMode;
            newData.HomeDefense = tData.HomeDefense;
            newData.locationToBuildOn = tData.locationToBuildOn;
            newData.MetalStored = tData.MetalStored;
            newData.PlanetDefenseWantsToHelp = tData.PlanetDefenseWantsToHelp;
            newData.planetToBuildOn = tData.planetToBuildOn;
            newData.Prosperity = tData.Prosperity;
            //not for these though
            //newData.ShipsInside = tData.ShipsInside;
            //newData.ShipsInside_ForUI = tData.ShipsInside_ForUI;
            newData.Source = tData.Source;
            newData.target = tData.target;
            newData.toBuildDefenseOrEcon = tData.toBuildDefenseOrEcon;
            newEntity.ShouldNotBeConsideredAsThreatToHumanTeam = true;
        }
    }
}
