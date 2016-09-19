/*
Signal.cs is part of the VLAB project.
Copyright (c) 2016 Li Alex Zhang and Contributors

Permission is hereby granted, free of charge, to any person obtaining a 
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included 
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using VLab;
using System;
using System.Linq;
using Ripple;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using MathNet;

namespace VLabAnalysis
{
    public enum SignalSource
    {
        Ripple,
        Plexon,
        TDT
    }

    public enum SignalType
    {
        Raw,
        ThirtyKSpS,
        TwoKSpS,
        OneKSpS,
        LFP,
        Event,
        Spike,
    }

    public struct Signal
    {
        public int Channel { get; set; }
        public SignalType Type { get; set; }

        public Signal(int channel, SignalType type)
        {
            Channel = channel;
            Type = type;
        }
    }

    /// <summary>
    /// Implementation should be thread safe
    /// </summary>
    public interface ISignal : IDisposable
    {
        bool IsOnline { get; }
        SignalSource Source { get; }
        int[] Channels { get; }
        SignalType[] GetSignalTypes(int channel, bool ison);
        bool IsSignalOn(int channel, SignalType signaltype);
        void AddAnalyzer(IAnalyzer analyzer);
        void RemoveAnalyzer(int analyzerid);
        void StartCollectData(bool isclean);
        void StopCollectData(bool iscollectall);
        bool IsReady { get; }
        void GetData(out List<double>[] spike, out List<int>[] uid,
            out List<double[,]> lfp, out List<double> lfpstarttime,
            out List<double> eventtime, out Dictionary<string, List<int>> digin);
        ConcurrentDictionary<int, IAnalyzer> Analyzers { get; }
        void Reset();
    }

    public class RippleSignal : ISignal
    {
        bool disposed = false;
        XippmexDotNet xippmexdotnet = new XippmexDotNet();

        readonly int digitalIPI, analogIPI, tickfreq, maxelectrodid, timeunitpersec, sleepresolution;
        object lockobj = new object();
        object datalock = new object();
        object eventlock = new object();
        Thread datathread;
        ManualResetEvent datathreadevent = new ManualResetEvent(true);
        ConcurrentDictionary<int, IAnalyzer> idanalyzer = new ConcurrentDictionary<int, IAnalyzer>();

        // those fields should be accessed only through corresponding property to provide thread safety
        bool iscollectingdata;
        bool gotothreadevent;
        bool isonline;
        int[] eids;

        // Linear Data Buffers
        List<double>[] spike;
        List<int>[] uid;
        List<double[,]> lfp;
        List<double> lfpstarttime;
        List<double> digitalintime;
        Dictionary<string, List<int>> digin;

        public RippleSignal(int tickfreq = 30000, int maxelec = 5120, int timeunitpersec = 1000,
            int digitalIPI = 800, int analogIPI = 4800, int sleepresolution = 1)
        {
            this.tickfreq = tickfreq;
            this.maxelectrodid = maxelec;
            this.timeunitpersec = timeunitpersec;
            this.digitalIPI = digitalIPI;
            this.analogIPI = analogIPI;
            this.sleepresolution = Math.Max(1, sleepresolution);
        }

        ~RippleSignal()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
            }
            StopCollectData(false);
            xippmexdotnet.xippmex("close");
            xippmexdotnet.Dispose();
            foreach(var a in idanalyzer.Values)
            {
                a.Dispose();
            }
            disposed = true;
        }

        string SignalTypeToRipple(SignalType signaltype)
        {
            switch (signaltype)
            {
                case SignalType.Raw:
                    return "raw";
                case SignalType.ThirtyKSpS:
                    return "30ksps";
                case SignalType.TwoKSpS:
                    return "hi-res";
                case SignalType.OneKSpS:
                    return "1ksps";
                case SignalType.LFP:
                    return "lfp";
                case SignalType.Event:
                    return "stim";
                default:
                    return "spk";
            }
        }

        SignalType RippleToSignalType(string ripplesignaltype)
        {
            switch (ripplesignaltype)
            {
                case "raw":
                    ripplesignaltype = "Raw";
                    break;
                case "30ksps":
                    ripplesignaltype = "ThirtyKSpS";
                    break;
                case "hi-res":
                    ripplesignaltype = "TwoKSpS";
                    break;
                case "1ksps":
                    ripplesignaltype = "OneKSpS";
                    break;
                case "lfp":
                    ripplesignaltype = "LFP";
                    break;
                case "stim":
                    ripplesignaltype = "Event";
                    break;
                case "spk":
                    ripplesignaltype = "Spike";
                    break;
            }
            return (SignalType)Enum.Parse(typeof(SignalType), ripplesignaltype);
        }

        public SignalType[] GetSignalTypes(int channel, bool ison = true)
        {
            SignalType[] v = null;
            if (IsReady && Channels.Contains(channel))
            {
                var ts = xippmexdotnet.xippmex(1, "signal", channel)[0] as MWCellArray;
                List<SignalType> vv = new List<SignalType>();
                for (var i = 0; i < ts.NumberOfElements; i++)
                {
                    vv.Add(RippleToSignalType(((MWCharArray)ts[new[] { 1, i + 1 }]).ToString()));
                }
                v = vv.ToArray();
            }
            if (v != null && ison)
            {
                v = v.Where(i => IsSignalOn(channel, i)).ToArray();
            }
            return v;
        }

        public bool IsSignalOn(int channel, SignalType signaltype)
        {
            if (IsReady && Channels.Contains(channel))
            {
                var t = ((MWNumericArray)xippmexdotnet.xippmex(1, "signal", channel, SignalTypeToRipple(signaltype))[0]).ToScalarInteger();
                return t == 1 ? true : false;
            }
            return false;
        }

        public void AddAnalyzer(IAnalyzer analyzer)
        {
            int id;
            if (idanalyzer.Count == 0)
            {
                id = 0;
            }
            else
            {
                id = idanalyzer.Keys.Max() + 1;
            }
            analyzer.ID = id;
            idanalyzer[id] = analyzer;
        }

        public void RemoveAnalyzer(int analyzerid)
        {
            if (idanalyzer.ContainsKey(analyzerid))
            {
                IAnalyzer a;
                idanalyzer.TryRemove(analyzerid,out a);
            }
        }

        public bool IsOnline
        {
            get
            {
                lock (lockobj)
                {
                    if (!isonline)
                    {
                        try
                        {
                            isonline = ((MWLogicalArray)xippmexdotnet.xippmex(1)[0]).ToVector()[0];
                        }
                        catch (Exception ex)
                        {
                            Debug.Log(ex.Message);
                        }
                    }
                    return isonline;
                }
            }
        }

        public int[] Channels
        {
            get
            {
                if (IsOnline)
                {
                    lock (lockobj)
                    {
                        if (eids == null)
                        {
                            try
                            {
                                var t = (double[])((MWNumericArray)xippmexdotnet.xippmex(1, "elec", "all")[0]).ToVector(MWArrayComponent.Real);
                                eids = t.Select(i => (int)i).Where(i => i <= maxelectrodid).ToArray();
                            }
                            catch (Exception ex)
                            {
                                Debug.Log(ex.Message);
                            }
                        }
                    }
                }
                return eids;
            }
        }

        public bool IsReady
        {
            get
            {
                return Channels != null;
            }
        }

        bool IsCollectingData
        {
            get { lock (datalock) { return iscollectingdata; } }
            set { lock (datalock) { iscollectingdata = value; } }
        }

        bool GotoThreadEvent
        {
            get { lock (lockobj) { return gotothreadevent; } }
            set { lock (lockobj) { gotothreadevent = value; } }
        }

        public void StartCollectData(bool isclean = true)
        {
            lock (datalock)
            {
                if (IsReady)
                {
                    if (datathread == null)
                    {
                        datathread = new Thread(CollectDataThreadFunction);
                        InitDataBuffer();
                        datathreadevent.Set();
                        IsCollectingData = true;
                        datathread.Start();
                        return;
                    }
                    else
                    {
                        if (!IsCollectingData)
                        {
                            if (isclean)
                            {
                                InitDataBuffer();
                            }
                            IsCollectingData = true;
                            datathreadevent.Set();
                        }
                    }
                }
            }
        }

        public void StopCollectData(bool iscollectall = true)
        {
            lock (datalock)
            {
                if (datathread != null && IsCollectingData)
                {
                    lock (eventlock)
                    {
                        datathreadevent.Reset();
                        GotoThreadEvent = true;
                    }
                    while (true)
                    {
                        if (!GotoThreadEvent)
                        {
                            if (iscollectall)
                            {
                                // make sure to get all data before the time this function is issued.
                                CollectData();
                            }
                            IsCollectingData = false;
                            break;
                        }
                    }
                }
            }
        }

        #region Collect Data
        void CollectDataThreadFunction()
        {
            // here we use local and readonly variables, threadsafe methods. 
            string fcs = "", scs = "";
            int fi, si, fn;
            if (digitalIPI < analogIPI)
            {
                fn = (int)Mathf.Floor(analogIPI / digitalIPI);
                fcs = "digital";
                scs = "analog";
                fi = digitalIPI;
                si = analogIPI % digitalIPI;
            }
            else if (digitalIPI == analogIPI)
            {
                fn = 1;
                fcs = "all";
                scs = "";
                fi = digitalIPI;
                si = 0;
            }
            else
            {
                fn = (int)Mathf.Floor(digitalIPI / analogIPI);
                fcs = "analog";
                scs = "digital";
                fi = analogIPI;
                si = digitalIPI % analogIPI;
            }
            while (true)
            {
            ThreadEvent:
                lock (eventlock)
                {
                    GotoThreadEvent = false;
                    datathreadevent.WaitOne();
                }
                for (var i = 0; i < fn; i++)
                {
                    if (GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }
                    for (var j = 0; j < fi / sleepresolution; j++)
                    {
                        Thread.Sleep(sleepresolution);
                        if (GotoThreadEvent)
                        {
                            goto ThreadEvent;
                        }
                    }
                    switch (fcs)
                    {
                        case "digital":
                            CollectDigitalIn();
                            CollectSpike();
                            break;
                        case "analog":
                            CollectLFP();
                            break;
                        default:
                            CollectData();
                            break;
                    }
                }
                if (!string.IsNullOrEmpty(scs))
                {
                    if (GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }
                    for (var j = 0; j < si / sleepresolution; j++)
                    {
                        Thread.Sleep(sleepresolution);
                        if (GotoThreadEvent)
                        {
                            goto ThreadEvent;
                        }
                    }
                    switch (scs)
                    {
                        case "digital":
                            CollectDigitalIn();
                            CollectSpike();
                            break;
                        case "analog":
                            CollectLFP();
                            break;
                        default:
                            CollectData();
                            break;
                    }
                }
            }
        }

        void CollectData()
        {
            CollectDigitalIn();
            CollectSpike();
            CollectLFP();
        }

        void CollectDigitalIn()
        {
            var d = xippmexdotnet.xippmex(3, "digin");
            var d1 = (d[1] as MWNumericArray);
            if (!d1.IsEmpty)
            {
                var et = (double[])d1.ToVector(MWArrayComponent.Real);
                for (var i = 0; i < et.Length; i++)
                {
                    digitalintime.Add((et[i] / tickfreq) * timeunitpersec);
                }
            }
        }

        void CollectSpike()
        {
            var sid = Channels;
            // xippmex works only when electrode ids are in (1,n) row vector of doubles
            var s = xippmexdotnet.xippmex(4, "spike", new MWNumericArray(1, sid.Length, sid, null, true, false));
            var s1 = s[1] as MWCellArray;
            var s3 = s[3] as MWCellArray;
            for (var i = 0; i < sid.Length; i++)
            {
                // MWCellArray indexing is 1-based
                var idx = new int[] { i + 1, 1 };
                var st = s1[idx] as MWNumericArray;
                if (!st.IsEmpty)
                {
                    var ss = (double[])st.ToVector(MWArrayComponent.Real);
                    var us = (double[])(s3[idx] as MWNumericArray).ToVector(MWArrayComponent.Real);
                    for (var j = 0; j < ss.Length; j++)
                    {
                        var sidx = sid[i] - 1;
                        spike[sidx].Add((ss[j] / tickfreq) * timeunitpersec);
                        uid[sidx].Add((int)us[j]);
                    }
                }
            }
        }

        void CollectLFP()
        {
            //var es = Electrodes;
            //MWArray[] p = new MWArray[2];
            //try
            //{
            //    p = xippmexdotnet.xippmex(1, "cont", new MWNumericArray(1,es.Length, es, null, true, false), 5000, "lfp");
            //}
            //catch (Exception ex)
            //{
            //    Debug.Log(ex.Message);
            //}
            //var fp = (p[0] as MWNumericArray).ToArray(MWArrayComponent.Real);
            //lfpstarttime.Add((p[1] as MWNumericArray).ToScalarDouble() / tickfreq);
            //lfp.Add(fp);
        }
        #endregion

        public void GetData(out List<double>[] ospike, out List<int>[] ouid,
            out List<double[,]> olfp, out List<double> olfpstarttime,
            out List<double> eventtime, out Dictionary<string, List<int>> odigin)
        {
            lock (datalock)
            {
                if (IsCollectingData)
                {
                    StopCollectData();
                    GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out eventtime, out odigin);
                    StartCollectData(false);
                }
                else
                {
                    GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out eventtime, out odigin);
                }
            }
        }

        void GetDataBuffer(out List<double>[] aspike, out List<int>[] auid,
            out List<double[,]> alfp, out List<double> alfpstarttime,
            out List<double> adigintime, out Dictionary<string, List<int>> adigin)
        {
            aspike = spike;
            auid = uid;
            alfp = lfp;
            alfpstarttime = lfpstarttime;
            adigintime = digitalintime;
            adigin = digin;
            InitDataBuffer();
        }

        void InitDataBuffer()
        {
            var en = Channels.Length;
            spike = new List<double>[en];
            uid = new List<int>[en];
            for (var i = 0; i < en; i++)
            {
                var s = new List<double>();
                var u = new List<int>();
                spike[i] = s;
                uid[i] = u;
            }
            lfp = new List<double[,]>();
            lfpstarttime = new List<double>();
            digitalintime = new List<double>();
            digin = new Dictionary<string, List<int>>();
        }

        public SignalSource Source { get { return SignalSource.Ripple; } }

        public ConcurrentDictionary<int, IAnalyzer> Analyzers
        {
            get
            {
                return idanalyzer;
            }
        }

        public void Reset()
        {
            foreach (var a in idanalyzer.Values)
            {
                a.Reset();
            }
        }
    }
}