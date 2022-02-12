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
        }
    }
}
