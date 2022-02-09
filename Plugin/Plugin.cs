using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;


namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(ChatServicePlugin.Guid)]
    public partial class AssetDataPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Asset Data Plug-In";
        public const string Guid = "org.lordashes.plugins.assetdata";
        public const string Version = "1.0.0.0";

        // Configuration
        const char dividor = '|';

        private static Dictionary<string, Dictionary<string, Datum>> data = new Dictionary<string, Dictionary<string, Datum>>();
        private static List<Subscription> subscriptions = new List<Subscription>();
        private static Dictionary<string,string[]> multiPacketBuffer = new Dictionary<string, string[]>();
        private static object padlockSubscriptions = new object();
        private static object padlockData = new object();

        private static bool diagnostics = false;

        private static string dataPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "/";

        private static int cutoff = int.MaxValue;

        /// <summary>
        /// Class for holding subscriptions
        /// </summary>
        private class Subscription
        {
            public System.Guid subscription { get; set; } = System.Guid.Empty;
            public string pattern { get; set; } = "*";
            public Action<DatumChange> callback { get; set; } = null;
        }

        /// <summary>
        /// Class for holding a the previous and current piece of information
        /// </summary>
        public class Datum
        {
            public string previous { get; set; } = null;
            public string value { get; set; } = null;

        }

        /// <summary>
        /// Class for holding information about a data change
        /// </summary>
        public class DatumChange
        {
            /// <summary>
            /// Type of data change that has occured
            /// </summary>
            public ChangeAction action { get; set; } = ChangeAction.invalid;

            /// <summary>
            /// The id of the asset that this change is associated with
            /// </summary>
            public string source { get; set; } = null;

            /// <summary>
            /// The name of the information that has changed
            /// </summary>
            public string key { get; set; } = "";

            /// <summary>
            /// The previous value of the information prior to the change
            /// </summary>
            public object previous { get; set; } = null;

            /// <summary>
            /// The current value of the information after the change
            /// </summary>
            public object value { get; set; } = null;
        }

        /// <summary>
        /// Enumeration for the type of data change
        /// </summary>
        public enum ChangeAction
        {
            invalid = -1,
            initial = 0,
            remove = 1,
            add = 2,
            modify = 3,
        }

        void Awake()
        {
            Debug.Log("Asset Data Plugin: Active.");

            diagnostics = Config.Bind("Settings", "Diagnostics", false).Value;

            cutoff = Config.Bind("Settings", "Number of days to data for unreferenced asset", 30).Value;

            CampaignSessionManager.OnCampaignChanged += () =>
            {
                if (System.IO.File.Exists(dataPath + "AssetDataPlugin."+CampaignSessionManager.Info.Description+"("+CampaignSessionManager.Id.ToString()+").json"))
                {
                    if (diagnostics) { Debug.Log("Asset Data Plugin: Previous Data Found. Loading AssetDataPlugin Data..."); }
                    data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Datum>>>(System.IO.File.ReadAllText(dataPath + "AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json"));
                }

                for (int asset = 0; asset < data.Keys.Count; asset++)
                {
                    string key = data.Keys.ElementAt(asset);
                    if (DateTime.UtcNow.Subtract(DateTime.Parse(data[key]["{Internal.Source.Timestamp}"].value)).TotalDays > cutoff)
                    {
                        if (diagnostics) { Debug.Log("Asset Data Plugin: Removing Data For Asset " + key + " (Last Access " + data[key]["{Internal.Source.Timestamp}"].value + ")"); }
                        data.Remove(key);
                        asset--;
                    }
                }
            };

            ChatServicePlugin.handlers.Add("/" + AssetDataPlugin.Guid, ProcessRemoteChange);
            ChatServicePlugin.handlers.Add("/" + AssetDataPlugin.Guid+".Multi", ProcessRemoteChange);
        }

        /// <summary>
        /// Subscription for notification when the given key (or key pattern) changes for any asset
        /// </summary>
        /// <param name="pattern">The key or a wild card key for which notificatons are desired</param>
        /// <param name="callback">The callback method that is triggered when a notification occurs</param>
        /// <returns>Subscription id which can be used in other methods like unsubscribe</returns>
        public static System.Guid Subscribe(string pattern, Action<DatumChange> callback)
        {
            if (diagnostics) { Debug.Log("Asset Data Plugin: Client Subscribed To " + pattern); }
            System.Guid identity = System.Guid.NewGuid();
            lock (padlockSubscriptions)
            {
                subscriptions.Add(new Subscription() { subscription = identity, pattern = pattern, callback = callback });
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
            if (diagnostics) { Debug.Log("Asset Data Plugin: Client Unsubscribed Subscription " + subscriptionId); }
            lock (padlockSubscriptions)
            {
                for (int s = 0; s < subscriptions.Count; s++)
                {
                    if (subscriptions[s].subscription == subscriptionId)
                    {
                        subscriptions.RemoveAt(s);
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
                if (diagnostics) { Debug.Log("Asset Data Plugin: Client Requested Full Reset"); }
                lock (padlockData)
                {
                    foreach (KeyValuePair<string, Dictionary<string, Datum>> source in data)
                    {
                        foreach (KeyValuePair<string, Datum> datum in source.Value)
                        {
                            DatumUpdate(ChangeAction.initial, source.Key, datum.Key, datum.Value.previous, datum.Value.value, System.Guid.Empty, null);
                        }
                    }
                }
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
                if (diagnostics) { Debug.Log("Asset Data Plugin: Client Requested Reset Associated With Subscription " + subscriptionId); }
                lock (padlockData)
                {
                    foreach (KeyValuePair<string, Dictionary<string, Datum>> source in data)
                    {
                        foreach (KeyValuePair<string, Datum> datum in source.Value)
                        {
                            DatumUpdate(ChangeAction.initial, source.Key, datum.Key, datum.Value.previous, datum.Value.value, subscriptionId, null);
                        }
                    }
                }
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
                if (diagnostics) { Debug.Log("Asset Data Plugin: Client Requested Reset Associated With Key " + pattern); }
                lock (padlockData)
                {
                    foreach (KeyValuePair<string, Dictionary<string, Datum>> source in data)
                    {
                        foreach (KeyValuePair<string, Datum> datum in source.Value)
                        {
                            DatumUpdate(ChangeAction.initial, source.Key, datum.Key, datum.Value.previous, datum.Value.value, System.Guid.Empty, pattern);
                        }
                    }
                }
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
        public static void SetInfo(string identity, string key, string value)
        {
            try
            {
                if (diagnostics) { Debug.Log("Asset Data Plugin: SetInfo: Client Set " + key + " on " + identity + " to " + value); }
                lock (padlockData)
                {
                    if (!data.ContainsKey(identity) || !data[identity].ContainsKey(key))
                    {
                        // ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + " " + identity.ToString() + dividor + key + dividor + "add" + dividor + dividor + value, LocalPlayer.Id.Value);
                        SendPackets(identity, key, "add", value);
                    }
                    else
                    {
                        // ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + " " + identity.ToString() + dividor + key + dividor + "modify" + dividor + data[identity][key].value + dividor + value, LocalPlayer.Id.Value);
                        SendPackets(identity, key, "modify", value);
                    }
                    _SetInfo(identity, key, value);
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
        public static void SetInfo(string identity, string key, object value)
        {
            try
            {
                SetInfo(identity, key, JsonConvert.SerializeObject(value));
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
        public static void ClearInfo(string identity, string key, string value)
        {
            try
            {
                if (diagnostics) { Debug.Log("Asset Data Plugin: ClearInfo: Client Cleared " + key + " on " + identity); }
                ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + " " + identity.ToString() + dividor + key + dividor + "remove" + dividor + data[identity][key].value + dividor, LocalPlayer.Id.Value);
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
                if (diagnostics) { Debug.Log("Asset Data Plugin: ReadInfo: Client Read " + key + " on " + identity); }
                string _identity = identity.ToString();
                lock (padlockData)
                {
                    if (!data.ContainsKey(_identity)) { return null; }
                    if (!data[_identity].ContainsKey(key)) { return null; }
                    return data[_identity][key].value;
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
                if (diagnostics) { Debug.Log("Asset Data Plugin: ReadInfo: Client Read " + key + " on " + identity); }
                lock (padlockData)
                {
                    if (!data.ContainsKey(identity)) { return null; }
                    if (!data[identity].ContainsKey(key)) { return null; }
                    return data[identity][key];
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In ReadDatum");
                Debug.LogException(x);
                return null;
            }
        }

        private static void _SetInfo(string identity, string key, string value)
        {
            try
            {
                if (diagnostics) { Debug.Log("Asset Data Plugin: _SetInfo : Client Set " + key + " on " + identity + " to " + value); }
                lock (padlockData)
                {
                    if (!data.ContainsKey(identity))
                    {
                        data.Add(identity, new Dictionary<string, Datum>());
                        data[identity].Add("{Internal.Source.Timestamp}", new Datum() { previous = null, value = DateTime.UtcNow.ToString() });
                    }
                    else
                    {
                        data[identity]["{Internal.Source.Timestamp}"].value = DateTime.UtcNow.ToString();
                    }
                    if (!data[identity].ContainsKey(key))
                    {
                        data[identity].Add(key, new Datum() { previous = null, value = value });
                    }
                    else
                    {
                        data[identity][key].previous = data[identity][key].value;
                        data[identity][key].value = value;
                    }
                    System.IO.File.WriteAllText(dataPath + "AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json", JsonConvert.SerializeObject(data, Formatting.Indented));
                }
            }
            catch(Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In _SetInfo");
                Debug.LogException(x);
            }
        }

        private static void _ClearInfo(string identity, string key)
        {
            try
            {
                if (diagnostics) { Debug.Log("Asset Data Plugin: _ClearInfo: Client Cleared " + key + " on " + identity); }
                string _identity = identity.ToString();
                lock (padlockData)
                {
                    if (!data.ContainsKey(_identity)) { return; }
                    if (!data[_identity].ContainsKey(key)) { return; }
                    object lastValue = data[_identity][key];
                    data[_identity].Remove(key);
                    _ClearInfo(identity, key);
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In _ClearInfo");
                Debug.LogException(x);
            }
        }

        private string ProcessRemoteChange(string message, string sender, ChatServicePlugin.ChatSource source)
        {
            try
            {
                if (message.StartsWith("/" + AssetDataPlugin.Guid + " "))
                {
                    message = message.Substring(("/" + AssetDataPlugin.Guid).Length);
                    string[] parts = message.Split(dividor);
                    if (diagnostics) { Debug.Log("Asset Data Plugin: ProcessRemoteChange: Remote notification of " + parts[1] + " on " + parts[0] + " (" + parts[2] + ") from " + parts[3] + " to " + parts[4]); }
                    ChangeAction action = ChangeAction.modify;
                    switch (parts[2].ToUpper())
                    {
                        case "ADD":
                            action = ChangeAction.add;
                            if (!data.ContainsKey(parts[0].Trim()) || !data[parts[0].Trim()].ContainsKey(parts[1]) || data[parts[0].Trim()][parts[1]].value != parts[4])
                            {
                                if (parts[0].Trim().ToUpper() != "SYSTEM") { _SetInfo(parts[0], parts[1], parts[4]); }
                            }
                            break;
                        case "MODIFY":
                            action = ChangeAction.modify;
                            if (!data.ContainsKey(parts[0].Trim()) || !data[parts[0].Trim()].ContainsKey(parts[1]) || data[parts[0].Trim()][parts[1]].value != parts[4])
                            {
                                if (parts[0].Trim().ToUpper() != "SYSTEM") { _SetInfo(parts[0].Trim(), parts[1], parts[4]); }
                            }
                            break;
                        case "REMOVE":
                            action = ChangeAction.remove;
                            if (data.ContainsKey(parts[0].Trim()) && data[parts[0].Trim()].ContainsKey(parts[1]))
                            {
                                if (parts[0].Trim().ToUpper() != "SYSTEM") { _ClearInfo(parts[0].Trim(), parts[1]); }
                            }
                            break;
                    }
                    DatumUpdate(action, parts[0].Trim(), parts[1], parts[3], parts[4], System.Guid.Empty, null);
                }
                else // if (message.StartsWith("/" + AssetDataPlugin.Guid + ".Multi "))
                {
                    Debug.Log("Asset Data Plugin: Multi Packet Message Detected");
                    message = message.Substring(("/" + AssetDataPlugin.Guid + ".Multi").Length).Trim();
                    string[] specs = message.Substring(0, message.IndexOf(" ")).Split(':');
                    message = message.Substring(message.IndexOf(" ") + 1);
                    if(!multiPacketBuffer.ContainsKey(specs[0]))
                    {
                        Debug.Log("Asset Data Plugin: New Multi Packet Message Detected. Creating Key "+specs[0]+" For "+specs[2]+" Packets");
                        multiPacketBuffer.Add(specs[0], new string[int.Parse(specs[2])]);
                    }
                    Debug.Log("Asset Data Plugin: Storing Packet " + specs[1] + " Of " + specs[2]+" = "+message);
                    multiPacketBuffer[specs[0]][int.Parse(specs[1])] = message;
                    Debug.Log("Asset Data Plugin: Checking If Entire Message Has Been Received");
                    bool ready = true;
                    for(int i=0; i<int.Parse(specs[2]); i++)
                    {
                        if (multiPacketBuffer[specs[0]][i] == null)
                        {
                            Debug.Log("Asset Data Plugin: Missing Packet "+i);
                            ready = false; break; 
                        }
                    }
                    if(ready)
                    {
                        Debug.Log("Asset Data Plugin: Entire Multi Packet Message Is Readay. Processing");
                        string completeMessage = String.Join("", multiPacketBuffer[specs[0]]);
                        multiPacketBuffer.Remove(specs[0]);
                        ProcessRemoteChange("/" + AssetDataPlugin.Guid + " " + completeMessage, sender, source);
                    }
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In _ProcessRemoteChange");
                Debug.LogException(x);
            }
            return null;
        }

        private static void DatumUpdate(ChangeAction action, string identity, string key, object previous, object value, System.Guid subscriptionId, string pattern)
        {
            try
            {
                if (diagnostics) { Debug.Log("Asset Data Plugin: DatumUpdate: Sending Out Callbacks For " + key + " on " + identity + " changing from " + previous + " to " + value); }
                lock (padlockSubscriptions)
                {
                    foreach (Subscription subscription in subscriptions)
                    {
                        Wildcard match = new Wildcard(subscription.pattern, RegexOptions.IgnoreCase);
                        if (match.IsMatch(key) && ((subscription.subscription == subscriptionId) || (subscriptionId == System.Guid.Empty)) && (subscription.pattern == pattern || pattern == null))
                        {
                            subscription.callback(new DatumChange() { action = action, source = identity, key = key, previous = previous, value = value });
                        }
                    }
                }
            }
            catch (Exception x)
            {
                Debug.Log("Asset Data Plugin: Exception In DatumUpdate");
                Debug.LogException(x);
            }
        }

        private static void SendPackets(string identity, string key, string action, string value)
        {
            string msg = value;
            if (msg.Length > 100)
            {
                float packets = ((float)msg.Length / 100f);
                Debug.Log("Packets = " + packets);
                if(packets != Math.Floor(packets)) 
                {
                    Debug.Log("Round up");
                    packets = (float)Math.Floor(packets)+1f;
                    Debug.Log("Packets = " + packets);
                }
                System.Guid id = System.Guid.NewGuid();
                for(int i=0; i<packets; i++)
                {
                    if (i == 0)
                    {
                        Debug.Log("Sending Initial Packet " + i + " of " + packets + ": " + msg.Substring(0, 100));
                        ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + identity.ToString() + dividor + key + dividor + action + dividor + dividor + msg.Substring(0, 100), LocalPlayer.Id.Value);
                    }
                    else if(msg.Length>=100)
                    {
                        Debug.Log("Sending Packet " + i + " of " + packets + ": " + msg.Substring(0, 100));
                        ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + msg.Substring(0, 100), LocalPlayer.Id.Value);
                    }
                    else
                    {
                        Debug.Log("Sending End Packet " + i + " of " + packets + ": " + msg);
                        ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + msg, LocalPlayer.Id.Value);
                    }
                    if (msg.Length >= 100) { msg = msg.Substring(100); } else { msg = ""; }
                }
            }
            else
            {
                ChatManager.SendChatMessage("/" + AssetDataPlugin.Guid + " " + msg, LocalPlayer.Id.Value);
            }
        }
    }
}