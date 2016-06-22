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
        void StartCollectSignal(bool isreset);
        void StopCollectSignal();
        bool IsReady { get; }
        void GetSignal(out List<double>[] spike, out List<int>[] uid,
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

    /// <summary>
    /// this class does not to be overall thread safe, because that will need alot of work and 
    /// syncronize on class states. we only need to make sure that the signal caching thread is safe
    /// working with the other thread accessing through ISignal interface
    /// </summary>
    public class RippleSignal : ISignal, IDisposable
    {
        XippmexDotNet xippmexdotnet = new XippmexDotNet();
        readonly int digitalIPI, analogIPI;
        const int tickfreq = 30000;
        const int maxelec = 5120;
        object lockobj = new object();

        
        double starttime = -1;
        Dictionary<int, List<SIGNALTYPE>> elecsignal = new Dictionary<int, List<SIGNALTYPE>>();
        Dictionary<SignalChannel, List<IAnalyzer>> elecsigch = new Dictionary<SignalChannel, List<IAnalyzer>>();
        Thread thread;
        ManualResetEvent threadevent = new ManualResetEvent(true);
        
        // those fields should be accessed only through corresponding property to provide thread safety
        bool iscaching;
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

        public RippleSignal(int digitalIPI = 800, int analogIPI = 4700)
        {
            this.digitalIPI = digitalIPI;
            this.analogIPI = analogIPI;
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

        bool IsCaching
        {
            get
            {
                lock (lockobj)
                {
                    return iscaching;
                }
            }
            set
            {
                lock (lockobj)
                {
                    iscaching = value;
                }
            }
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

        public void StartCollectSignal(bool isreset)
        {
            if (IsReady)
            {
                if (isreset)
                {
                    Reset();
                }
                if (thread == null)
                {
                    thread = new Thread(ThreadCollectSignal);
                    thread.Start();
                }
                else
                {
                    threadevent.Set();
                }
            }
        }

        public void StopCollectSignal()
        {
            if (thread != null)
            {
                threadevent.Reset();
            }
        }

        #region ThreadCachingFunctions
        /// <summary>
        /// This is the thread caching function, make sure that this function doesn't
        /// conflict with the class calling thread.
        /// </summary>
        void ThreadCollectSignal()
        {
            // here we use local and readonly variables, threadsafe static Math methods. 
            string fcs = "", scs = "";
            int fi, si, fn;
            if (digitalIPI < analogIPI)
            {
                fn = (int)Mathf.Floor((float)analogIPI / digitalIPI);
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
                fn = (int)Mathf.Floor((float)digitalIPI / analogIPI);
                fcs = "analog";
                scs = "digital";
                fi = analogIPI;
                si = digitalIPI % analogIPI;
            }

            while (true)
            {
                // ManualResetEvent is threadsafe
                threadevent.WaitOne();
                for (var i = 0; i < fn; i++)
                {
                    Thread.Sleep(fi);
                    IsCaching = true;
                    switch (fcs)
                    {
                        case "digital":
                            CollectSpike();
                            CollectDigIn();
                            break;
                        case "analog":
                            CollectLFP();
                            break;
                        default:
                            CollectSignal();
                            break;
                    }
                    IsCaching = false;
                }

                if (!string.IsNullOrEmpty(scs))
                {
                    Thread.Sleep(si);
                    IsCaching = true;
                    switch (scs)
                    {
                        case "digital":
                            CollectSpike();
                            CollectDigIn();
                            break;
                        case "analog":
                            CollectLFP();
                            break;
                        default:
                            CollectSignal();
                            break;
                    }
                    IsCaching = false;
                }
            }
        }

        void CollectSignal()
        {
            CollectSpike();
            CollectDigIn();
            CollectLFP();
        }

        void CollectSpike()
        {
            var es = Electrodes;
            var s = xippmexdotnet.xippmex(4, "spike", new MWNumericArray(es.Length, 1, es, null, true, false));
            var s1 = (object[,])(s[1] as MWCellArray).ToArray();
            var s3 = (object[,])(s[3] as MWCellArray).ToArray();
            for (var i = 0; i < s1.Length; i++)
            {
                var st = (double[,])s1[i, 0];
                if (st.Length > 0)
                {
                    var u = (double[,])s3[i, 0];
                    for (var j = 0; j < st.Length; j++)
                    {
                        spike[i].Add(st[j,0]);
                        uid[i].Add((int)u[j,0]);
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
            if(!d1.IsEmpty)
            {
                var et = (double[])d1.ToVector(MWArrayComponent.Real);
                digintime.AddRange(et);
                var d2 = (d[2] as MWStructArray);
                var fn = d2.FieldNames;
                foreach (var f in fn)
                {
                    digin[f] = new List<int>((int[])(d2.GetField(f) as MWNumericArray).ToVector(MWArrayComponent.Real));
                }
            }
        }
        #endregion

        #region safe with caching thread only when it has already been stoped
        /// <summary>
        /// Stop Caching thread and then safely reset buffers
        /// </summary>
        void Reset()
        {
            threadevent.Reset();
            while (true)
            {
                if (!IsCaching)
                {
                    InitBuffer();
                    activebuffer = 1;
                    SwapBuffer();
                    threadevent.Set();
                    break;
                }
            }
        }

        void InitBuffer()
        {
            InitBuffer0();
            InitBuffer1();
        }

        void InitBuffer0()
        {
            spike0 = new List<double>[Electrodes.Length];
            uid0 = new List<int>[Electrodes.Length];
            for (var i = 0; i < Electrodes.Length; i++)
            {
                var s0 = new List<double>();
                var u0 = new List<int>();
                spike0[i] = s0;
                uid0[i] = u0;
            }
            lfp0 = new List<double[,]>();
            lfpstarttime0 = new List<double>();
            digintime0 = new List<double>();
            digin0 = new Dictionary<string, List<int>>();
        }

        void InitBuffer1()
        {
            spike1 = new List<double>[Electrodes.Length];
            uid1 = new List<int>[Electrodes.Length];
            for (var i = 0; i < Electrodes.Length; i++)
            {
                var s1 = new List<double>();
                var u1 = new List<int>();
                spike1[i] = s1;
                uid1[i] = u1;
            }
            lfp1 = new List<double[,]>();
            lfpstarttime1 = new List<double>();
            digintime1 = new List<double>();
            digin1 = new Dictionary<string, List<int>>();
        }

        void SwapBuffer()
        {
            if (activebuffer == 0)
            {
                InitBuffer1();
                spike = spike1;
                uid = uid1;
                lfp = lfp1;
                lfpstarttime = lfpstarttime1;
                digintime = digintime1;
                digin = digin1;
                activebuffer = 1;
            }
            else
            {
                InitBuffer0();
                spike = spike0;
                uid = uid0;
                lfp = lfp0;
                lfpstarttime = lfpstarttime0;
                digintime = digintime0;
                digin = digin0;
                activebuffer = 0;
            }
        }

        public void GetSignal(out List<double>[] aspike, out List<int>[] auid,
            out List<double[,]> alfp, out List<double> alfpstarttime,
            out List<double> adigintime, out Dictionary<string, List<int>> adigin)
        {
            threadevent.Reset();
            while (true)
            {
                if (!IsCaching)
                {
                    // make sure to get latest data, since caching may be done long before calling here.
                    CollectSignal();
                    GetActiveBuffer(out aspike, out auid, out alfp, out alfpstarttime, out adigintime, out adigin);
                    SwapBuffer();
                    threadevent.Set();
                    break;
                }
            }
        }

        void GetActiveBuffer(out List<double>[] aspike, out List<int>[] auid,
            out List<double[,]> alfp, out List<double> alfpstarttime,
            out List<double> adigintime, out Dictionary<string, List<int>> adigin)
        {
            if (activebuffer == 0)
            {
                aspike = spike0;
                auid = uid0;
                alfp = lfp0;
                alfpstarttime = lfpstarttime0;
                adigintime = digintime0;
                adigin = digin0;
            }
            else
            {
                aspike = spike1;
                auid = uid1;
                alfp = lfp1;
                alfpstarttime = lfpstarttime1;
                adigintime = digintime1;
                adigin = digin1;
            }
        }
        #endregion

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
                return alser;
            }
        }
    }
}