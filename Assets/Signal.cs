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
    public enum SIGNALSYSTEM
    {
        Ripple,
        Plexon,
        TDT
    }

    public enum SIGNALTYPE
    {
        Spike,
        LFP,
        Raw,
        Stim,
        HiRes,
        OneKSpS,
        ThirtyKSpS
    }

    public struct SignalChannel
    {
        int eid;
        public int ElectrodID { get { return eid; } }
        SIGNALTYPE st;
        public SIGNALTYPE SignalType { get { return st; } }

        public SignalChannel(int electrodid, SIGNALTYPE signaltype)
        {
            eid = electrodid;
            st = signaltype;
        }
    }

    public interface ISignal
    {
        bool IsSignalOnline { get; }
        SIGNALSYSTEM System { get; }
        int[] ElectrodeIDs { get; }
        SIGNALTYPE[] GetSignalType(int electrodid,bool checkon);
        bool IsSignalChannelOn(int electrodid, SIGNALTYPE signaltype);
        void AddAnalyzer(IAnalyzer analyzer);
        void RemoveAnalyzer(int analyzerid);
        void StartCollectData(bool iscleanstart);
        void StopCollectData(bool isonemorecollect);
        bool IsReady { get; }
        void GetData(out List<double>[] spike, out List<int>[] uid,
            out List<double[,]> lfp, out List<double> lfpstarttime,
            out List<double> digintime, out Dictionary<string, List<int>> digin);
        Dictionary<int, IAnalyzer> Analyzer { get; }
        void AnalyzerReset();
    }

    /// <summary>
    /// this class does not to be overall thread safe, because that will need alot of work and 
    /// syncronize on class states. we only need to make sure that the signal caching thread is safe
    /// working with the other thread accessing through ISignal interface
    /// </summary>
    public class RippleSignal : ISignal, IDisposable
    {
        XippmexDotNet xippmexdotnet = new XippmexDotNet();
        readonly int digitalIPI, analogIPI, tickfreq, maxelectrodid, timeunitpersec, sleepresolution;
        object lockobj = new object();
        object datalock = new object();
        object eventlock = new object();

        Thread datathread;
        ManualResetEvent datathreadevent = new ManualResetEvent(true);
        Dictionary<int, IAnalyzer> idanalyzer = new Dictionary<int, IAnalyzer>();

        // those fields should be accessed only through corresponding property to provide thread safety
        bool iscollectingdata;
        bool gotothreadevent;
        bool isonline;
        int[] eids;
        // those fields should be manimulated only by thread safe methods
        List<double>[] spike;
        List<int>[] uid;
        List<double[,]> lfp;
        List<double> lfpstarttime;
        List<double> digintime;
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
            Dispose(true);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                datathread.Abort();
                xippmexdotnet.xippmex("close");
            }
        }

        //public void SetSignal(int elecid, SIGNALTYPE signaltype, bool onoff)
        //{
        //    if (IsReady && ElectrodeIDs.Contains(elecid))
        //    {
        //        if (starttime < 0)
        //        {
        //            starttime = ((MWNumericArray)xippmexdotnet.xippmex(1, "time")[0]).ToScalarDouble() / tickfreq;
        //        }
        //        switch (signaltype)
        //        {
        //            case SIGNALTYPE.Spike:
        //                xippmexdotnet.xippmex("signal", elecid, "spk", new MWLogicalArray(onoff));
        //                SetElectrodeSignal(elecid, signaltype, onoff);
        //                break;
        //            case SIGNALTYPE.LFP:
        //                xippmexdotnet.xippmex("signal", elecid, "lfp", new MWLogicalArray(onoff));
        //                SetElectrodeSignal(elecid, signaltype, onoff);
        //                break;
        //            case SIGNALTYPE.Raw:
        //                break;
        //            default:
        //                xippmexdotnet.xippmex("signal", elecid, "spk", new MWLogicalArray(onoff));
        //                xippmexdotnet.xippmex("signal", elecid, "lfp", new MWLogicalArray(onoff));
        //                SetElectrodeSignal(elecid, SIGNALTYPE.Spike, onoff);
        //                SetElectrodeSignal(elecid, SIGNALTYPE.LFP, onoff);
        //                break;
        //        }
        //    }
        //}

        string SignalTypeToRipple(SIGNALTYPE signaltype)
        {
            switch (signaltype)
            {
                case SIGNALTYPE.OneKSpS:
                    return "1ksps";
                case SIGNALTYPE.ThirtyKSpS:
                    return "30ksps";
                case SIGNALTYPE.Spike:
                    return "spk";
                case SIGNALTYPE.LFP:
                    return "lfp";
                case SIGNALTYPE.Raw:
                    return "raw";
                case SIGNALTYPE.HiRes:
                    return "hi-res";
                default:
                    return "stim";
            }
        }

        SIGNALTYPE RippleToSignalType(string signaltype)
        {
            switch (signaltype)
            {
                case "1ksps":
                    signaltype = "OneKSpS";
                    break;
                case "30ksps":
                    signaltype = "ThirtyKSpS";
                    break;
                case "spk":
                    signaltype = "Spike";
                    break;
                case "lfp":
                    signaltype = "LFP";
                    break;
                case "raw":
                    signaltype = "Raw";
                    break;
                case "hi-res":
                    signaltype = "HiRes";
                    break;
                case "stim":
                    signaltype = "Stim";
                    break;
            }
            return (SIGNALTYPE)Enum.Parse(typeof(SIGNALTYPE), signaltype);
        }

        public SIGNALTYPE[] GetSignalType(int electrodid,bool checkon=true)
        {
            SIGNALTYPE[] v = null;
            if (IsReady && ElectrodeIDs.Contains(electrodid))
            {
                var ts = xippmexdotnet.xippmex(1, "signal", electrodid)[0] as MWCellArray;
                List<SIGNALTYPE> vv = new List<SIGNALTYPE>();
                for (var i = 0; i < ts.NumberOfElements; i++)
                {
                    vv.Add(RippleToSignalType(((MWCharArray)ts[new[] { 1, i + 1 }]).ToString()));
                }
                v = vv.ToArray();
            }
            if(v!=null&&checkon)
            {
                v = v.Where(i => IsSignalChannelOn(electrodid, i)).ToArray();
            }
            return v;
        }

        public bool IsSignalChannelOn(int electrodid, SIGNALTYPE signaltype)
        {
            if (IsReady && ElectrodeIDs.Contains(electrodid))
            {
                var t = ((MWNumericArray)xippmexdotnet.xippmex(1, "signal", electrodid, SignalTypeToRipple(signaltype))[0]).ToScalarInteger();
                return t == 1 ? true : false;
            }
            return false;
        }

        //void SetElectrodeSignal(int elecid, SIGNALTYPE signaltype, bool onoff)
        //{
        //    var st = elecsignal[elecid];
        //    if (st == null)
        //    {
        //        if (onoff)
        //        {
        //            st = new List<SIGNALTYPE>();
        //            st.Add(signaltype);
        //            AddAnalyzer(elecid, signaltype, AnalysisFactory.Get(signaltype));
        //        }
        //    }
        //    else
        //    {
        //        if (onoff)
        //        {
        //            if (!st.Contains(signaltype))
        //            {
        //                st.Add(signaltype);
        //                AddAnalyzer(elecid, signaltype, AnalysisFactory.Get(signaltype));
        //            }
        //        }
        //        else
        //        {
        //            if (st.Contains(signaltype))
        //            {
        //                st.Remove(signaltype);
        //                RemoveAnalyzer(elecid, signaltype, -1);
        //            }
        //        }
        //    }
        //}

        public void AddAnalyzer(IAnalyzer analyzer)
        {
            int aid;
            if (idanalyzer.Count == 0)
            {
                aid = 0;
            }
            else
            {
                aid = idanalyzer.Keys.Max() + 1;
            }
            analyzer.ID = aid;
            idanalyzer[aid] = analyzer;
        }

        public void RemoveAnalyzer(int aid)
        {
            if (idanalyzer.ContainsKey(aid))
            {
                idanalyzer.Remove(aid);
            }
        }

        #region Thread Safe
        public bool IsSignalOnline
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

        public int[] ElectrodeIDs
        {
            get
            {
                if (IsSignalOnline)
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
                return ElectrodeIDs != null;
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
        #endregion

        public void StartCollectData(bool iscleanstart = true)
        {
            lock (datalock)
            {
                if (IsReady)
                {
                    if (datathread == null)
                    {
                        datathread = new Thread(ThreadCollectData);
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
                            if (iscleanstart)
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

        public void StopCollectData(bool isonemorecollect = true)
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
                            if (isonemorecollect)
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

        #region ThreadFunction
        void ThreadCollectData()
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
                            CollectDigIn();
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
                            CollectDigIn();
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
            CollectDigIn();
            CollectSpike();
            CollectLFP();
        }

        void CollectSpike()
        {
            var es = ElectrodeIDs;
            // get all works only when electrode ids are in (1,n) row vector of doubles
            var s = xippmexdotnet.xippmex(4, "spike", new MWNumericArray(1, es.Length, es, null, true, false));
            var s1 = (s[1] as MWCellArray);
            var s3 = (s[3] as MWCellArray);
            for (var i = 0; i < es.Length; i++)
            {
                // MWCellArray indexing is 1-based
                var idx = new int[] { i + 1, 1 };
                var st = (s1[idx] as MWNumericArray);
                if (!st.IsEmpty)
                {
                    var ss = (double[])st.ToVector(MWArrayComponent.Real);
                    var us = (double[])(s3[idx] as MWNumericArray).ToVector(MWArrayComponent.Real);
                    for (var j = 0; j < ss.Length; j++)
                    {
                        spike[es[i] - 1].Add((ss[j] / tickfreq) * timeunitpersec);
                        uid[es[i] - 1].Add((int)us[j]);
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

        void CollectDigIn()
        {
            var d = xippmexdotnet.xippmex(3, "digin");
            var d1 = (d[1] as MWNumericArray);
            if (!d1.IsEmpty)
            {
                var et = (double[])d1.ToVector(MWArrayComponent.Real);
                for (var i = 0; i < et.Length; i++)
                {
                    digintime.Add((et[i] / tickfreq) * timeunitpersec);
                }
            }
        }
        #endregion

        public void GetData(out List<double>[] ospike, out List<int>[] ouid,
            out List<double[,]> olfp, out List<double> olfpstarttime,
            out List<double> odigintime, out Dictionary<string, List<int>> odigin)
        {
            lock (datalock)
            {
                if (IsCollectingData)
                {
                    StopCollectData();
                    GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out odigintime, out odigin);
                    StartCollectData(false);
                }
                else
                {
                    GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out odigintime, out odigin);
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
            adigintime = digintime;
            adigin = digin;
            InitDataBuffer();
        }

        void InitDataBuffer()
        {
            var en = ElectrodeIDs.Length;
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
            digintime = new List<double>();
            digin = new Dictionary<string, List<int>>();
        }

        public SIGNALSYSTEM System { get { return SIGNALSYSTEM.Ripple; } }

        public Dictionary<int, IAnalyzer> Analyzer
        {
            get
            {
                return idanalyzer;
            }
        }

        public void AnalyzerReset()
        {
            foreach (var a in idanalyzer.Values)
            {
                a.Reset();
            }
        }
    }
}