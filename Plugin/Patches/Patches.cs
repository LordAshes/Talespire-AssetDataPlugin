using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LordAshes
{
    public partial class AssetDataPlugin : BaseUnityPlugin
    {
        public static class Patches
        {
            /*
             * This patch causes HF asset to not load. It has been replaced by
             * a check in the Update() cycle to determine when boards are loaded
             * and unload.
             * 
            [HarmonyPatch(typeof(CampaignSessionManager), "FetchCampaignSettings")]
            public static class Patche01
            {
                public static bool Prefix()
                {
                    return true;
                }

                public static void Postfix(Action ___OnCampaignChanged)
                {
                    Action rasieEvent = ___OnCampaignChanged;
                    rasieEvent?.Invoke();
                }
            }
            */

            [HarmonyPatch(typeof(CreatureBoardAsset), "RequestDelete")]
            public static class Patche02
            {
                public static bool Prefix()
                {
                    return true;
                }

                public static void Postfix(CreatureBoardAsset __instance)
                {
                    if (Internal.data.ContainsKey(__instance.CreatureId.ToString()))
                    {
                        if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Clearing Asset Data For "+Legacy.GetCreatureName(__instance.Name)+" ("+ __instance.CreatureId+")"); }
                        Internal.data[__instance.CreatureId.ToString()].Clear();
                        Internal.data.Remove(__instance.CreatureId.ToString());
                        System.IO.File.WriteAllText(Internal.pluginPath + "AssetData/AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json", JsonConvert.SerializeObject(Internal.data, Formatting.Indented));
                    }
                }
            }
        }
    }
}
