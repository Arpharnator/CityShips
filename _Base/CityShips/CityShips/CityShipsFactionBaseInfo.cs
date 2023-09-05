using System;
using Arcen.AIW2.Core;
using Arcen.Universal;

namespace Arcen.AIW2.External
{
    public class CityShipsFactionBaseInfo : ExternalFactionBaseInfoRoot
    {
        public static readonly bool debug = false;
        //Important Value
        public int Intensity { get; private set; }

        #region Serialized

        #endregion

        public readonly DoubleBufferedList<SafeSquadWrapper> Defenses = DoubleBufferedList<SafeSquadWrapper>.Create_WillNeverBeGCed(300, "CityShips-Defenses");
        public readonly DoubleBufferedList<SafeSquadWrapper> DefensesDefenders = DoubleBufferedList<SafeSquadWrapper>.Create_WillNeverBeGCed(300, "CityShips-DefensesDefenders");
        public readonly DoubleBufferedList<SafeSquadWrapper> CityShips = DoubleBufferedList<SafeSquadWrapper>.Create_WillNeverBeGCed(300, "CityShips-CityShips");
        public readonly DoubleBufferedList<SafeSquadWrapper> EconomicUnits = DoubleBufferedList<SafeSquadWrapper>.Create_WillNeverBeGCed(300, "CityShips-EconomicUnits");

        public bool AnyDefensesActive;
        public bool humanAllied = false;
        public bool aiAllied = false;
        public bool SeedNearPlayer = false;

        #region Cleanup
        public CityShipsFactionBaseInfo() => Cleanup();

        protected override void Cleanup()
        {
            Intensity = 0;
            humanAllied = false;
            aiAllied = false;
            Defenses.Clear();
            DefensesDefenders.Clear();
            CityShips.Clear();
            EconomicUnits.Clear();
            AnyDefensesActive = false;
            SeedNearPlayer = false;
        }
        #endregion

        #region Ser / Deser
        public override void SerializeFactionTo(SerMetaData MetaData, ArcenSerializationBuffer Buffer, SerializationCommandType SerializationCmdType)
        {
        }

        public override void DeserializeFactionIntoSelf(SerMetaData MetaData, ArcenDeserializationBuffer Buffer, SerializationCommandType SerializationCmdType)
        {
        }
        #endregion

        #region Faction Settings
        public override int GetDifficultyOrdinal_OrNegativeOneIfNotRelevant() => Intensity;

        protected override void DoFactionGeneralAggregationsPausedOrUnpaused() { }


        protected override void DoRefreshFromFactionSettings()
        {
            // If we're a subfaction added by Splintering Spire, inherit their Intensity.
            Intensity = AttachedFaction.Config.GetIntValueForCustomFieldOrDefaultValue("Intensity", true);
            this.SeedNearPlayer = AttachedFaction.GetBoolValueForCustomFieldOrDefaultValue("SpawnNearPlayer", true);
            string invasionTime = AttachedFaction.Config.GetStringValueForCustomFieldOrDefaultValue("InvasionTime", true);
            Faction faction = this.AttachedFaction;
            if (faction.InvasionTime == -1)
            {
                //initialize the invasion time
                if (invasionTime == "Immediate")
                    faction.InvasionTime = 1;
                else if (invasionTime == "Early Game")
                    faction.InvasionTime = (60 * 60); //1 hours in
                else if (invasionTime == "Mid Game")
                    faction.InvasionTime = (2 * (60 * 60)); //1.5 hours in
                else if (invasionTime == "Late Game")
                    faction.InvasionTime = 3 * (60 * 60); //3 hours in
                if (faction.InvasionTime > 1)
                {
                    //this will be a desync on the client and host, but the host will correct the client in under 5 seconds.
                    if (Engine_Universal.PermanentQualityRandom.Next(0, 100) < 50)
                        faction.InvasionTime += Engine_Universal.PermanentQualityRandom.Next(0, faction.InvasionTime / 10);
                    else
                        faction.InvasionTime -= Engine_Universal.PermanentQualityRandom.Next(0, faction.InvasionTime / 10);
                }
            }
        }
        #endregion

        public override void SetStartingFactionRelationships() => AllegianceHelper.EnemyThisFactionToAll(AttachedFaction);

        #region DoPerSecondLogic_Stage2Aggregating_OnMainThreadAndPartOfSim_ClientAndHost
        public override void DoPerSecondLogic_Stage2Aggregating_OnMainThreadAndPartOfSim_ClientAndHost(ArcenClientOrHostSimContextCore Context)
        {
            try
            {
                int totalUnits = 0;
                int totalStrength = 0;
                Defenses.ClearConstructionListForStartingConstruction();
                DefensesDefenders.ClearConstructionListForStartingConstruction();
                CityShips.ClearConstructionListForStartingConstruction();
                EconomicUnits.ClearConstructionListForStartingConstruction();

                AttachedFaction.DoForEntities(delegate (GameEntity_Squad entity)
                {
                    if (entity.TypeData.GetHasTag("CityShipsDefense"))
                    {
                        Defenses.AddToConstructionList(entity);
                    }
                    if (entity.TypeData.GetHasTag("CityShipsCityShip"))
                        CityShips.AddToConstructionList(entity);

                    if (entity.TypeData.GetHasTag("CityShipsEconomicUnit"))
                    {
                        EconomicUnits.AddToConstructionList(entity);
                    }
                    if (entity.TypeData.IsMobileCombatant)
                    {
                        //only creates it if it couldn't find it.
                        CityShipsPerUnitBaseInfo data = entity.CreateExternalBaseInfo<CityShipsPerUnitBaseInfo>("CityShipsPerUnitBaseInfo");
                        if (data.DefenseMode)
                        {
                            GameEntity_Squad HomeDefense = data.HomeDefense.GetSquad();
                            if (entity.PlanetFaction.Faction != HomeDefense?.PlanetFaction.Faction)
                            {
                                HomeDefense = null;
                                data.HomeDefense.Clear();
                            }
                            if (HomeDefense == null)
                            {
                                entity.Despawn(Context, true, InstancedRendererDeactivationReason.AFactionJustWarpedMeOut); //if we've lost our defense, we die
                            }
                            DefensesDefenders.AddToConstructionList(entity);
                        }
                    }
                    totalUnits += 1 + entity.ExtraStackedSquadsInThis;
                    totalStrength += entity.GetStrengthOfSelfAndContents();
                    return DelReturn.Continue;
                });
                Defenses.SwitchConstructionToDisplay();
                DefensesDefenders.SwitchConstructionToDisplay();
                CityShips.SwitchConstructionToDisplay();
                EconomicUnits.SwitchConstructionToDisplay();
            }
            catch
            {
                //ArcenDebugging.ArcenDebugLogSingleLine("Hit exception in Templar DoPerSecondLogic_Stage2Aggregating_OnMainThreadAndPartOfSim debugCode " + debugCode + " " + e.ToString(), Verbosity.ShowAsError);
            }
            
        }
            
        #endregion

        public override void DoPerSecondLogic_Stage3Main_OnMainThreadAndPartOfSim_ClientAndHost(ArcenClientOrHostSimContextCore Context)
        {
             UpdateAllegiance();
        }

        private void UpdateAllegiance()
        {
            {
                if (ArcenStrings.Equals(this.Allegiance, "Hostile To All") ||
                   ArcenStrings.Equals(this.Allegiance, "HostileToAll") ||
                   string.IsNullOrEmpty(this.Allegiance))
                {
                    this.humanAllied = false;
                    this.aiAllied = false;
                    if (string.IsNullOrEmpty(this.Allegiance))
                        throw new Exception("empty CityShips allegiance '" + this.Allegiance + "'");
                    if (debug)
                        ArcenDebugging.ArcenDebugLogSingleLine("This CityShips faction should be hostile to all (default)", Verbosity.DoNotShow);
                    //make sure this isn't set wrong somehow
                    AllegianceHelper.EnemyThisFactionToAll(AttachedFaction);
                }
                else if (ArcenStrings.Equals(this.Allegiance, "Hostile To Players Only") ||
                        ArcenStrings.Equals(this.Allegiance, "HostileToPlayers"))
                {
                    this.aiAllied = true;
                    AllegianceHelper.AllyThisFactionToAI(AttachedFaction);
                    if (debug)
                        ArcenDebugging.ArcenDebugLogSingleLine("This CityShips faction should be friendly to the AI and hostile to players", Verbosity.DoNotShow);

                }
                else if (ArcenStrings.Equals(this.Allegiance, "Minor Faction Team Red"))
                {
                    if (debug)
                        ArcenDebugging.ArcenDebugLogSingleLine("This CityShips faction is on team red", Verbosity.DoNotShow);
                    AllegianceHelper.AllyThisFactionToMinorFactionTeam(AttachedFaction, "Minor Faction Team Red");
                }
                else if (ArcenStrings.Equals(this.Allegiance, "Minor Faction Team Blue"))
                {
                    if (debug)
                        ArcenDebugging.ArcenDebugLogSingleLine("This CityShips faction is on team blue", Verbosity.DoNotShow);

                    AllegianceHelper.AllyThisFactionToMinorFactionTeam(AttachedFaction, "Minor Faction Team Blue");
                }
                else if (ArcenStrings.Equals(this.Allegiance, "Minor Faction Team Green"))
                {
                    if (debug)
                        ArcenDebugging.ArcenDebugLogSingleLine("This CityShips faction is on team green", Verbosity.DoNotShow);

                    AllegianceHelper.AllyThisFactionToMinorFactionTeam(AttachedFaction, "Minor Faction Team Green");
                }

                else if (ArcenStrings.Equals(this.Allegiance, "HostileToAI") ||
                        ArcenStrings.Equals(this.Allegiance, "Friendly To Players"))
                {
                    this.humanAllied = true;
                    AllegianceHelper.AllyThisFactionToHumans(AttachedFaction);
                    if (debug)
                        ArcenDebugging.ArcenDebugLogSingleLine("This CityShips faction should be hostile to the AI and friendly to players", Verbosity.DoNotShow);
                }
                else
                {
                    throw new Exception("unknown CityShips allegiance '" + this.Allegiance + "'");
                }
            }
        }

        public override float CalculateYourPortionOfPredictedGameLoad_Where100IsANormalAI(ArcenCharacterBufferBase OptionalExplainCalculation)
        {
            DoRefreshFromFactionSettings();

            int load = 10 + (Intensity * 6);

            if (OptionalExplainCalculation != null)
                OptionalExplainCalculation.Add(load).Add($" Load From CityShips");
            return load;
        }

        #region UpdatePowerLevel
        public override void UpdatePowerLevel()
        {
            FInt result = FInt.Zero;
            
            this.AttachedFaction.OverallPowerLevel = result;
            //if ( World_AIW2.Instance.GameSecond % 60 == 0 )
            //    ArcenDebugging.ArcenDebugLogSingleLine("resulting power level: " + faction.OverallPowerLevel, Verbosity.DoNotShow );
        }
        #endregion
    }
}
