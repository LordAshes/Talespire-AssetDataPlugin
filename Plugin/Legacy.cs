using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LordAshes
{
    public partial class AssetDataPlugin : BaseUnityPlugin
    {
        public static class Legacy
        {
            public static string GetCreatureName(CreatureBoardAsset asset)
            {
                return GetCreatureName(asset.Creature.Name);
            }

            public static string GetCreatureName(Creature creature)
            {
                return GetCreatureName(creature.Name);
            }

            public static string GetCreatureName(string name)
            {
                if (name.Contains("<"))
                {
                    name = name.Substring(0, name.IndexOf("<"));
                }
                return name;
            }

            public static string GetStatBlock(CreatureBoardAsset asset)
            {
                return GetStatBlock(asset.Creature.Name);
            }

            public static string GetStatBlock(Creature creature)
            {
                return GetStatBlock(creature.Name);
            }

            public static string GetStatBlock(string block)
            {
                if (block.Contains(">"))
                {
                    return block.Substring(block.IndexOf(">") + 1);
                }
                else
                {
                    return "";
                }
            }

            public class LegacyChange
            {
                public string cid { get; set; }
                public string action { get; set; }
                public string key { get; set; }
                public string previous { get; set; }
                public string value { get; set; }
            }

            public static MethodInfo setInfo = null;
            public static MethodInfo clearInfo = null;

            public static void SubscribeToLegacyMessages()
            {
                if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Checking For Stat Messaging Legacy Support"); }
                Type statMessaging = Type.GetType("LordAshes.StatMessaging, StatMessaging");
                if (statMessaging != null)
                {
                    try
                    {
                        setInfo = statMessaging.GetMethod("SetInfo");
                        clearInfo = statMessaging.GetMethod("ClearInfo");
                        MethodInfo method = statMessaging.GetMethod("ReflectionSubscription");
                        object[] methodParams = new object[] { typeof(AssetDataPlugin.Legacy).AssemblyQualifiedName, "LegacyCallback" };
                        method.Invoke(null, methodParams);
                        if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Legacy Support Active"); }
                    }
                    catch (Exception x)
                    {
                        Debug.Log("Asset Data Plugin: Unable To Subscribe To Stat Messaging Legacy Support");
                        Debug.LogException(x);
                    }
                }
                else
                {
                    if (Internal.diagnostics >= DiagnosticSelection.low) {  Debug.Log("Asset Data Plugin: Stat Messaging Not Available. Disabling Legacy Support"); }
                }
            }

            public static void LegacyCallback(string json)
            {
                if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Legacy Support Received: " + json); }
                List<Legacy.LegacyChange> changes = JsonConvert.DeserializeObject<List<Legacy.LegacyChange>>(json);
                foreach (Legacy.LegacyChange change in changes)
                {
                    AssetDataPlugin.ChangeAction action = ChangeAction.invalid;
                    switch (change.action)
                    {
                        case "modified": action = ChangeAction.modify; break;
                        case "added": action = ChangeAction.add; break;
                        case "removed": action = ChangeAction.remove; break;
                    }
                    Internal.ProcessRemoteChange("/" + AssetDataPlugin.Guid + " " + change.cid+ "|" + change.key + "|" + action + "|" + change.previous + "|" + change.value, LocalPlayer.Id.ToString(), Talespire.SourceRole.anonymous);
                }
            }
        }
    }
}
