﻿using BepInEx;
using BepInEx.Configuration;
using Bounce.Unmanaged;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    public partial class AssetDataPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Asset Data Plug-In";
        public const string Guid = "org.lordashes.plugins.assetdata";
        public const string Version = "2.1.3.0";

        public bool boardLoaded = false;

        void Awake()
        {
            Internal.diagnostics = Config.Bind("Settings", "Diagnostics", DiagnosticSelection.low).Value;

            Debug.Log("Asset Data Plugin: "+this.GetType().AssemblyQualifiedName+" is actve. (Diagnostics = "+ Internal.diagnostics+")");

            Internal.cutoff = Config.Bind("Settings", "Number of days to data for unreferenced asset", 30).Value;

            Internal.triggerDiagnosticToggle = Config.Bind("Settings", "Toggle Screen Diagnostics", new KeyboardShortcut(KeyCode.D, KeyCode.RightControl)).Value;
            Internal.triggerSpecificDiagnostic = Config.Bind("Settings", "Show Diagnostics For Asset By Name", new KeyboardShortcut(KeyCode.F, KeyCode.RightControl)).Value;
            Internal.triggerDiagnosticSpecificDump = Config.Bind("Settings", "Log Selected Asset Data", new KeyboardShortcut(KeyCode.G, KeyCode.RightControl)).Value;
            Internal.triggerDiagnosticDump = Config.Bind("Settings", "Log Complete Asset Data", new KeyboardShortcut(KeyCode.G, KeyCode.RightAlt)).Value;
            Internal.triggerSimData = Config.Bind("Settings", "Simulate Data", new KeyboardShortcut(KeyCode.G, KeyCode.RightShift)).Value;

            Internal.maxRequestAttempts = Config.Bind("Settings", "Maximum Request Attempts", 100).Value;

            StartCoroutine("GetDistributor", new object[] { Config.Bind("Settings", "Plugins load time", 3f).Value });

            if(!System.IO.Directory.Exists(Internal.pluginPath + "AssetData")) { System.IO.Directory.CreateDirectory(Internal.pluginPath + "AssetData/"); }

            if (!Internal.data.ContainsKey(AssetDataPlugin.Guid)) { Internal.data.Add(AssetDataPlugin.Guid, new Dictionary<string, Datum>()); }
            if (!Internal.data[AssetDataPlugin.Guid].ContainsKey("ScreenDiagnostics"))
            {
                Internal.data[AssetDataPlugin.Guid].Add("ScreenDiagnostics", new Datum() { previous = false.ToString(), value = false.ToString() });
            }
            else
            {
                Internal.data[AssetDataPlugin.Guid]["ScreenDiagnostics"] = new Datum() { previous = Internal.data[AssetDataPlugin.Guid]["ScreenDiagnostics"].value, value = false.ToString() };
            }
            if (!Internal.data[AssetDataPlugin.Guid].ContainsKey("DiagnosticsOverrideAssetName"))
            {
                Internal.data[AssetDataPlugin.Guid].Add("DiagnosticsOverrideAssetName", new Datum() { previous = "", value = "" });
            }
            else
            {
                Internal.data[AssetDataPlugin.Guid]["DiagnosticsOverrideAssetName"] = new Datum() { previous = Internal.data[AssetDataPlugin.Guid]["DiagnosticsOverrideAssetName"].value, value = "" };
            }

            var harmony = new Harmony(Guid);
            harmony.PatchAll();

            StartCoroutine(CheckForLegacySupport());

            StartCoroutine(BacklogLoop());

            Utility.PostOnMainPage(this.GetType());
        }

        void Update()
        {
            if(!boardLoaded && Utility.isBoardLoaded()) { boardLoaded = true; OnCampaignChange(); }
            else if (boardLoaded && !Utility.isBoardLoaded()) { boardLoaded = false; }

            if (Utility.StrictKeyCheck(Internal.triggerDiagnosticToggle))
            {
                SetInfo(AssetDataPlugin.Guid, "ScreenDiagnostics", (!bool.Parse(Internal.data[AssetDataPlugin.Guid]["ScreenDiagnostics"].value)).ToString());
            }
            else if (Utility.StrictKeyCheck(Internal.triggerSpecificDiagnostic))
            {
                SystemMessage.AskForTextInput("Diagnostics For Asset...", "Enter Asset Identification:",
                                              "Apply", (name) => { SetInfo(AssetDataPlugin.Guid, "DiagnosticsOverrideAssetName", name); SetInfo(AssetDataPlugin.Guid, "ScreenDiagnostics", true.ToString()); }, null,
                                              "Clear", () => { SetInfo(AssetDataPlugin.Guid, "DiagnosticsOverrideAssetName", ""); }, "");
            }
            else if (Utility.StrictKeyCheck(Internal.triggerDiagnosticSpecificDump))
            {
                Debug.Log("Asset Data Plugin:\r\n" + JsonConvert.SerializeObject(Internal.data[LocalClient.SelectedCreatureId.ToString()]));
            }
            else if (Utility.StrictKeyCheck(Internal.triggerDiagnosticDump))
            {
                Debug.Log("Asset Data Plugin:\r\n" + JsonConvert.SerializeObject(Internal.data));
            }
            else if (Utility.StrictKeyCheck(Internal.triggerSimData))
            {
                SystemMessage.AskForTextInput("Data Simulation...", "Enter Source:",
                                              "Apply", (source) => 
                {
                    SystemMessage.AskForTextInput("Diagnostics For Asset...", "Enter Key:",
                                                  "Apply", (key) =>
                    {
                        SystemMessage.AskForTextInput("Diagnostics For Asset...", "Enter Value:",
                                                                                  "Apply", (value) =>
                        {
                            AssetDataPlugin.SetInfo(source, key, value);
                        }, null,
                        "Clear", null, "");
                    }, null,
                    "Clear", null, "");
                }, null,
                "Clear", null, "");
            }
        }

        void OnGUI()
        {
            try
            {
                if (bool.Parse(Internal.data[AssetDataPlugin.Guid]["ScreenDiagnostics"].value))
                {
                    string id = Internal.data[AssetDataPlugin.Guid]["DiagnosticsOverrideAssetName"].value;
                    if (id == "")
                    {
                        CreatureBoardAsset asset = null;
                        CreaturePresenter.TryGetAsset(LocalClient.SelectedCreatureId, out asset);
                        if (asset != null)
                        {
                            id = asset.CreatureId.ToString();
                        }
                    }
                    if (id != "")
                    {
                        string content = "{}";
                        if (Internal.data.ContainsKey(id)) { content = JsonConvert.SerializeObject(Internal.data[id]); }
                        GUI.Label(new Rect(10, 40, Screen.width - 10, Screen.height - 40), "[" + id + "]: " + content);
                    }
                    else
                    {
                        GUI.Label(new Rect(10, 40, Screen.width - 10, Screen.height - 40), "[No Asset Selected]");
                    }
                }
            }
            catch(Exception x)
            {
                Debug.Log("Asset Data Plugin: GUI Exception: " + x);
            }
        }

        /// <summary>
        /// Subscription for notification when the given key (or key pattern) changes for any asset
        /// </summary>
        /// <param name="pattern">The key or a wild card key for which notificatons are desired</param>
        /// <param name="callback">The callback method that is triggered when a notification occurs</param>
        /// <returns>Subscription id which can be used in other methods like unsubscribe</returns>
        public static System.Guid Subscribe(string pattern, Action<DatumChange> callback)
        {
            if (Internal.diagnostics>=DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Subscribed To " + pattern); }
            System.Guid identity = System.Guid.NewGuid();
            lock (Internal.padlockSubscriptions)
            {
                Internal.subscriptions.Add(new Subscription() { subscription = identity, pattern = pattern, callback = callback, callbackType = null, callbackMethod = null, checker = null });
                Reset(identity);
            }
            return identity;
        }

        public static System.Guid Subscribe(string pattern, Action<DatumChange> callback, Func<DatumChange,bool> checker)
        {
            if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Subscribed To " + pattern); }
            System.Guid identity = System.Guid.NewGuid();
            lock (Internal.padlockSubscriptions)
            {
                Internal.subscriptions.Add(new Subscription() { subscription = identity, pattern = pattern, callback = callback, callbackType = null, callbackMethod = null, checker = checker });
                Reset(identity);
            }
            return identity;
        }

        /// <summary>
        /// Subscription for notification when the given key (or key pattern) changes for any asset
        /// </summary>
        /// <param name="pattern">The key or a wild card key for which notificatons are desired</param>
        /// <param name="callback">The callback method that is triggered when a notification occurs</param>
        /// <returns>Subscription id which can be used in other methods like unsubscribe</returns>
        public static System.Guid SubscribeViaReflection(string pattern, string callbackType, string callbackMethod)
        {
            if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Subscribed To " + pattern); }
            System.Guid identity = System.Guid.NewGuid();
            lock (Internal.padlockSubscriptions)
            {
                Internal.subscriptions.Add(new Subscription() { subscription = identity, pattern = pattern, callback = null, callbackType = callbackType, callbackMethod = callbackMethod });
                Reset(identity);
            }
            return identity;
        }

        /// <summary>
        /// Unsubscribes the subscription associated with the given subscription id
        /// </summary>
        /// <param name="subscriptionId">Subscription id returned by the correspondiong subscribe method</param>
        public static void Unsubscribe(System.Guid subscriptionId)
        {
            if (Internal.diagnostics>=DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Unsubscribed Subscription " + subscriptionId); }
            lock (Internal.padlockSubscriptions)
            {
                for (int s = 0; s < Internal.subscriptions.Count; s++)
                {
                    if (Internal.subscriptions[s].subscription == subscriptionId)
                    {
                        Internal.subscriptions.RemoveAt(s);
                        s--;
                    }
                }
            }
        }

        /// <summary>
        /// Resets all the current subscriptions which causes all notifications to be re-evaluated.
        /// This method is obsolte and one of the more specific Resets() should be used instead.
        /// </summary>
        [Obsolete]
        public static void Reset()
        {
            try
            {
                Internal.Reset();
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In Reset()");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Resets the subscription associated with the given subscription id which causes all notifications for that subscription to be re-evaluated.
        /// </summary>
        /// <param name="subscriptionId">Subscription id associated with the subscription to be reset</param>
        public static void Reset(System.Guid subscriptionId)
        {
            try
            {
                Internal.Reset(subscriptionId);
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In Reset(subscriptionId)");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Resets the subscription associated with the given key (or key pattern) which causes all notifications for that subscription to be re-evaluated.
        /// </summary>
        /// <param name="subscriptionId">Subscription id associated with the subscription to be reset</param>
        public static void Reset(string pattern)
        {
            try
            {
                Internal.Reset(pattern);
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In Reset(pattern)");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Method use to set the value of the given key for the given asset
        /// </summary>
        /// <param name="identity">Identification of the asset associated with the data</param>
        /// <param name="key">Identification of the key for which the value applies</param>
        /// <param name="value">The value of the given key for the given asset</param>
        public static void SetInfo(string identity, string key, string value, bool legacy=false)
        {
            try
            {
                if (Internal.diagnostics>=DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: SetInfo: Client Requested Set of " + key + " on " + identity + " to " + value); }
                lock (Internal.padlockData)
                {
                    if (!Internal.data.ContainsKey(identity) || !Internal.data[identity].ContainsKey(key))
                    {
                        Internal.SendPackets(identity, key, "add", value, legacy);
                    }
                    else
                    {
                        Internal.SendPackets(identity, key, "modify", value, legacy);
                    }
                    Internal.SetInfo(identity, key, value);
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In SetInfo(string)");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Method use to set the value of the given key for the given asset
        /// </summary>
        /// <param name="identity">Identification of the asset associated with the data</param>
        /// <param name="key">Identification of the key for which the value applies</param>
        /// <param name="value">The value of the given key for the given asset</param>
        public static void SetInfo(string identity, string key, object value, bool legacy = false)
        {
            try
            {
                SetInfo(identity, key, JsonConvert.SerializeObject(value), legacy);
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In SetInfo(object)");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Method use to send a real-time only (non-stored) message to all subscribed clients
        /// </summary>
        /// <param name="key">Identification of the key for which the value applies</param>
        /// <param name="value">The value of the given key</param>
        public static void SendInfo(string key, string value)
        {
            try
            {
                SetInfo("SYSTEM", key, value);
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In SendInfo");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Method use to send a real-time only (non-stored) message to all subscribed clients
        /// </summary>
        /// <param name="key">Identification of the key for which the value applies</param>
        /// <param name="value">The value of the given key</param>
        public static void SendInfo(string key, object value)
        {
            try
            { 
                SetInfo("SYSTEM", key, JsonConvert.SerializeObject(value));
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In SendInfo");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Method use to clear the value of the given key for the given asset
        /// </summary>
        /// <param name="identity">Identification of the asset associated with the data</param>
        /// <param name="key">Identification of the key to be cleared</param>
        public static void ClearInfo(string identity, string key, bool Legacy = false)
        {
            try
            {
                if (Internal.diagnostics>=DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: ClearInfo: Client Requested Clear of " + key + " on " + identity); }
                Internal.SendPackets(identity, key, "remove", "", Legacy);
                Internal.ClearInfo(identity, key);
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In ClearInfo");
                Debug.LogException(x);
            }
        }

        /// <summary>
        /// Method used to read the current value of a key for the given asset
        /// </summary>
        /// <param name="identity">Identification of the asset associated with the data</param>
        /// <param name="key">Identification of the key to be read</param>
        /// <returns>String representation of the value</returns>
        public static string ReadInfo(string identity, string key)
        {
            try
            {
                if (Internal.diagnostics>=DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: ReadInfo: Client Read " + key + " on " + identity); }
                string _identity = identity.ToString();
                lock (Internal.padlockData)
                {
                    if (!Internal.data.ContainsKey(_identity)) { return null; }
                    if (!Internal.data[_identity].ContainsKey(key)) { return null; }
                    return Internal.data[_identity][key].value;
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In ReadInfo");
                Debug.LogException(x);
                return null;
            }
        }

        /// <summary>
        /// Method used to read the current value of a key for the given asset as a given type
        /// </summary>
        /// <typeparam name="T">The type that the value will be interpreted as</typeparam>
        /// <param name="identity">Identification of the asset associated with the data</param>
        /// <param name="key">Identification of the key to be read</param>
        /// <returns>The value interpreted as the given type</returns>
        public static T ReadInfo<T>(string identity, string key)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(ReadInfo(identity, key));
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In ReadInfo<"+typeof(T).ToString()+">");
                Debug.LogException(x);
                return default(T);
            }
        }

        /// <summary>
        /// Method used to read a datum associated with the give key for the given asset
        /// </summary>
        /// <param name="identity">Identification of the asset from which the data will be read</param>
        /// <param name="key">Identification of the key which is to be read</param>
        /// <returns>Datum representing the current state of the key value</returns>
        public static Datum ReadDatum(string identity, string key)
        {
            try
            {
                if (Internal.diagnostics>=DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: ReadInfo: Client Read " + key + " on " + identity); }
                lock (Internal.padlockData)
                {
                    if (!Internal.data.ContainsKey(identity)) { return null; }
                    if (!Internal.data[identity].ContainsKey(key)) { return null; }
                    return Internal.data[identity][key];
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In ReadDatum");
                Debug.LogException(x);
                return null;
            }
        }

        /// <summary>
        /// Method of initiating legacy support if StatMessaging is available
        /// </summary>
        /// <returns></returns>
        public IEnumerator CheckForLegacySupport()
        {
            yield return new WaitForSeconds(3.0f);
            Debug.Log("Asset Data Plugin: Checking For Legacy Support");
            Legacy.SubscribeToLegacyMessages();
        }

        /// <summary>
        /// Backlog coroutine to releave stress from having backlog processed each Update cycle
        /// </summary>
        /// <returns></returns>
        public IEnumerator BacklogLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(0.25f);
                Backlog.Process();
            }
        }

        /// <summary>
        /// Campaign change method handler
        /// </summary>
        public void OnCampaignChange()
        {
            if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Campaign Changed To "+ CampaignSessionManager.Instance.CurrentState.Name); }

            if (CampaignSessionManager.Instance.CurrentState.Name != "InCampaignInBuildScene") { return; }

            if (System.IO.File.Exists(Internal.pluginPath + "AssetData/AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json"))
            {
                if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Previous Data Found. Loading AssetDataPlugin Data..."); }
                lock (Internal.padlockData)
                {
                    Internal.data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Datum>>>(System.IO.File.ReadAllText(Internal.pluginPath + "AssetData/AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json"));
                }
            }

            for (int asset = 0; asset < Internal.data.Keys.Count; asset++)
            {
                string key = Internal.data.Keys.ElementAt(asset);
                if (Internal.data[key].ContainsKey("{Internal.Source.Timestamp}"))
                {
                    if (DateTime.UtcNow.Subtract(DateTime.Parse(Internal.data[key]["{Internal.Source.Timestamp}"].value)).TotalDays > Internal.cutoff)
                    {
                        if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Removing Data For Asset " + key + " (Last Access " + Internal.data[key]["{Internal.Source.Timestamp}"].value + ")"); }
                        lock (Internal.padlockData)
                        {
                            Internal.data.Remove(key);
                        }
                        asset--;
                    }
                }
            }

            Internal.Reset();
        }
    }
}