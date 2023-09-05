using Arcen.AIW2.Core;
using Arcen.Universal;
using System;
using System.Text;
using UnityEngine;

namespace Arcen.AIW2.External
{
    public class CityShipsPerUnitBaseInfo : ExternalSquadBaseInfo
    {
        //Serialized
        public int MetalStored;
        public int Prosperity;
        public int toBuildDefenseOrEcon;
        public readonly Dictionary<GameEntityTypeData, int> ShipsInside = Dictionary<GameEntityTypeData, int>.Create_WillNeverBeGCed(60, "CityShipsPerDefenseBaseInfo-ShipsInside");

        public bool DefenseMode; //true if defense ship, otherwise false
        public LazyLoadSquadWrapper HomeDefense;
        

        //non serialized
        public string ShipsInside_ForUI; //a string representation of the ShipsInside that's UI safe
        public Planet PlanetDefenseWantsToHelp;
        public int target; //an index to refer to a list
        public CityShipsPerUnitBaseInfo()
        {
            Cleanup();
        }
        public Planet planetToBuildOn;
        public ArcenPoint locationToBuildOn;

        public override void CopyTo(ExternalSquadBaseInfo CopyTarget)
        {
            if (CopyTarget == null)
                return;
            CityShipsPerUnitBaseInfo target = CopyTarget as CityShipsPerUnitBaseInfo;
            if (target == null)
            {
                ArcenDebugging.ArcenDebugLogSingleLine("Could not copy from " + this.GetType() + " to " + CopyTarget.GetType() + " because of type mismatch.", Verbosity.ShowAsError);
                return;
            }
            target.ShipsInside_ForUI = this.ShipsInside_ForUI;
            target.PlanetDefenseWantsToHelp = this.PlanetDefenseWantsToHelp;
            target.target = this.target; //lol funi
        }

        public void CopyFrom(TemplarPerUnitBaseInfo otherData)
        {
            this.DefenseMode = otherData.DefenseMode;
        }

        public override void DoAfterSingleOtherShipMergedIntoOurStack(ExternalSquadBaseInfo OtherShipBeingDiscarded)
        {
           //do jackshit
        }

        public override void DoAfterStackSplitAndCopy(int OriginalStackCount, int MyPersonalNewStackCount)
        {
            if (OriginalStackCount <= 0)
                return;

            //this method is called on both halves of the stack that are split,
            //and you can see the amount that was in the total stack originally, and the portion that is in this half of the new split stack

            //let each half only have part of the metal. We can use floats because this is only happening on the host anyway.
            float percentageMetalLeft = (float)MyPersonalNewStackCount / (float)OriginalStackCount;
            this.MetalStored = Mathf.RoundToInt(this.MetalStored * percentageMetalLeft);

            foreach (KeyValuePair<GameEntityTypeData, int> kv in this.ShipsInside)
            {
                int newShipsInside = Mathf.RoundToInt(kv.Value * percentageMetalLeft);
                if (newShipsInside < 1)
                    newShipsInside = 1;
                this.ShipsInside[kv.Key] = newShipsInside;
            }
        }

        public override void SerializeTo(SerMetaData MetaData, ArcenSerializationBuffer Buffer, SerializationCommandType SerializationCmdType)
        {
            if (!SerializationCmdType.GetIsNetworkType())
            {
                Buffer.AddBool(MetaData, this.DefenseMode);
            }
            Buffer.AddInt32(MetaData, ReadStyle.PosExceptNeg1, this.HomeDefense.GetPrimaryKeyID(), "HomeCastle.PrimaryKeyID");
            if (!SerializationCmdType.GetIsNetworkType())
            {
                Buffer.AddInt32(MetaData, ReadStyle.Signed, this.MetalStored);
                Buffer.AddInt32(MetaData, ReadStyle.Signed, this.Prosperity);
                Buffer.AddInt32(MetaData, ReadStyle.Signed, this.toBuildDefenseOrEcon);
            }
            Buffer.AddInt16(MetaData, ReadStyle.NonNeg, (Int16)ShipsInside.Count, "ShipsInside.Count");
            ShipsInside.DoFor(delegate (KeyValuePair<GameEntityTypeData, int> pair)
            {
                GameEntityTypeDataTable.Instance.SerializeByIndex(MetaData, pair.Key, Buffer, "ShipInside.Type");
                Buffer.AddInt32(MetaData, ReadStyle.NonNeg, ShipsInside[pair.Key], "ShipInside.Count");
                return DelReturn.Continue;
            });
        }

        public override void DeserializeIntoSelf(SerMetaData MetaData, ArcenDeserializationBuffer Buffer, SerializationCommandType SerializationCmdType)
        {
            if (!SerializationCmdType.GetIsNetworkType())
                this.DefenseMode = Buffer.ReadBool(MetaData);
            this.HomeDefense = LazyLoadSquadWrapper.Create(Buffer.ReadInt32(MetaData, ReadStyle.PosExceptNeg1, "HomeCastle.PrimaryKeyID"), true, "TemplarCastDeser");
            if (!SerializationCmdType.GetIsNetworkType())
            {
                this.MetalStored = Buffer.ReadInt32(MetaData, ReadStyle.Signed);
                this.Prosperity = Buffer.ReadInt32(MetaData, ReadStyle.Signed);
                this.toBuildDefenseOrEcon = Buffer.ReadInt32(MetaData, ReadStyle.Signed);
            }
            Int16 count = Buffer.ReadInt16(MetaData, ReadStyle.NonNeg, "ShipsInside.Count");
            for (int i = 0; i < count; i++)
            {
                GameEntityTypeData data = GameEntityTypeDataTable.Instance.DeserializeByIndex(MetaData, Buffer, "ShipInside.Type");
                ShipsInside[data] = Buffer.ReadInt32(MetaData, ReadStyle.NonNeg, "ShipInside.Count");
            }
        }

        protected override void Cleanup()
        {
            this.MetalStored = 0;
            this.Prosperity = 0;
            this.toBuildDefenseOrEcon = 0;
            this.DefenseMode = false;
            this.HomeDefense.Clear();
            this.ShipsInside_ForUI = string.Empty;
            this.PlanetDefenseWantsToHelp = null;
            this.target = -1;
            this.locationToBuildOn = ArcenPoint.ZeroZeroPoint;
            this.planetToBuildOn = null;
        }

        public int GetTotalStrengthInside(GameEntity_Squad entity)
        {
            //calculate the strength of ships in here
            int strength = 0;
            try
            {
                ShipsInside.DoFor(delegate (KeyValuePair<GameEntityTypeData, int> pair)
                {
                    byte markLevel = entity.CurrentMarkLevel;

                    GameEntityTypeData.MarkLevelStats markLevelStats = pair.Key.MarkStatsFor(markLevel);
                    strength += (markLevelStats.StrengthPerSquad_CalculatedWithNullFleetMembership * ShipsInside[pair.Key]);
                    return DelReturn.Continue;
                });
            }
            catch { }
            return strength;
        }

        public static ArcenDoubleCharacterBuffer Buffer_ForShipsInside = new ArcenDoubleCharacterBuffer("CityShipPerDefenseBaseInfo-Buffer_ForShipsInside");

        public void UpdateShipsInside_ForUI(GameEntity_Squad entity)
        {
            Balance_MarkLevel markByOrdinal = Balance_MarkLevelTable.Instance.RowsByOrdinal[entity.CurrentMarkLevel];
            int shipsFound = 0;
            ShipsInside.DoFor(delegate (KeyValuePair<GameEntityTypeData, int> pair)
            {
                shipsFound++;
                byte markLevel = entity.CurrentMarkLevel;
                GameEntityTypeData.MarkLevelStats markLevelStats = pair.Key.MarkStatsFor(markLevel);
                int strengthForUnit = (markLevelStats.StrengthPerSquad_CalculatedWithNullFleetMembership * this.ShipsInside[pair.Key]);
                if (shipsFound > 1)
                    Buffer_ForShipsInside.Add(", ");

                if (this.ShipsInside[pair.Key] > 0)
                    Buffer_ForShipsInside.Add(pair.Key.GetDisplayName(), "909090").Add(" x" + this.ShipsInside[pair.Key].ToString(), markByOrdinal.ColorHex);
                return DelReturn.Continue;
            });
            if (shipsFound == 0)
                Buffer_ForShipsInside.Add("no ships.\n");
            else
            {
                int strengthInside = GetTotalStrengthInside(entity) / 1000;
                if (strengthInside == 0)
                    strengthInside = 1;
                Buffer_ForShipsInside.Add(". Approx ").Add(ArcenExternalUIUtilities.GUI_StrengthTextColorAndIcon).Add(strengthInside.ToString(), "ffa1a1").Add(".\n");
            }
            this.ShipsInside_ForUI = Buffer_ForShipsInside.GetStringAndResetForNextUpdate();
        }
    }
}
