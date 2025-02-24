﻿using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Costura;
using System.Drawing;
using System.Threading;

namespace TriggernometryProxy
{

    public class ProxyPlugin : IActPluginV1
    {

        private Triggernometry.RealPlugin Instance;

        private ActPluginData ActPluginPrevious = null;

        private object CornerLock = new object();
        private bool CornerPopupVisible = false;
        private Control CornerPopup = null;
        private bool complained = false;
        private int callbackIdCounter = 0;
        private List<Tuple<int, string, CustomCallbackDelegate, object>> queuedRegs = new List<Tuple<int, string, CustomCallbackDelegate, object>>();

        public delegate void CustomCallbackDelegate(object o, string param);
        
        public ProxyPlugin()
        {
            CosturaUtility.Initialize();
        }

        public int RegisterNamedCallback(string name, CustomCallbackDelegate callback, object o)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }
            int newid = Interlocked.Increment(ref callbackIdCounter);
            lock (this)
            {
                if (Instance != null)
                {
                    Instance.RegisterNamedCallback(newid, name, callback, o);
                }
                else
                {
                    queuedRegs.Add(new Tuple<int, string, CustomCallbackDelegate, object>(newid, name, callback, o));
                }
            }
            return newid;
        }

        public void UnregisterNamedCallback(int id)
        {
            lock (this)
            {
                if (Instance != null)
                {
                    Instance.UnregisterNamedCallback(id);
                }
                else
                {
                    var ex = (from ix in queuedRegs where ix.Item1 == id select ix).ToList();
                    foreach (var x in ex)
                    {
                        queuedRegs.Remove(x);
                    }
                }
            }
        }

        public void FailsafeRegisterHook(string hookname, string methodname)
        {
            // this is to prevent errors when users don't shut down ACT in between updates, and the old realplugin is still loaded in
            // (and might not expose the hooks that are expected by a newer version of the proxy)
            try
            {
                MethodInfo mi = GetType().GetMethod(methodname);
                PropertyInfo pi = Instance.GetType().GetProperty(hookname);
                Delegate dob = Delegate.CreateDelegate(pi.PropertyType, this, mi);
                pi.SetValue(Instance, dob);
                return;
            }
            catch (Exception)
            {
            }
            ComplainAboutReload();
        }

        private void ComplainAboutReload()
        {
            if (complained == true)
            {
                return;
            }
            complained = true;
            Instance.IfYouSeeThisErrorYouNeedToRestartACT();
        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            lock (this)
            {
                Instance = new Triggernometry.RealPlugin();
                foreach (Tuple<int, string, CustomCallbackDelegate, object> t in queuedRegs)
                {
                    Instance.RegisterNamedCallback(t.Item1, t.Item2, t.Item3, t.Item4);
                }
                queuedRegs.Clear();
            }
            Instance.mainform = ActGlobals.oFormActMain;
            Version iv = typeof(Triggernometry.RealPlugin).Assembly.GetName().Version;
            Version ip = typeof(ProxyPlugin).Assembly.GetName().Version;
            if (iv.CompareTo(ip) != 0)
            {
                ComplainAboutReload();
            }
            FailsafeRegisterHook("InCombatHook", "InCombat");
            FailsafeRegisterHook("EndCombatHook", "EndCombat");
            FailsafeRegisterHook("CurrentZoneHook", "GetCurrentZone");
            FailsafeRegisterHook("ActiveEncounterHook", "ExportActiveEncounter");
            FailsafeRegisterHook("LastEncounterHook", "ExportLastEncounter");
            FailsafeRegisterHook("EncounterDurationHook", "GetEncounterDuration");
            FailsafeRegisterHook("TtsPlaybackHook", "InvokeTtsMethod");
            FailsafeRegisterHook("SoundPlaybackHook", "InvokeSoundMethod");
            FailsafeRegisterHook("CustomTriggerCheckHook", "HasCustomTriggers");
            FailsafeRegisterHook("CustomTriggerHook", "GetCustomTriggers");
            FailsafeRegisterHook("CornerShowHook", "ShowCornerNotification");
            FailsafeRegisterHook("CornerHideHook", "HideCornerNotification");
            FailsafeRegisterHook("TabLocateHook", "LocateTab");
            FailsafeRegisterHook("InstanceHook", "GetInstance");
            GetPluginNameAndPath();
            ActGlobals.oFormActMain.OnLogLineRead += OFormActMain_OnLogLineRead;
            Instance.InitPlugin(pluginScreenSpace, pluginStatusText);
        }

        public void LocateTab(TabPage tp)
        {
            try
            {
                FieldInfo fi = ActGlobals.oFormActMain.GetType().GetField("tc1", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null)
                {
                    return;
                }
                TabControl tc1 = (TabControl)fi.GetValue(ActGlobals.oFormActMain);
                foreach (TabPage tp1 in tc1.TabPages)
                {
                    if (tp1.Text == "Plugins")
                    {
                        foreach (Control c in tp1.Controls)
                        {
                            if (c.Name == "tcPlugins")
                            {
                                TabControl tc2 = (TabControl)c;
                                foreach (TabPage tp2 in tc2.TabPages)
                                {
                                    if (tp2 == tp)
                                    {
                                        tc2.SelectedTab = tp;
                                    }
                                }
                            }
                        }
                        tc1.SelectedTab = tp1;
                        return;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public void ShowCornerNotification()
        {
            if (ActGlobals.oFormActMain.InvokeRequired == true)
            {
                ActGlobals.oFormActMain.Invoke((MethodInvoker)delegate { ShowCornerNotification(); });
                return;
            }
            lock (CornerLock)
            {
                if (CornerPopupVisible == true)
                {
                    return;
                }         
                MethodInfo mi = ActGlobals.oFormActMain.GetType().GetMethod("CornerControlAdd");
                if (mi != null)
                {
                    CornerPopup = Instance.GetCornerControl();
                    mi.Invoke(ActGlobals.oFormActMain, new object[] { CornerPopup });
                    CornerPopupVisible = true;
                }
            }
        }

        public void HideCornerNotification()
        {
            if (ActGlobals.oFormActMain.InvokeRequired == true)
            {
                ActGlobals.oFormActMain.Invoke((MethodInvoker)delegate { HideCornerNotification(); });
                return;
            }
            lock (CornerLock)
            {
                if (CornerPopupVisible == false)
                {
                    return;
                }
                MethodInfo mi = ActGlobals.oFormActMain.GetType().GetMethod("CornerControlRemove");
                if (mi != null)
                {
                    mi.Invoke(ActGlobals.oFormActMain, new object[] { CornerPopup });
                    CornerPopup = null;
                    CornerPopupVisible = false;
                }
            }
        }

        public void DeInitPlugin()
        {            
            ActGlobals.oFormActMain.OnLogLineRead -= OFormActMain_OnLogLineRead;
            Instance.DeInitPlugin();
            HideCornerNotification();
        }

        private void OFormActMain_OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            Instance.OnLogLineRead(isImport, logInfo.logLine, logInfo.detectedZone);
        }

        public void GetPluginNameAndPath()
        {
            Instance.path = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config");
            Instance.pluginPath = Instance.path;
            string name = null;            
            foreach (ActPluginData p in ActGlobals.oFormActMain.ActPlugins)
            {
                if (p.pluginObj == this)
                {
                    name = p.pluginFile.Name;
                    Instance.pluginPath = p.pluginFile.Directory.FullName;
                    break;
                }
            }
            if (name == null || name.Trim().Length == 0)
            {
                name = "Triggernometry";
            }
            Instance.pluginName = name;
        }

        public bool InCombat()
        {
            return ActGlobals.oFormActMain.InCombat;
        }

        public void EndCombat()
        {
            ActGlobals.oFormActMain.EndCombat(false);
        }

        public string GetCurrentZone()
        {
            return ActGlobals.oFormActMain.CurrentZone;
        }

        public string ExportLastEncounter()
        {
            Advanced_Combat_Tracker.FormActMain act = Advanced_Combat_Tracker.ActGlobals.oFormActMain;
            FieldInfo fi = act.GetType().GetField("defaultTextFormat", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic defaultTextFormat = fi.GetValue(act);
            fi = act.GetType().GetField("textExportFormats", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic textExportFormats = fi.GetValue(act);
            fi = act.GetType().GetField("opMiniParse", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic opMiniParse = fi.GetValue(act);
            fi = opMiniParse.GetType().GetField("ddlMiniFormat", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic ddlMiniFormat = fi.GetValue(opMiniParse);
            if (ddlMiniFormat.SelectedIndex != -1)
            {
                defaultTextFormat = textExportFormats[ddlMiniFormat.SelectedIndex];
            }
            if (defaultTextFormat != null)
            {
                int zones = act.ZoneList.Count;
                for (int ii = zones - 1; ii >= 0; ii--)
                {
                    int encs = act.ZoneList[ii].Items.Count;
                    for (int jj = encs - 1; jj >= 1; jj--)
                    {
                        if (act.ZoneList[ii].Items[jj] != act.ActiveZone.ActiveEncounter)
                        {
                            return act.GetTextExport(act.ZoneList[ii].Items[jj], defaultTextFormat);
                        }
                    }
                }
            }
            return "";
        }

        public string ExportActiveEncounter()
        {

            Advanced_Combat_Tracker.FormActMain act = Advanced_Combat_Tracker.ActGlobals.oFormActMain;
            FieldInfo fi = act.GetType().GetField("defaultTextFormat", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic defaultTextFormat = fi.GetValue(act);
            fi = act.GetType().GetField("textExportFormats", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic textExportFormats = fi.GetValue(act);
            fi = act.GetType().GetField("opMiniParse", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic opMiniParse = fi.GetValue(act);
            fi = opMiniParse.GetType().GetField("ddlMiniFormat", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
            dynamic ddlMiniFormat = fi.GetValue(opMiniParse);
            if (ddlMiniFormat.SelectedIndex != -1)
            {
                defaultTextFormat = textExportFormats[ddlMiniFormat.SelectedIndex];
            }

            return act.GetTextExport(act.ActiveZone.ActiveEncounter, defaultTextFormat);
            
        }

        public double GetEncounterDuration()
        {
            return ActGlobals.oFormActMain.ActiveZone.ActiveEncounter.Duration.TotalSeconds;
        }

        public void InvokeTtsMethod(string tts)
        {
            if (ActGlobals.oFormActMain.PlayTtsMethod != null)
            {
                ActGlobals.oFormActMain.PlayTtsMethod(tts);
            }
        }

        public void InvokeSoundMethod(string filename, int volume)
        {
            if (ActGlobals.oFormActMain.PlaySoundMethod != null)
            {
                ActGlobals.oFormActMain.PlaySoundMethod(filename, volume);
            }
        }

        public bool HasCustomTriggers()
        {
            return (ActGlobals.oFormActMain.CustomTriggers.Count > 0);
        }

        public List<Triggernometry.RealPlugin.CustomTriggerCategoryProxy> GetCustomTriggers()
        {
            List<Triggernometry.RealPlugin.CustomTriggerCategoryProxy> alltrigs = new List<Triggernometry.RealPlugin.CustomTriggerCategoryProxy>(); ;
            var trigs = from ix in ActGlobals.oFormActMain.CustomTriggers
                        group ix by new { ix.Value.Category, ix.Value.RestrictToCategoryZone } into ixs
                        select new { Key = ixs.Key, Items = ixs.ToList() };
            foreach (var trig in trigs)
            {
                Triggernometry.RealPlugin.CustomTriggerCategoryProxy ctp = new Triggernometry.RealPlugin.CustomTriggerCategoryProxy();
                ctp.Category = trig.Key.Category;
                ctp.RestrictToCategoryZone = trig.Key.RestrictToCategoryZone;
                foreach (var tx in trig.Items)
                {
                    Triggernometry.RealPlugin.CustomTriggerProxy ct = new Triggernometry.RealPlugin.CustomTriggerProxy();
                    ct.Active = tx.Value.Active;
                    ct.ShortRegexString = tx.Value.ShortRegexString;
                    ct.SoundData = tx.Value.SoundData;
                    ct.SoundType = tx.Value.SoundType;
                    ct.TimerName = tx.Value.TimerName;
                    ct.Tabbed = tx.Value.Tabbed;
                    ct.Timer = tx.Value.Timer;
                    ctp.Items.Add(ct);
                }
                alltrigs.Add(ctp);
            }
            return alltrigs;
        }

        public Triggernometry.RealPlugin.PluginWrapper GetInstance(string ActPluginName, string ActPluginType)
        {
            foreach (ActPluginData p in ActGlobals.oFormActMain.ActPlugins)
            {
                string tn = p.pluginObj != null ? p.pluginObj.GetType().Name : "(null)";
                if (
                    (
                        (String.Compare(p.pluginFile.Name, ActPluginName, true) == 0)
                        ||
                        (String.Compare(tn, ActPluginType, true) == 0)
                    )
                    &&
                    (String.Compare(p.lblPluginStatus.Text, "FFXIV Plugin Started.", true) == 0)
                )
                {
                    if (ActPluginPrevious == p)
                    {
                        return new Triggernometry.RealPlugin.PluginWrapper() { pluginObj = p.pluginObj, state = 1 };
                    }
                    else
                    {
                        ActPluginPrevious = p;
                        System.Diagnostics.FileVersionInfo vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(p.pluginFile.FullName);
                        int[] expectedActVer = new int[4] { 2, 0, 4, 6 };
                        string expectedActVers = "2.0.4.6";
                        int[] currentActVer = new int[4] { vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart, vi.FilePrivatePart };                        
                        for (int i = 0; i < 4; i++)
                        {
                            if (currentActVer[i] > expectedActVer[i])
                            {
                                break;
                            }
                            if (currentActVer[i] < expectedActVer[i])
                            {                                
                                return new Triggernometry.RealPlugin.PluginWrapper() { pluginObj = p.pluginObj, state = 2, fileversion = vi.FileVersion.ToString(), expectedversion = expectedActVers };
                            }
                        }
                        return new Triggernometry.RealPlugin.PluginWrapper() { pluginObj = p.pluginObj, state = 1 };
                    }
                }
            }
            return new Triggernometry.RealPlugin.PluginWrapper() { pluginObj = null, state = 0 };
        }

    }

}
