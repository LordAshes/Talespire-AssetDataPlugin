using BepInEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LordAshes
{
    public partial class AssetDataPlugin : BaseUnityPlugin
    {
        public static class Backlog
        {
            public class BacklogItem
            {
                public AssetDataPlugin.DatumChange request { get; set; }
                public Subscription subscription { get; set; }
                public int failures { get; set; } = 0;
            }

            public static Dictionary<string, ConcurrentQueue<BacklogItem>> backlog = new Dictionary<string, ConcurrentQueue<BacklogItem>>();

            public static void Process()
            {
                if (backlog.Count == 0) { return; }
                for(int q=0; q<backlog.Count; q++)
                {
                    if (backlog.ElementAt(q).Value.Count == 0)
                    {
                        // Remove empty queues
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Removing Queue"); }
                        backlog.Remove(backlog.ElementAt(q).Key);
                    }
                    else
                    {
                        // Process next item in queue
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Backlog Queue '" + backlog.ElementAt(q).Value.ElementAt(0).subscription.subscription + "' Has " + backlog.ElementAt(q).Value.Count + " Items"); }
                        Next(backlog.ElementAt(q).Key);
                    }
                }
            }

            /// <summary>
            /// Add backlog item
            /// </summary>
            /// <param name="queueName"></param>
            /// <param name="request"></param>
            /// <returns></returns>
            public static int Add(string queueName, BacklogItem request)
            {
                if (!backlog.ContainsKey(queueName)) { backlog.Add(queueName, new ConcurrentQueue<BacklogItem>()); }
                backlog[queueName].Enqueue(request);
                return backlog.Count;
            }

            /// <summary>
            /// Process next backlog queue item
            /// </summary>
            /// <param name="queueName"></param>
            public static void Next(string queueName)
            {
                BacklogItem item = null;
                // Get next backlog item
                if(backlog[queueName].TryDequeue(out item))
                {
                    if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Backlog Dequeue Successful. A: "+item.request.action.ToString()+", S:"+item.request.source+", K:"+item.request.key+", V:"+item.request.value); }
                    // If a check function was provided, evaluate check
                    if (item.subscription.checker==null)
                    {
                        // If check is passed or not provided, send notification
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: No check performed. Sending Notification..."); }
                        SendNotification(item);
                    }
                    else if (item.subscription.checker(item.request))
                    {
                        // If check is passed or not provided, send notification
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Check passed. Sending Notification..."); }
                        SendNotification(item);
                    }
                    else
                    {
                        // Check failed, re-queue request
                        item.failures++;
                        if (item.failures < Internal.maxRequestAttempts)
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Check Failed Attempt "+(item.failures)+". Re-eneueuing..."); }
                            backlog[queueName].Enqueue(item);
                        }
                        else
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Check Failed Final Attempt. Removing..."); }
                            try { AssetDataPlugin.ClearInfo(item.request.source, item.request.key); } catch { ; }
                        }
                    }
                }
                else
                {
                    // Dequeue failed
                    if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Backlog Dequeue Failed. Skipping..."); }
                }
            }

            /// <summary>
            /// Method for sending callback notifications
            /// </summary>
            /// <param name="item"></param>
            public static void SendNotification(BacklogItem item)
            {
                if (item.subscription.callback != null)
                {
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Regular Callback"); }
                    try
                    {
                        item.subscription.callback(item.request);
                    }
                    catch (Exception x)
                    {
                        Debug.Log("Asset Data Plugin: Exception Sending Regular Callback");
                        Debug.Log(Convert.ToString(x));
                        Debug.LogException(x);
                    }
                }
                else if (item.subscription.callbackType != null && item.subscription.callbackMethod != null)
                {
                    if (Internal.diagnostics >= DiagnosticSelection.high) { Debug.Log("Asset Data Plugin: Sending Reflection Callback"); }
                    try
                    {
                        Type t = Type.GetType(item.subscription.callbackType);
                        if (t != null)
                        {
                            MethodInfo m = t.GetMethod(item.subscription.callbackMethod);
                            if (m != null)
                            {
                                m.Invoke(null, new object[] { item.request.action.ToString(), item.request.source, item.request.key, item.request.previous, item.request.value });
                            }
                            else
                            {
                                Debug.LogWarning("Asset Data Plugin: Callback Method Is Not Found");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("Asset Data Plugin: Callback Type Is Not Found");
                        }
                    }
                    catch (Exception x)
                    {
                        Debug.Log("Asset Data Plugin: Exception Sending Reflection Callback");
                        Debug.LogException(x);
                    }
                }
            }

            public static class Checker
            {
                public static bool CheckSourceAsCreature(AssetDataPlugin.DatumChange datum)
                {
                    if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check"); }
                    // Test that source is a valid creature id
                    CreatureGuid cid = CreatureGuid.Empty;
                    if(CreatureGuid.TryParse(datum.source, out cid))
                    {
                        // Test that creature has a loaded base
                        GameObject assetBase = Utility.GetBaseLoader(cid);
                        if (assetBase == null)
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Failed (Base Not Loaded)"); }
                            return false; 
                        }
                        // Test that creature has a loaded body
                        GameObject assetMain = Utility.GetAssetLoader(cid);
                        if (assetMain == null) 
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Failed (Body Not Loaded)"); }
                            return false; 
                        }
                    }
                    else
                    {
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Failed (Non Valid Guid)"); }
                        return false;
                    }
                    if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Passed"); }
                    return true;
                }

                public static bool CheckSourceAndValueAsCreature(AssetDataPlugin.DatumChange datum)
                {
                    if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source And Value Creature Loaded Check"); }
                    // Test that source is a valid creature id
                    CreatureGuid cid = CreatureGuid.Empty;
                    if (CreatureGuid.TryParse(datum.source, out cid))
                    {
                        // Test that creature has a loaded base
                        GameObject assetBase = Utility.GetBaseLoader(cid);
                        if (assetBase == null) 
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Failed (Base Not Loaded)"); }
                            return false; 
                        }
                        // Test that creature has a loaded body
                        GameObject assetMain = Utility.GetAssetLoader(cid);
                        if (assetMain == null) 
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Failed (Body Not Loaded)"); }
                            return false; 
                        }
                    }
                    else
                    {
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source Creature Loaded Check: Failed (Non Valid Guid)"); }
                        return false;
                    }
                    // Test that value is a valid creature id
                    if (CreatureGuid.TryParse((datum.value+"@").Substring(0, (datum.value + "@").LastIndexOf("@")), out cid))
                    {
                        // Test that creature has a loaded base
                        GameObject assetBase = Utility.GetBaseLoader(cid);
                        if (assetBase == null) 
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Value Creature Loaded Check: Failed (Base Not Loaded)"); }
                            return false; 
                        }
                        // Test that creature has a loaded body
                        GameObject assetMain = Utility.GetAssetLoader(cid);
                        if (assetMain == null) 
                        {
                            if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Value Creature Loaded Check: Failed (Body Not Loaded)"); }
                            return false; 
                        }
                    }
                    else
                    {
                        if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Value Creature Loaded Check: Failed (Non Valid Guid)"); }
                        return false;
                    }
                    if (Internal.diagnostics >= DiagnosticSelection.debug) { Debug.Log("Asset Data Plugin: Source And Value Creature Loaded Check: Passed"); }
                    return true;
                }
            }
        }
    }
}
