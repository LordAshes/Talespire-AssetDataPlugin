using BepInEx;
using BepInEx.Configuration;
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
            public string callbackType { get; set; } = null;
            public string callbackMethod { get; set; } = null;
            public Func<DatumChange, bool> checker { get; set; } = null;
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
            high = 2,
            debug = 999
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
            public const char dividor = '^';

            public static KeyboardShortcut triggerDiagnosticToggle;
            public static KeyboardShortcut triggerSpecificDiagnostic;
            public static KeyboardShortcut triggerDiagnosticSpecificDump; 
            public static KeyboardShortcut triggerDiagnosticDump;
            public static KeyboardShortcut triggerSimData;

            public static int maxRequestAttempts = 100;

            public static void Reset()
            {
                try
                {
                    if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Client Requested Full Reset"); }
                    lock (Internal.padlockData)
                    {
                        Backlog.backlog.Clear();
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
                    }
                    System.IO.File.WriteAllText(pluginPath + "AssetData/AssetDataPlugin." + CampaignSessionManager.Info.Description + "(" + CampaignSessionManager.Id.ToString() + ").json", JsonConvert.SerializeObject(data, Formatting.Indented));
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
                    if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Remote message = " + message); }
                    if (message.StartsWith("/" + AssetDataPlugin.Guid + " "))
                    {
                        message = message.Substring(("/" + AssetDataPlugin.Guid + " ").Length);
                        string[] parts = message.Split(dividor);
                        // Source|Key|ChangeAction|Previous|Value
                        if (diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Remote notification of " + parts[1] + " on " + parts[0] + " (" + parts[2] + ") from " + parts[3] + " to " + parts[4]); }
                        ChangeAction action = ChangeAction.modify;
                        if (diagnostics >= DiagnosticSelection.high) 
                        {
                            if (data.ContainsKey(parts[0].Trim()))
                            {
                                Debug.Log("Asset Data Plugin: Identity Exists: Yes");
                                if (data[parts[0].Trim()].ContainsKey(parts[1]))
                                {
                                    Debug.Log("Asset Data Plugin: Key Exists: Yes");
                                    if(data[parts[0].Trim()][parts[1]].value != parts[4])
                                    {
                                        Debug.Log("Asset Data Plugin: Change: "+ data[parts[0].Trim()][parts[1]].value+" vs "+parts[4]+": Yes");
                                    }
                                    else
                                    {
                                        Debug.Log("Asset Data Plugin: Change: " + data[parts[0].Trim()][parts[1]].value + " vs " + parts[4] + ": No");
                                    }
                                }
                                else
                                {
                                    Debug.Log("Asset Data Plugin: Key Exists: No");
                                }
                            }
                            else
                            {
                                Debug.Log("Asset Data Plugin: Identity Exists: No");
                            }
                        }
                        switch (parts[2].ToUpper())
                        {
                            case "ADD":
                                action = ChangeAction.add;
                                if (!data.ContainsKey(parts[0].Trim()) || !data[parts[0].Trim()].ContainsKey(parts[1]) || data[parts[0].Trim()][parts[1]].value != parts[4])
                                {
                                    if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Remote Add Request Detected"); }
                                    if (parts[0].Trim().ToUpper() != "SYSTEM") { SetInfo(parts[0], parts[1], parts[4]); }
                                }
                                break;
                            case "MODIFY":
                                action = ChangeAction.modify;
                                if (!data.ContainsKey(parts[0].Trim()) || !data[parts[0].Trim()].ContainsKey(parts[1]) || data[parts[0].Trim()][parts[1]].value != parts[4])
                                {
                                    if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Remote Modify Request Detected"); }
                                    if (parts[0].Trim().ToUpper() != "SYSTEM") { SetInfo(parts[0].Trim(), parts[1], parts[4]); }
                                }
                                break;
                            case "REMOVE":
                                action = ChangeAction.remove;
                                if (data.ContainsKey(parts[0].Trim()) && data[parts[0].Trim()].ContainsKey(parts[1]))
                                {
                                    if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Remote Clear Request Detected"); }
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
                        if (diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Datum Changed: " + key + " on " + identity + " changing from " + previous + " to " + value); }
                        lock (padlockSubscriptions)
                        {
                            if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Queuing Callbacks"); }
                            foreach (Subscription subscription in subscriptions)
                            {
                                Wildcard match = new Wildcard(subscription.pattern, RegexOptions.IgnoreCase);
                                bool isMatch = match.IsMatch(key);
                                bool subscriptionRestrictionMatch = ((subscription.subscription == subscriptionId) || (subscriptionId == System.Guid.Empty));
                                bool patternRestrictionMatch = (subscription.pattern == pattern || pattern == null);
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Subscription: "+subscription.pattern+", Key: "+key+", Match: "+ isMatch+" (Subscription Restriction: " + subscriptionRestrictionMatch+", Pattern Restriction: "+patternRestrictionMatch+")"); }
                                if (isMatch && subscriptionRestrictionMatch && patternRestrictionMatch)
                                {
                                    Backlog.Add(subscription.subscription.ToString(), new Backlog.BacklogItem()
                                    {
                                        request = new DatumChange()
                                        {
                                            action = action,
                                            key = key,
                                            source = identity,
                                            previous = previous,
                                            value = value
                                        },
                                        subscription = subscription
                                    });
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
                if(diagnostics >= DiagnosticSelection.debug)
                {
                    Debug.Log("Asset Data Plugin: SendPackets: identity=" + identity);
                    Debug.Log("Asset Data Plugin: SendPackets: key=" + key);
                    Debug.Log("Asset Data Plugin: SendPackets: action=" + Convert.ToString(action));
                    Debug.Log("Asset Data Plugin: SendPackets: value=" + value);
                    Debug.Log("Asset Data Plugin: SendPackets: legacy=" + legacy);
                }
                if (!legacy)
                {
                    if (diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: SendPackets: Asset Data Send Mode"); }
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
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Change To Other Clients (Initial Packet " + (i+1) + " of " + packets + ": " + msg.Substring(0, 100)+")"); }
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + identity.ToString() + dividor + key + dividor + action + dividor + dividor + msg.Substring(0, 100), LocalPlayer.Id.Value);
                            }
                            else if (msg.Length >= 100)
                            {
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Change To Other Clients (Packet " + (i+1) + " of " + packets + ": " + msg.Substring(0, 100)+")"); }
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + msg.Substring(0, 100), LocalPlayer.Id.Value);
                            }
                            else
                            {
                                if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Change To Other Clients (End Packet " + (i+1) + " of " + packets + ": " + msg+")"); }
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + ".Multi " + id.ToString() + ":" + i + ":" + packets + " " + msg, LocalPlayer.Id.Value);
                            }
                            if (msg.Length >= 100) { msg = msg.Substring(100); } else { msg = ""; }
                        }
                    }
                    else
                    {
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Change To Other Clients (Packet: " + msg +")"); }
                        if (messageDistributor != null)
                        {
                            try
                            {
                                messageDistributor.SendMessage("/" + AssetDataPlugin.Guid + " " + identity.ToString() + dividor + key + dividor + action + dividor + dividor + msg, LocalPlayer.Id.Value);
                            }
                            catch(Exception x)
                            {
                                Debug.LogWarning("Asset Data Plugin: Problem distributing the message via the message distributor");
                                Debug.LogException(x);
                            }
                        }
                        else
                        {
                            Debug.LogWarning("Asset Data Plugin: Message cannot be distributed to others becauase no message distribution plugin (e.g. RPC Plugin, Chat Service or similar plugin is present)");
                        }
                    }
                }
                else
                {
                    if (diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: SendPackets: Legacy Send Mode"); }
                    if (action.ToUpper() != "REMOVE")
                    {
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Legacy Set To Other Clients (Packet: " + identity+", "+key+", "+value+ ")"); }
                        if (Legacy.setInfo != null)
                        {
                            if (diagnostics >= DiagnosticSelection.debug) { Debug.LogWarning("Asset Data Plugin: SendPackets: Legacy Set Info."); }
                            try
                            {
                                if (Legacy.setInfo != null)
                                {
                                    Legacy.setInfo.Invoke(null, new object[] { new CreatureGuid(identity), key, value });
                                }
                                else
                                {
                                    Debug.LogWarning("Asset Data Plugin: Legacy suppot is not available. Ensure Stat Messaging Plugin is installed.");
                                }
                            }
                            catch(Exception x)
                            {
                                Debug.LogWarning("Asset Data Plugin: Problem using Legacy SetInfo");
                                Debug.LogException(x);
                            }
                        }
                        else
                        {
                            if (diagnostics >= DiagnosticSelection.debug) { Debug.LogWarning("Asset Data Plugin: SendPackets: Legacy Mode Not Available. Ensure Stat Messaging Is Downlownloaded."); }
                        }
                    }
                    else
                    {
                        if (diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Legacy Clear To Other Clients (Packet: " + identity + ", " + key + ", " + value + ")"); }
                        if (Legacy.clearInfo != null)
                        {
                            if (diagnostics >= DiagnosticSelection.debug) { Debug.LogWarning("Asset Data Plugin: SendPackets: Legacy Clear Info."); }
                            try
                            {
                                if (Legacy.setInfo != null)
                                {
                                    Legacy.clearInfo.Invoke(null, new object[] { new CreatureGuid(identity), key });
                                }
                                else
                                {
                                    Debug.LogWarning("Asset Data Plugin: Legacy suppot is not available. Ensure Stat Messaging Plugin is installed.");
                                }
                            }
                            catch(Exception x)
                            {
                                Debug.LogWarning("Asset Data Plugin: Problem using Legacy ClearInfo");
                                Debug.LogException(x);
                            }
                        }
                        else
                        {
                            if (diagnostics >= DiagnosticSelection.debug) { Debug.LogWarning("Asset Data Plugin: SendPackets: Legacy Mode Not Available. Ensure Stat Messaging Is Downlownloaded."); }
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
              + Internal.dividor
              + "LordAshes.ChatServicePlugin+ChatMessageService, ChatServicePlugin"
            ).Value;

            if(Internal.diagnostics >= DiagnosticSelection.high) Debug.Log("Asset Data Plugin: Looking For Message Distributor From " + distributors);

            foreach (string distributor in distributors.Split(Internal.dividor))
            {
                try
                {
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Testing Message Distributor " + distributor); }
                    Type type = null;
                    try { type = Type.GetType(distributor); if (type == null) { throw new Exception("Unable To Get A Reference To " + distributor + " Type (Result Null)"); } } catch(Exception) { throw new Exception("Unable To Get A Reference To " + distributor + " Type (Result Exception)");  }
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Obtained Reference To " + distributor + " Type"); }
                    try { if (type.GetMethod("AddHandler") == null) { throw new Exception("Missing AddHandler Method (Result Null)"); } } catch (Exception) { throw new Exception("Missing AddHandler Method (Result Exception)"); }
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: AddHandler Method Present"); }
                    try { if (type.GetMethod("RemoveHandler") == null) { throw new Exception("Missing RemoveHandler Method (Result Null)"); } } catch (Exception) { throw new Exception("Missing RemoveHandler Method (Result Exception)"); }
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: RemoveHandler Method Present"); }
                    try { if (type.GetMethod("SendMessage") == null) { throw new Exception("Missing SendMessage Method (Result Null)"); } } catch (Exception) { throw new Exception("Missing SendMessage Method (Result Exception)"); }
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: SendMessage Method Present"); }
                    Internal.messageDistributor = new Distributor(type);
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Making Distributor Subscriptions"); }
                    Internal.messageDistributor.AddHandler("/" + AssetDataPlugin.Guid, Internal.ProcessRemoteChange);
                    Internal.messageDistributor.AddHandler("/" + AssetDataPlugin.Guid + ".Multi", Internal.ProcessRemoteChange);
                    if (Internal.diagnostics >= DiagnosticSelection.low) { Debug.Log("Asset Data Plugin: Using Message Distributor " + distributor); }
                    break;
                }
                catch (Exception ex)
                {
                    if (Internal.diagnostics >= DiagnosticSelection.high)
                    {
                        Debug.Log("Asset Data Plugin: Distributor '" + distributor + "' rejected because " + ex.Message);
                        try
                        {
                            Type type = Type.GetType(distributor);
                            if (type != null)
                            {
                                MethodInfo[] methods = type.GetMethods();
                                foreach (MethodInfo method in methods)
                                {
                                    Debug.LogWarning("Asset Data Plugin: Distributor '" + distributor + "' has method '" + method.Name + "'");
                                }
                            }
                        }
                        catch (Exception) {; }
                    }
                }
            }
            if (Internal.messageDistributor == null)
            {
                Debug.LogError("Asset Data Plugin: No Usable Message Distributor Found");
                SystemMessage.AskForTextInput("Missing Choice Plugin",
                                              "Please download RPC, Chat Service or similar plugin",
                                              "Exit Talepsire", (s) =>  
                                              {
                                                  AppStateManager.ForceQuitNoUiNoSync();
                                              }, null,
                                              "Understood", null, "Running in Local Mode Only.");
            }
        }
    }
}
