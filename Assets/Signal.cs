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
    public interface ISignal
    {
        bool IsSignalOnline { get; }
        SIGNALSYSTEM System { get; }
        int[] ElectrodeChannels { get; }
        int[] Electrodes { get; }
        void SetSignal(int elecid, SIGNALTYPE signaltype, bool onoff);
        SIGNALTYPE[] GetSignalType(int elecid);
        bool IsSignalTypeOn(int elecid, SIGNALTYPE signaltype);
        void AddAnalysis(int elecid, SIGNALTYPE signaltype, IAnalyzer analyzer);
        void RemoveAnalysis(int elecid, SIGNALTYPE signaltype, int chidx);
        double StartTime { get; }
        Dictionary<int, List<SIGNALTYPE>> ElectrodeSignal { get; }
        void StartCollectData(bool iscleanstart);
        void StopCollectData(bool isonemorecollect);
        bool IsReady { get; }
        void GetData(out List<double>[] spike, out List<int>[] uid,
            out List<double[,]> lfp, out List<double> lfpstarttime,
            out List<double> digintime, out Dictionary<string, List<int>> digin);
        List<IAnalyzer> Analyzers { get; }
    }

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
        ThirtyKSpS,
        All
    }

    public struct SignalChannel
    {
        public int elec;
        public SIGNALTYPE signaltype;
        public SignalChannel(int elec, SIGNALTYPE signaltype)
        {
            this.elec = elec;
            this.signaltype = signaltype;
        }
    }

    public struct DigitalEvent
    {
        public int channel;
        public int value;
        public double time;
        public DigitalEvent(int channel,int value,double time)
        {
            this.channel = channel;
            this.value = value;
            this.time = time;
        }

    }

    /// <summary>
    /// this class does not to be overall thread safe, because that will need alot of work and 
    /// syncronize on class states. we only need to make sure that the signal caching thread is safe
    /// working with the other thread accessing through ISignal interface
    /// </summary>
    public class RippleSignal : ISignal, IDisposable
    {
        XippmexDotNet xippmexdotnet = new XippmexDotNet();
        readonly int digitalIPI, analogIPI, tickfreq , maxelec , unitpersec,sleepresolution ;
        object lockobj = new object();
        object datalock = new object();
        object eventlock = new object();

        
        double starttime = -1;
        Dictionary<int, List<SIGNALTYPE>> elecsignal = new Dictionary<int, List<SIGNALTYPE>>();
        Dictionary<SignalChannel, List<IAnalyzer>> elecsigch = new Dictionary<SignalChannel, List<IAnalyzer>>();
        Thread datathread;
        ManualResetEvent datathreadevent = new ManualResetEvent(true);
        List<IAnalyzer> analyzers;
        
        // those fields should be accessed only through corresponding property to provide thread safety
        bool iscollectingdata;
        bool gotothreadevent;
        bool isonline;
        int[] elec;
        int[] elecchannel;
        // those fields should be manimulated only by thread safe methods
        int activebuffer = 1;
        List<double>[] spike, spike0, spike1;
        List<int>[] uid, uid0, uid1;
        List<double[,]> lfp, lfp0, lfp1;
        List<double> lfpstarttime, lfpstarttime0, lfpstarttime1;
        List<double> digintime, digintime0, digintime1;
        Dictionary<string, List<int>> digin, digin0, digin1;

        public RippleSignal(int tickfreq=30000,int maxelec=5120,int unitpersec=1000,
            int digitalIPI = 800, int analogIPI = 4700,int sleepresolution=2)
        {
            this.tickfreq = tickfreq;
            this.maxelec = maxelec;
            this.unitpersec = unitpersec;
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

        public SIGNALSYSTEM System { get { return SIGNALSYSTEM.Ripple; } }

        public void SetSignal(int elecid, SIGNALTYPE signaltype, bool onoff)
        {
            if (IsReady && Electrodes.Contains(elecid))
            {
                if (starttime < 0)
                {
                    starttime = ((MWNumericArray)xippmexdotnet.xippmex(1, "time")[0]).ToScalarDouble() / tickfreq;
                }
                switch (signaltype)
                {
                    case SIGNALTYPE.Spike:
                        xippmexdotnet.xippmex("signal", elecid, "spk", new MWLogicalArray(onoff));
                        SetElectrodeSignal(elecid, signaltype, onoff);
                        break;
                    case SIGNALTYPE.LFP:
                        xippmexdotnet.xippmex("signal", elecid, "lfp", new MWLogicalArray(onoff));
                        SetElectrodeSignal(elecid, signaltype, onoff);
                        break;
                    case SIGNALTYPE.Raw:
                        break;
                    default:
                        xippmexdotnet.xippmex("signal", elecid, "spk", new MWLogicalArray(onoff));
                        xippmexdotnet.xippmex("signal", elecid, "lfp", new MWLogicalArray(onoff));
                        SetElectrodeSignal(elecid, SIGNALTYPE.Spike, onoff);
                        SetElectrodeSignal(elecid, SIGNALTYPE.LFP, onoff);
                        break;
                }
            }
        }

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

        public SIGNALTYPE[] GetSignalType(int elecid)
        {
            SIGNALTYPE[] v = null;
            if (IsReady && ElectrodeChannels.Contains(elecid))
            {
                var t = (object[,])((MWCellArray)xippmexdotnet.xippmex(1, "signal", elecid)[0]).ToArray();
                List<SIGNALTYPE> vv = new List<SIGNALTYPE>();
                foreach (var s in t)
                {
                    var ss = (char[,])s;
                    List<char> str = new List<char>();
                    foreach (var c in ss)
                    {
                        str.Add(c);
                    }
                    vv.Add(RippleToSignalType(new string(str.ToArray())));
                }
                v = vv.ToArray();
            }
            return v;
        }

        public bool IsSignalTypeOn(int elecid, SIGNALTYPE signaltype)
        {
            if (IsReady && ElectrodeChannels.Contains(elecid))
            {
                var t = ((MWNumericArray)xippmexdotnet.xippmex(1, "signal", elecid, SignalTypeToRipple(signaltype))[0]).ToScalarInteger();
                return t == 1 ? true : false;
            }
            return false;
        }

        void SetElectrodeSignal(int elecid, SIGNALTYPE signaltype, bool onoff)
        {
            var st = elecsignal[elecid];
            if (st == null)
            {
                if (onoff)
                {
                    st = new List<SIGNALTYPE>();
                    st.Add(signaltype);
                    AddAnalysis(elecid, signaltype, AnalysisFactory.Get(signaltype));
                }
            }
            else
            {
                if (onoff)
                {
                    if (!st.Contains(signaltype))
                    {
                        st.Add(signaltype);
                        AddAnalysis(elecid, signaltype, AnalysisFactory.Get(signaltype));
                    }
                }
                else
                {
                    if (st.Contains(signaltype))
                    {
                        st.Remove(signaltype);
                        RemoveAnalysis(elecid, signaltype, -1);
                    }
                }
            }
        }

        /// <summary>
        /// not thread safe, should coordinate with analysisqueue processing thread
        /// </summary>
        /// <param name="elecid"></param>
        /// <param name="signaltype"></param>
        /// <param name="analyzer"></param>
        public void AddAnalysis(int elecid, SIGNALTYPE signaltype, IAnalyzer analyzer)
        {
            var k = new SignalChannel(elecid, signaltype);
            if (elecsigch.ContainsKey(k))
            {
                elecsigch[k].Add(analyzer);
            }
            else
            {
                var la = new List<IAnalyzer>();
                la.Add(analyzer);
                elecsigch[k] = la;
            }
        }

        /// <summary>
        /// not thread safe, should coordinate with analysisqueue processing thread
        /// </summary>
        /// <param name="elecid"></param>
        /// <param name="signaltype"></param>
        /// <param name="chidx"></param>
        public void RemoveAnalysis(int elecid, SIGNALTYPE signaltype, int chidx)
        {
            var k = new SignalChannel(elecid, signaltype);
            if (chidx < 0)
            {
                elecsigch.Remove(k);
            }
            else
            {
                if (elecsigch.ContainsKey(k) && chidx < elecsigch[k].Count)
                {
                    elecsigch[k].RemoveAt(chidx);
                }
            }
        }

        #region Thread Safe
        /// <summary>
        /// thread safe
        /// </summary>
        public bool IsReady
        {
            get
            {
                return Electrodes != null;
            }
        }

        bool IsCollectingData
        {
            get
            {
                bool t;
                lock (datalock)
                {
                    t= iscollectingdata;
                }
                return t;
            }
            set
            {
                lock (datalock)
                {
                    iscollectingdata = value;
                }
            }
        }

        bool GotoThreadEvent
        {
            get { bool t; lock (lockobj) { t = gotothreadevent; } return gotothreadevent; } 
            set { lock (lockobj) { gotothreadevent = value; } }
        }

        /// <summary>
        /// thread safe
        /// </summary>
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
                        catch(Exception ex)
                        {
                            Debug.Log(ex.Message);
                        }
                    }
                    return isonline;
                }
            }
        }

        /// <summary>
        /// thread safe
        /// </summary>
        public int[] ElectrodeChannels
        {
            get
            {
                int[] v = null;
                if (IsSignalOnline)
                {
                    lock (lockobj)
                    {
                        if (elecchannel == null)
                        {
                            var t = ((MWNumericArray)xippmexdotnet.xippmex(1, "elec", "all")[0]).ToVector(MWArrayComponent.Real);
                            elecchannel = ((double[])t).Select(i => (int)i).ToArray();
                            elec = elecchannel.Where(i => i <= maxelec).ToArray();
                        }
                        if (elecchannel != null)
                        {
                            v = elecchannel;
                        }
                    }
                }
                return v;
            }
        }

        /// <summary>
        /// thread safe
        /// </summary>
        public int[] Electrodes
        {
            get
            {
                int[] v = null;
                if (IsSignalOnline)
                {
                    lock (lockobj)
                    {
                        if (elec == null)
                        {
                            var t = ((MWNumericArray)xippmexdotnet.xippmex(1, "elec", "all")[0]).ToVector(MWArrayComponent.Real);
                            elecchannel = ((double[])t).Select(i => (int)i).ToArray();
                            elec = elecchannel.Where(i => i <= maxelec).ToArray();
                        }
                        if (elec != null)
                        {
                            v = elec;
                        }
                    }
                }
                return v;
            }
        }
        #endregion

        public void StartCollectData(bool iscleanstart)
        {
            lock (datalock)
            {
                if (datathread == null)
                {
                    if (IsReady)
                    {
                        datathread = new Thread(ThreadCollectData);
                        InitDataBuffer();
                        datathreadevent.Set();
                        IsCollectingData = true;
                        datathread.Start();
                        return;
                    }
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
                    else
                    {
                        //if (iscleanstart)
                        //{
                        //    StopCollectData(false);
                        //    InitDataBuffer();
                        //    IsCollectingData = true;
                        //    datathreadevent.Set();
                        //}
                    }
                }
            }
        }

        public void StopCollectData(bool isonemorecollect)
        {
            lock (datalock)
            {
                if (datathread != null&&IsCollectingData)
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

        #region ThreadFunctions
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
            var es = Electrodes;
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
                        spike[es[i]-1].Add((ss[j]/tickfreq)*unitpersec);
                        uid[es[i]-1].Add((int)us[j]);
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
                    digintime.Add((et[i]/tickfreq)*unitpersec);
                }
            }
        }
        #endregion

        public void GetData(out List<double>[] aspike, out List<int>[] auid,
            out List<double[,]> alfp, out List<double> alfpstarttime,
            out List<double> adigintime, out Dictionary<string, List<int>> adigin)
        {
            lock (datalock)
            {
                if (IsCollectingData)
                {
                    StopCollectData(true);
                    GetDataBuffer(out aspike, out auid, out alfp, out alfpstarttime, out adigintime, out adigin);
                    StartCollectData(false);
                }
                else
                {
                    GetDataBuffer(out aspike, out auid, out alfp, out alfpstarttime, out adigintime, out adigin);
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
            spike = new List<double>[Electrodes.Length];
            uid = new List<int>[Electrodes.Length];
            for (var i = 0; i < Electrodes.Length; i++)
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

        public double StartTime
        {
            get
            {
                return starttime;
            }
        }

        public Dictionary<int, List<SIGNALTYPE>> ElectrodeSignal
        {
            get
            {
                return elecsignal;
            }
        }

        public List<IAnalyzer> Analyzers
        {
            get
            {
                if (analyzers == null)
                {
                    List<IAnalyzer> alser = new List<IAnalyzer>();
                    foreach (var sc in elecsigch.Keys)
                    {
                        var ass = elecsigch[sc];
                        foreach (var a in ass)
                        {
                            a.SignalChannel = sc;
                        }
                        alser.AddRange(ass);
                    }
                    analyzers = alser;
                }
                    return analyzers;
            }
        }
    }
}