using Arcen.AIW2.Core;
using Arcen.Universal;
using System;

using System.Text;

namespace Arcen.AIW2.External
{
    public class CityShipsPerUnitDescriptionAppender : GameEntityDescriptionAppenderBase
    {
        public override void AddToDescriptionBuffer(GameEntity_Squad RelatedEntityOrNull, GameEntityTypeData RelatedEntityTypeData, ArcenCharacterBufferBase Buffer)
        {
            if (RelatedEntityOrNull == null)
                return;
            CityShipsFactionBaseInfo baseInfo = null;
            Faction facOrNull = RelatedEntityOrNull.GetFactionOrNull_Safe();
            if (facOrNull != null)
                baseInfo = facOrNull.TryGetExternalBaseInfoAs<CityShipsFactionBaseInfo>();
            if (baseInfo == null)
            {
                Buffer.Add("CityShipsFactionBaseInfo could not be found here. This is a BUG");
                return;
            }
            CityShipsPerUnitBaseInfo tData = RelatedEntityOrNull.TryGetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
            if (tData == null)
            {
                Buffer.Add("tData for this CityShip unit is null. This is a bug.");
                return;
            }
            int mult = 1;
            if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsTown") || RelatedEntityOrNull.TypeData.GetHasTag("CityShipsStarhold") || RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCityShipTier2"))
                mult = 3;
            if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCity") || RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCitadel") || RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCityShipTier3"))
                mult = 8;
            if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsMetropolis") || RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCityShipTier4"))
                mult = 24;
            int income = tData.Prosperity / (1000 / baseInfo.Intensity) * mult;
            int metalCap = (1 * tData.Prosperity * baseInfo.Intensity * mult);
            Buffer.Add("\nThis Unit has " + tData.Prosperity + " Prosperity. \n");
            if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsEconomicUnit"))
            {
                Buffer.Add("This Economic Unit makes " + income + " Metal every second, and ");
                Buffer.Add("has " + tData.MetalStored + " / " + metalCap + " Metal. \n");
            }
            if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsDefense"))
            {
                int baseStrength = 2000;
                if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsStarhold"))
                {
                    baseStrength = 6000;
                }
                if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCitadel"))
                {
                    baseStrength = 12000;
                }
                FInt aip = FactionUtilityMethods.Instance.GetCurrentAIP();
                CityShipsPerUnitBaseInfo data = RelatedEntityOrNull.GetExternalBaseInfoAs<CityShipsPerUnitBaseInfo>();
                int output = (baseStrength + (baseStrength * data.Prosperity / 1000));
                output /= 1000;
                Buffer.Add("This Defense Unit makes " + income + " Metal every second, and ");
                Buffer.Add("has " + tData.MetalStored + " Metal.\n");
                Buffer.Add("Contains : " + tData.ShipsInside_ForUI);
            }
            if (RelatedEntityOrNull.TypeData.GetHasTag("CityShipsCityShip"))
            {
                Buffer.Add("This City Ship makes " + income + " Metal every second, and ");
                Buffer.Add("has " + tData.MetalStored + " Metal. It needs at least " + metalCap + " stored to be able to build ships.\n");
                Buffer.Add("Contains : " + tData.ShipsInside_ForUI);
            }
        }
    }
}
