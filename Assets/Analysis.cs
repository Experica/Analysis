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
        int[] Electrodes { get; }
        SIGNALTYPE[] SignalType { get; }
        void SetSignal(int elecid, SIGNALTYPE signaltype, bool onoff);
        void AddAnalysis(int elecid, SIGNALTYPE signaltype, IAnalyzer analyzer);
        void RemoveAnalysis(int elecid, SIGNALTYPE signaltype, int chidx);
        double StartTime { get; }
        Dictionary<int, List<SIGNALTYPE>> ElectrodeSignal { get; }
        void StartCollectSignal();
        bool IsReady { get; }
        void Reset();
        void GetSignal(out List<double>[] spike, out List<int>[] uid, out List<double[,]> lfp, out List<double> lfpstarttime);
        List<IAnalyzer> Analyzers { get; }
    }

    public enum SIGNALTYPE
    {
        Spike,
        LFP,
        Raw,
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

    public class RippleSignal : ISignal, IDisposable
    {
        XippmexDotNet xippmexdotnet = new XippmexDotNet();
        readonly SIGNALTYPE[] signaltype = new SIGNALTYPE[] {  SIGNALTYPE.Spike, SIGNALTYPE.LFP,SIGNALTYPE.All };
        readonly int digitalIPI, analogIPI;
        const int tickfreq = 30000;
        object lockobj = new object();

        int[] elec;
        bool isonline;
        double starttime = -1;
        Dictionary<int, List<SIGNALTYPE>> elecsignal = new Dictionary<int, List<SIGNALTYPE>>();
        Dictionary<SignalChannel, List<IAnalyzer>> elecsigch = new Dictionary<SignalChannel, List<IAnalyzer>>();
        Thread thread;
        ManualResetEvent threadevent = new ManualResetEvent(true);
        int activebuffer = 1;
        bool iscaching;

        List<double>[] spike, spike0, spike1;
        List<int>[] uid, uid0, uid1;
        List<double[,]> lfp, lfp0, lfp1;
        List<double> lfpstarttime, lfpstarttime0, lfpstarttime1;

        public RippleSignal(int digitalIPI = 800, int analogIPI = 4700)
        {
            this.digitalIPI = digitalIPI;
            this.analogIPI = analogIPI;
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
            if (disposing)
            {
                xippmexdotnet.xippmex("close");
            }
        }

        public bool IsReady
        {
            get
            {
                return Electrodes != null;
            }
        }

        public void Reset()
        {
            InitBuffer();
            activebuffer = 1;
            SwapBuffer();
        }

        public void SetSignal(int elecid, SIGNALTYPE signaltype, bool onoff)
        {
            if (elec != null && elec.Contains(elecid))
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

        void SetElectrodeSignal(int elecid, SIGNALTYPE signaltype, bool onoff)
        {
            var st = elecsignal[elecid];
            if (st == null)
            {
                if (onoff)
                {
                    st = new List<SIGNALTYPE>();
                    st.Add(signaltype);
                    AddAnalysis(elecid, signaltype, GetDefaultAnalyzer(signaltype));
                }
            }
            else
            {
                if (onoff)
                {
                    if (!st.Contains(signaltype))
                    {
                        st.Add(signaltype);
                        AddAnalysis(elecid, signaltype, GetDefaultAnalyzer(signaltype));
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

        public void AddAnalysis(int elecid, SIGNALTYPE signaltype, IAnalyzer analyzer)
        {
            var k = new SignalChannel(elecid, signaltype);
            if(elecsigch.ContainsKey(k))
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

        public void RemoveAnalysis(int elecid, SIGNALTYPE signaltype,int chidx)
        {
            var k = new SignalChannel(elecid, signaltype);
            if (chidx < 0)
            {
                elecsigch.Remove(k);
            }
            else
            {
                if (elecsigch.ContainsKey(k)&&chidx< elecsigch[k].Count)
                {
                    elecsigch[k].RemoveAt(chidx);
                }
            }
        }

        IAnalyzer GetDefaultAnalyzer(SIGNALTYPE signaltype)
        {
            IAnalyzer a = null;
            switch (signaltype)
            {
                case SIGNALTYPE.Spike:
                    a = new mfrAnalyzer();
                    break;
                case SIGNALTYPE.LFP:
                    break;
            }
            return a;
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

        public void StartCollectSignal()
        {
            if (thread == null)
            {
                if (IsReady)
                {
                    Reset();
                    thread = new Thread(ThreadCollectSignal);
                    thread.Start();
                }
            }
        }

        void ThreadCollectSignal()
        {
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
                threadevent.WaitOne();
                for (var i = 0; i < fn; i++)
                {
                    Thread.Sleep(fi);
                    IsCaching = true;
                    switch (fcs)
                    {
                        case "digital":
                            CollectSpike();
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
            CollectLFP();
        }

        void CollectSpike()
        {
            var s = xippmexdotnet.xippmex(4, "spike", new MWNumericArray(elec));
            var st = (object[])(s[1] as MWCellArray).ToArray();
            var u = (object[])(s[3] as MWCellArray).ToArray();
            for (var i = 0; i < elec.Length; i++)
            {
                spike[i].AddRange((double[])st[i]);
                uid[i].AddRange((int[])u[i]);
            }
        }

        void CollectLFP()
        {
            var p = xippmexdotnet.xippmex(2, "cont", new MWNumericArray(elec), 5000, "lfp");
            var fp = (double[,])(p[0] as MWNumericArray).ToArray(MWArrayComponent.Real);
            lfpstarttime.Add((p[1] as MWNumericArray).ToScalarDouble() / tickfreq);
            lfp.Add(fp);
        }

        public int[] Electrodes
        {
            get
            {
                int[] v = null;
                if (IsSignalOnline)
                {
                    elec = (int[])((MWNumericArray)xippmexdotnet.xippmex(1, "elec", "all")[0]).ToVector(MWArrayComponent.Real);
                    if (elec != null)
                    {
                        v = elec;
                    }
                }
                return v;
            }
        }

        void InitBuffer()
        {
            InitBuffer0();
            InitBuffer1();
        }

        void InitBuffer0()
        {
            spike0 = new List<double>[elec.Length];
            uid0 = new List<int>[elec.Length];
            for (var i = 0; i < elec.Length; i++)
            {
                var s0 = new List<double>();
                var u0 = new List<int>();
                spike0[i] = s0;
                uid0[i] = u0;
            }
            lfp0 = new List<double[,]>();
            lfpstarttime0 = new List<double>();
        }

        void InitBuffer1()
        {
            spike1 = new List<double>[elec.Length];
            uid1 = new List<int>[elec.Length];
            for (var i = 0; i < elec.Length; i++)
            {
                var s1 = new List<double>();
                var u1 = new List<int>();
                spike1[i] = s1;
                uid1[i] = u1;
            }
            lfp1 = new List<double[,]>();
            lfpstarttime1 = new List<double>();
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
                activebuffer = 1;
            }
            else
            {
                InitBuffer0();
                spike = spike0;
                uid = uid0;
                lfp = lfp0;
                lfpstarttime = lfpstarttime0;
                activebuffer = 0;
            }
        }

        public void GetSignal(out List<double>[] aspike, out List<int>[] auid, out List<double[,]> alfp, out List<double> alfpstarttime)
        {
            threadevent.Reset();
            while (true)
            {
                if (!IsCaching)
                {
                    CollectSignal();
                    GetActiveBuffer(out aspike, out auid, out alfp, out alfpstarttime);
                    SwapBuffer();
                    threadevent.Set();
                    break;
                }
            }
        }

        void GetActiveBuffer(out List<double>[] aspike, out List<int>[] auid, out List<double[,]> alfp, out List<double> alfpstarttime)
        {
            if (activebuffer == 0)
            {
                aspike = spike0;
                auid = uid0;
                alfp = lfp0;
                alfpstarttime = lfpstarttime0;
            }
            else
            {
                aspike = spike1;
                auid = uid1;
                alfp = lfp1;
                alfpstarttime = lfpstarttime1;
            }
        }

        public bool IsSignalOnline
        {
            get
            {
                if (elec == null)
                {
                    isonline = ((MWNumericArray)xippmexdotnet.xippmex(1)[0]).ToScalarDouble() == 1 ? true : false;
                }
                return isonline;
            }
        }

        public SIGNALTYPE[] SignalType
        {
            get
            {
                return signaltype;
            }
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
                List<IAnalyzer> alser = new List<IAnalyzer>();
                foreach(var sc in elecsigch.Keys)
                {
                    var ass = elecsigch[sc];
                    foreach(var a in ass)
                    {
                        a.SignalChannel = sc;
                    }
                    alser.AddRange(ass);
                }
                return alser;
            }
        }
    }

    public interface IAnalysis
    {
        ISignal Signal { get; }
        ConcurrentDictionary<string, ConcurrentQueue<List<object>>> CondTest { get; set; }
        void Reset();
        int ClearDataPerAnalysis { get; set; }
        bool IsClearData { get; }
        DataSet DataSet { get; }
        void AddAnalysisQueue();
    }

    public class DataSet
    {
       public List<double>[] spike;
       public List<int>[] uid;
       public List<double[,]> lfp;
       public List<double> lfpstarttime;
       public List<int> CondIndex;
        public Experiment ex;

        public void Clear()
        {
            for(var i=0;i<spike.Length;i++)
            {
                spike[i].Clear();
                uid[i].Clear();
            }
            lfp.Clear();
            lfpstarttime.Clear();
            CondIndex.Clear();
        }

        public void Add(List<double>[] aspike,  List<int>[] auid,  List<double[,]> alfp,  List<double> alfpstarttime,List<int> acondindex)
        {
            if(spike==null)
            {
                spike = aspike;
                uid = auid;
            }
            else
            {
                for (var i = 0; i < spike.Length; i++)
                {
                    spike[i].AddRange(aspike[i]);
                    uid[i].AddRange(auid[i]);
                }
            }
            if(lfp==null)
            {
                lfp = alfp;
                lfpstarttime = alfpstarttime;
            }
            else
            {
                lfp.AddRange(alfp);
                lfpstarttime.AddRange(alfpstarttime);
            }
            if(CondIndex==null)
            {
                CondIndex = acondindex;
            }
            else
            {
                CondIndex.AddRange(acondindex);
            }
            
        }

        public bool IsData(int elec,SIGNALTYPE signaltype)
        {
            bool v = true;
            switch(signaltype)
            {
                case SIGNALTYPE.Spike:
                    if(spike==null)
                    {
                        v = false;
                        break;
                    }
                    else
                    {
                        var st = spike[elec];
                        if (st == null || st.Count == 0)
                        {
                            v = false;
                            break;
                        }
                    }
                    break;
                case SIGNALTYPE.LFP:
                    if(lfp==null)
                    {
                        v = false;
                        break;
                    }
                    else
                    {
                        bool vv = false;
                        foreach(var l in lfp)
                        {
                            if( l.GetLength(1) > 0)
                            {
                                vv = true;
                                break;
                            }
                        }
                        v = vv;
                    }
                    break;
                default:
                    break;
            }
            return v;
        }
    }

    public class AnalysisDotNet : IAnalysis, IDisposable
    {
        RippleSignal ripple = new RippleSignal();
        ConcurrentDictionary<string,ConcurrentQueue< List<object>>> condtest = new ConcurrentDictionary<string, ConcurrentQueue<List<object>>>();
        int cleardataperanalysis;
        ConcurrentQueue<int[]> analysisqueue = new ConcurrentQueue<int[]>();
        int analysisidx = 0;
        Thread thread;
        DataSet dataset = new DataSet();

        public AnalysisDotNet(int cleardataperanalysis = 1)
        {
            this.cleardataperanalysis = cleardataperanalysis;
        }

        public ISignal Signal
        {
            get { return ripple; }
        }

        public ConcurrentDictionary<string,ConcurrentQueue< List<object>>> CondTest
        {
            get { return condtest; }
            set { condtest = value; }
        }

        public int ClearDataPerAnalysis
        {
            get { return cleardataperanalysis; }
            set { cleardataperanalysis = value; }
        }

        public bool IsClearData
        {
            get
            {
                if (cleardataperanalysis > 0)
                {
                    return analysisidx % cleardataperanalysis == 0;
                }
                else
                {
                    return false;
                }
            }
        }

        public DataSet DataSet
        {
            get
            {
                return dataset;
            }
        }

        public void Reset()
        {
            condtest.Clear();
            dataset.Clear();
        }

        ~AnalysisDotNet()
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
            if (disposing)
            {
                ripple.Dispose();
            }
        }

        public void AddAnalysisQueue()
        {
            analysisidx++;
            analysisqueue.Enqueue(new int[] { IsClearData ? 1 : 0 });
            if (thread == null)
            {
                thread = new Thread(ProcessAnalysisQueue);
                thread.Start();
            }
        }

        void ProcessAnalysisQueue()
        {
            // the dataset for each session of analysis should not be modified by
            // any subsequent procedures, so it's ideal that it could be represented
            // as immutable. but the standard .net way in system.collections.immutable
            // only support up to .net 4.0 profile which current unity runtime(2.0/3.5)
            // is below of. so we couldn't enforce right now the immutability of our dataset.
            // Instead, we should be careful and ensure that any subsequent analysis DO NOT modify dataset,
            // so that parallel analysis is ensured thread safe. 
            List<double>[] spike;
            List<int>[] uid;
            List<double[,]> lfp;
            List<double> lfpstarttime;
            int[] aq;
            List<object> CondIndex;
            while (true)
            {
                if (analysisqueue.TryDequeue(out aq)&&condtest.ContainsKey("CondIndex")&&condtest["CondIndex"].TryDequeue(out CondIndex))
                {
                    var iscleardata = aq[0];
                    Signal.GetSignal(out spike, out uid, out lfp, out lfpstarttime);
                    DataSet.Add(spike, uid, lfp, lfpstarttime, CondIndex.Cast<int>().ToList());

                    Parallel.ForEach(Signal.Analyzers,(i)=>i.Analysis(DataSet));

                    if(iscleardata==1)
                    {
                        DataSet.Clear();
                    }
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
        }
    }
}