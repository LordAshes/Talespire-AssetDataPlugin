using BepInEx;
using Bounce.Unmanaged;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LordAshes
{
    public partial class AssetDataPlugin : BaseUnityPlugin
    {
        /// <summary>
        /// Class for holding subscriptions
        /// </summary>
        public class Subscription
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

        public class Distributor
        {
            Type _source = null;

            public Distributor(Type source)
            {
                _source = source;
            }

            public void AddHandler(string key, Func<string, string, Talespire.SourceRole, string> callback)
            {
                MethodInfo method = _source.GetMethod("AddHandler");
                method.Invoke(null, new object[] { key, callback });
            }

            public void RemoveHandler(string key)
            {
                MethodInfo method = _source.GetMethod("RemoveHandler");
                method.Invoke(null, new object[] { key });
            }

            public void SendMessage(string message, NGuid source)
            {
                MethodInfo method = _source.GetMethod("SendMessage");
                method.Invoke(null, new object[] { message, source });
            }
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

        public enum DiagnosticSelection
        {
            none = 0,
            low = 1,
            high = 2
        }

        public static class Internal
        {
            public static DiagnosticSelection diagnostics = DiagnosticSelection.low;
            public static int cutoff = int.MaxValue;

            public static object padlockData = new object();
            public static Dictionary<string, Dictionary<string, AssetDataPlugin.Datum>> data = new Dictionary<string, Dictionary<string, Datum>>();
            private static Dictionary<string, string[]> multiPacketBuffer = new Dictionary<string, string[]>();

            public static object padlockSubscriptions = new object();
            public static List<Subscription> subscriptions = new List<Subscription>();

            public static Distributor messageDistributor = null;

            public static string pluginPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "/";
            public const char dividor = '|';

            public static void Reset()
            {
                try
                {
                    if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Requested Full Reset"); }
                    lock (Internal.padlockData)
                    {
                        foreach (KeyValuePair<string, Dictionary<string, Datum>> source in Internal.data)
                        {
                            foreach (KeyValuePair<string, Datum> datum in source.Value)
                            {
                                Internal.DatumUpdate(ChangeAction.initial, source.Key, datum.Key, datum.Value.previous, datum.Value.value, System.Guid.Empty, null);
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

            public static void Reset(System.Guid subscriptionId)
            {
                try
                {
                    if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Requested Reset Associated With Subscription " + subscriptionId); }
                    lock (Internal.padlockData)
                    {
                        foreach (KeyValuePair<string, Dictionary<string, Datum>> source in Internal.data)
                        {
                            foreach (KeyValuePair<string, Datum> datum in source.Value)
                            {
                                Internal.DatumUpdate(ChangeAction.initial, source.Key, datum.Key, datum.Value.previous, datum.Value.value, subscriptionId, null);
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

            public static void Reset(string pattern)
            {
                try
                {
                    if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Requested Reset Associated With Key " + pattern); }
                    lock (Internal.padlockData)
                    {
                        foreach (KeyValuePair<string, Dictionary<string, Datum>> source in Internal.data)
                        {
                            foreach (KeyValuePair<string, Datum> datum in source.Value)
                            {
                                Internal.DatumUpdate(ChangeAction.initial, source.Key, datum.Key, datum.Value.previous, datum.Value.value, System.Guid.Empty, pattern);
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

            public static void SetInfo(string identity, string key, string value)
            {
                try
                {
                    if (identity.ToUpper() == "SYSTEM") { return; }
                    if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: _SetInfo : Client Set " + key + " on " + identity + " to " + value); }
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
                        System.IO.File.WriteAllText(pluginPath + "AssetData/AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json", JsonConvert.SerializeObject(data, Formatting.Indented));
                    }
                }
                catch (Exception x)
                {
                    Debug.Log("Asset Data Plugin: Exception In _SetInfo");
                    Debug.LogException(x);
                }
            }

            public static void ClearInfo(string identity, string key)
            {
                try
                {
                    if (identity.ToUpper() == "SYSTEM") { return; }
                    if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: _ClearInfo: Client Cleared " + key + " on " + identity); }
                    string _identity = identity.ToString();
                    lock (padlockData)
                    {
                        if (!data.ContainsKey(_identity)) { return; }
                        if (!data[_identity].ContainsKey(key)) { return; }
                        object lastValue = data[_identity][key];
                        data[_identity].Remove(key);
                        ClearInfo(identity, key);
                    }
                }
                catch (Exception x)
                {
                    Debug.Log("Asset Data Plugin: Exception In _ClearInfo");
                    Debug.LogException(x);
                }
            }

            public static string ProcessRemoteChange(string message, string sender, Talespire.SourceRole source)
            {
                try
                {
                    if (message.StartsWith("/" + AssetDataPlugin.Guid + " "))
                    {
                        message = message.Substring(("/" + AssetDataPlugin.Guid + " ").Length);
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Message="+message); }
                        string[] parts = message.Split(dividor);
                        // Source|Key|ChangeAction|Previous|Value
                        if (diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: ProcessRemoteChange: Remote notification of " + parts[1] + " on " + parts[0] + " (" + parts[2] + ") from " + parts[3] + " to " + parts[4]); }
                        ChangeAction action = ChangeAction.modify;
                        switch (parts[2].ToUpper())
                        {
                            case "ADD":
                                action = ChangeAction.add;
                                if (!data.ContainsKey(parts[0].Trim()) || !data[parts[0].Trim()].ContainsKey(parts[1]) || data[parts[0].Trim()][parts[1]].value != parts[4])
                                {
                                    if (parts[0].Trim().ToUpper() != "SYSTEM") { SetInfo(parts[0], parts[1], parts[4]); }
                                }
                                break;
                            case "MODIFY":
                                action = ChangeAction.modify;
                                if (!data.ContainsKey(parts[0].Trim()) || !data[parts[0].Trim()].ContainsKey(parts[1]) || data[parts[0].Trim()][parts[1]].value != parts[4])
                                {
                                    if (parts[0].Trim().ToUpper() != "SYSTEM") { SetInfo(parts[0].Trim(), parts[1], parts[4]); }
                                }
                                break;
                            case "REMOVE":
                                action = ChangeAction.remove;
                                if (data.ContainsKey(parts[0].Trim()) && data[parts[0].Trim()].ContainsKey(parts[1]))
                                {
                                    if (parts[0].Trim().ToUpper() != "SYSTEM") { ClearInfo(parts[0].Trim(), parts[1]); }
                                }
                                break;
                        }
                        DatumUpdate(action, parts[0].Trim(), parts[1], parts[3], parts[4], System.Guid.Empty, null);
                    }
                    else // if (message.StartsWith("/" + AssetDataPlugin.Guid + ".Multi "))
                    {
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Multi Packet Message Detected"); }
                        message = message.Substring(("/" + AssetDataPlugin.Guid + ".Multi").Length).Trim();
                        string[] specs = message.Substring(0, message.IndexOf(" ")).Split(':');
                        message = message.Substring(message.IndexOf(" ") + 1);
                        if (!multiPacketBuffer.ContainsKey(specs[0]))
                        {
                            if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: New Multi Packet Message Detected. Creating Key " + specs[0] + " For " + specs[2] + " Packets"); }
                            multiPacketBuffer.Add(specs[0], new string[int.Parse(specs[2])]);
                        }
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Storing Packet " + specs[1] + " Of " + specs[2] + " = " + message); }
                        multiPacketBuffer[specs[0]][int.Parse(specs[1])] = message;
                        bool ready = true;
                        for (int i = 0; i < int.Parse(specs[2]); i++)
                        {
                            if (multiPacketBuffer[specs[0]][i] == null)
                            {
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Missing Packet " + i); }
                                ready = false; break;
                            }
                        }
                        if (ready)
                        {
                            if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Entire Multi Packet Message Is Readay. Processing"); }
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

            public static void DatumUpdate(ChangeAction action, string identity, string key, object previous, object value, System.Guid subscriptionId, string pattern)
            {
                try
                {
                    if (key != "{Internal.Source.Timestamp}")
                    {
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: DatumUpdate: Sending Out Callbacks For " + key + " on " + identity + " changing from " + previous + " to " + value); }
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
                }
                catch (Exception x)
                {
                    Debug.Log("Asset Data Plugin: Exception In DatumUpdate");
                    Debug.LogException(x);
                }
            }

            public static void SendPackets(string identity, string key, string action, string value, bool legacy = false)
            {
                if (!legacy)
                {
                    string msg = value;
                    if (msg.Length > 100)
                    {
                        float packets = ((float)msg.Length / 100f);
                        if (packets != Math.Floor(packets))
                        {
                            packets = (float)Math.Floor(packets) + 1f;
                        }
                        System.Guid id = System.Guid.NewGuid();
                        for (int i = 0; i < packets; i++)
                        {
                            if (i == 0)
                            {
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Sending Initial Packet " + i + " of " + packets + ": " + msg.Substring(0, 100)); }
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + identity.ToString() + dividor + key + dividor + action + dividor + dividor + msg.Substring(0, 100), LocalPlayer.Id.Value);
                            }
                            else if (msg.Length >= 100)
                            {
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Sending Packet " + i + " of " + packets + ": " + msg.Substring(0, 100)); }
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + msg.Substring(0, 100), LocalPlayer.Id.Value);
                            }
                            else
                            {
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Sending End Packet " + i + " of " + packets + ": " + msg); }
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + msg, LocalPlayer.Id.Value);
                            }
                            if (msg.Length >= 100) { msg = msg.Substring(100); } else { msg = ""; }
                        }
                    }
                    else
                    {
                        messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + " " + identity.ToString() + dividor + key + dividor + action + dividor + dividor + msg, LocalPlayer.Id.Value);
                    }
                }
                else
                {
                    if (action.ToUpper() != "REMOVE")
                    {
                        if (Legacy.setInfo != null)
                        {
                            Legacy.setInfo.Invoke(null, new object[] { new CreatureGuid(identity), key, value });
                        }
                    }
                    else
                    {
                        if (Legacy.clearInfo != null)
                        {
                            Legacy.clearInfo.Invoke(null, new object[] { new CreatureGuid(identity), key });
                        }
                    }
                }
            }
        }

        public IEnumerator GetDistributor(object[] inputs)
        {
            yield return new WaitForSeconds((float)inputs[0]);
            if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Looking For Message Distributor"); }
            string distributors = Config.Bind("Settings", "Client Distribution Plugins In Order Of preference",
                "RPCPlugin.RPC.RPCManager, RPCPlugin"
              + "|"
              + "LordAshes.ChatServicePlugin+ChatMessageService, ChatServicePlugin"
            ).Value;

            if(Internal.diagnostics >= DiagnosticSelection.high) Debug.Log("Asset Data Plugin: Looking For Message Distributor From " + distributors);

            foreach (string distributor in distributors.Split('|'))
            {
                try
                {
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Testing Message Distributor " + distributor); }
                    Type type = null;
                    try
                    {
                        type = Type.GetType(distributor);
                    }
                    catch(Exception)
                    {
                        throw new Exception("Unable To Get A Reference To " + distributor + " Type"); 
                    }
                    if (type.GetMethod("AddHandler") == null) { throw new Exception("Missing AddHandler Method"); }
                    if (type.GetMethod("RemoveHandler") == null) { throw new Exception("Missing AddHandler Method"); }
                    if (type.GetMethod("SendMessage") == null) { throw new Exception("Missing AddHandler Method"); }
                    if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Using Message Distributor " + distributor); }
                    Internal.messageDistributor = new Distributor(type);
                    Internal.messageDistributor.AddHandler("/" + AssetDataPlugin.Guid, Internal.ProcessRemoteChange);
                    Internal.messageDistributor.AddHandler("/" + AssetDataPlugin.Guid + ".Multi", Internal.ProcessRemoteChange);
                    break;
                }
                catch (Exception ex)
                {
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Distributor '" + distributor + "' rejected because " + ex.Message); }
                }
            }
            if (Internal.messageDistributor == null)
            {
                Debug.LogError("Asset Data Plugin: No Usable Message Distributor Found");
                Environment.Exit(1);
            }
        }
    }
}
