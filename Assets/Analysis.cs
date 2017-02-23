/*
Analysis.cs is part of the VLAB project.
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
using System.IO;
using Ripple;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;

namespace VLabAnalysis
{
    public enum AnalysisSystem
    {
        DotNet
    }

    public enum AnalysisInterface
    {
        IAnalyzer,
        IVisualizer,
        IController
    }

    public static class AnalysisFactory
    {
        public static IAnalysis GetAnalysisSystem(this AnalysisSystem analysissystem, int cleardataperanalysis = 1,
            int retainanalysisperclear = 1, int sleepresolution = 2)
        {
            switch (analysissystem)
            {
                default:
                    return new DotNetAnalysis(cleardataperanalysis, retainanalysisperclear, sleepresolution);
            }
        }

        public static Type[] FindAll(this AnalysisInterface i)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var ts = assemblies.Where(a => a.GetName().Name == "Assembly-CSharp").SelectMany(a => a.GetTypes())
                .Where(t => t.Namespace == "VLabAnalysis" && t.IsClass && t.GetInterface(i.ToString()) != null).ToArray();
            return ts;
        }

        public static IAnalyzer GetAnalyzer(this int electrodeid, SignalType signaltype)
        {
            switch (signaltype)
            {
                case SignalType.Spike:
                    return new MFRAnalyzer(new Signal(electrodeid, signaltype));
                default:
                    return null;
            }
        }

        public static IAnalyzer GetAnalyzer(this Type analyzertype, int electrodeid, SignalType signaltype)
        {
            if (typeof(IAnalyzer).IsAssignableFrom(analyzertype))
            {
                return (IAnalyzer)Activator.CreateInstance(analyzertype, new Signal(electrodeid, signaltype));
            }
            return null;
        }
    }

    /// <summary>
    /// The dataset for each session of analysis should not be modified by
    /// any subsequent procedures, so it's ideal that it could be represented
    /// as immutable. but the standard .net way in system.collections.immutable
    /// only support up to .net 4.0 profile which current unity runtime(2.0/3.5)
    /// is below of. so we couldn't enforce right now the immutability of our dataset.
    /// Moreover, we may want to accumulate dataset for several sessions of analysis which
    /// is more efficent represented as mutable, since it avoid copying, save time and memeroy.
    /// Therefore, we should be careful and ensure that any subsequent analysis DO NOT modify dataset,
    /// so that parallel analysis is ensured thread safe. Even if we do, use the class methods and 
    /// properties instead of direct accessing.
    /// </summary>
    public class DataSet
    {
        public List<double>[] spike;
        public List<int>[] uid;
        public List<double[,]> lfp;
        public List<double> lfpstarttime;
        public List<double>[] dintime;
        public List<bool>[] dinvalue;

        Experiment ex;
        public List<int> AccumCondIndex, CondIndex, AccumCondRepeat, CondRepeat;
        public List<List<Dictionary<string, double>>> AccumCondState, CondState;
        double vlabt0 = 0; double latency = -1;
        bool isdinmark = false;
        bool isdinmarkerror = false;

        readonly int sc, mc, btc, etc, nmpc, msr, oll;
        object datalock = new object();

        public DataSet(int statechannel = 1, int markchannel = 2, int begintriggerchannel = 3, int endtriggerchannel = 4,
            int nmarkpercond = 2, int marksearchradius = 20, int onlinelatency = 20)
        {
            sc = statechannel;
            mc = markchannel;
            btc = begintriggerchannel;
            etc = endtriggerchannel;
            nmpc = nmarkpercond;
            msr = marksearchradius;
            oll = onlinelatency;
        }


        public Experiment Ex
        {
            get { lock (datalock) { return ex; } }
            set { lock (datalock) { ex = value; } }
        }

        public double VLabTimeZero
        { get { lock (datalock) { return vlabt0; } } }

        public double Latency
        {
            get
            {
                lock (datalock)
                {
                    if (latency < 0 && ex != null)
                    {
                        latency = ex.Latency + msr + oll;
                    }
                    return latency;
                }
            }
        }

        public bool IsDInMark
        { get { lock (datalock) { return isdinmark; } } }

        public bool IsDInMarkError
        { get { lock (datalock) { return isdinmarkerror; } } }

        public double VLabTimeToDataTime(double vlabtime)
        {
            lock (datalock)
            {
                if (ex != null)
                {
                    return vlabtime * (1 + ex.TimerDriftSpeed) + vlabt0;
                }
                return vlabtime;
            }
        }

        public bool TryGetMarkTime(int condtestidx, out double t1, out double t2)
        {
            var msi = condtestidx * nmpc;
            if (dinvalue[mc].Count >= msi + nmpc)
            {
                t1 = dintime[mc][msi];
                t2 = dintime[mc][msi + 1];
                return true;
            }
            else
            {
                t1 = 0; t2 = 0;
                return false;
            }
        }

        public bool TrySearchMarkTime(double t1searchpoint, double t2searchpoint, out double t1, out double t2)
        {
            double d1, d2; List<int> sidx1 = new List<int>(); List<int> sidx2 = new List<int>();
            for (var i = dinvalue[mc].Count - 1; i >= 0; i--)
            {
                d1 = dintime[mc][i] - t1searchpoint;
                d2 = dintime[mc][i] - t2searchpoint;
                if (Math.Abs(d1) <= msr && dinvalue[mc][i] == true)
                {
                    sidx1.Add(i);
                }
                if (Math.Abs(d2) <= msr && dinvalue[mc][i] == false)
                {
                    sidx2.Add(i);
                }
                if (d1 < -msr && d2 < -msr)
                {
                    break;
                }
            }
            if (sidx1.Count == 1 && sidx2.Count == 1)
            {
                t1 = dintime[mc][sidx1[0]];
                t2 = dintime[mc][sidx2[0]];
                return true;
            }
            else
            {
                t1 = 0; t2 = 0; return false;
            }
        }

        public void Reset()
        {
            lock (datalock)
            {
                spike = null;
                uid = null;
                lfp = null;
                lfpstarttime = null;
                dintime = null;
                dinvalue = null;

                ex = null;
                CondIndex = null;
                AccumCondIndex = null;
                CondState = null;
                AccumCondState = null;
                CondRepeat = null;
                AccumCondRepeat = null;

                vlabt0 = 0;
                latency = -1;
                isdinmark = false;
                isdinmarkerror = false;

                GC.Collect();
            }
        }

        public void Remove(double time)
        {
            lock (datalock)
            {
                if (spike != null)
                {
                    for (var i = 0; i < spike.Length; i++)
                    {
                        if (spike[i].Count > 0)
                        {
                            VLAExtention.Sub(spike[i], uid[i], time, double.PositiveInfinity);
                        }
                    }
                }
            }
        }

        public void Add(List<double>[] ospike, List<int>[] ouid,
            List<double[,]> alfp, List<double> alfpstarttime,
            List<double>[] odintime, List<bool>[] odinvalue,
            List<int> ocondindex, List<int> ocondrepeat, List<List<Dictionary<string, double>>> ocondstate)
        {
            lock (datalock)
            {
                if (ocondindex != null)
                {
                    if (CondIndex == null)
                    {
                        CondIndex = ocondindex;
                        AccumCondIndex = new List<int>();
                    }
                    else
                    {
                        AccumCondIndex.AddRange(CondIndex);
                        CondIndex = ocondindex;
                    }
                }

                if (ocondrepeat != null)
                {
                    if (CondRepeat == null)
                    {
                        CondRepeat = ocondrepeat;
                        AccumCondRepeat = new List<int>();
                    }
                    else
                    {
                        AccumCondRepeat.AddRange(CondRepeat);
                        CondRepeat = ocondrepeat;
                    }
                }

                if (ocondstate != null)
                {
                    if (CondState == null)
                    {
                        CondState = ocondstate;
                        AccumCondState = new List<List<Dictionary<string, double>>>();
                    }
                    else
                    {
                        AccumCondState.AddRange(CondState);
                        CondState = ocondstate;
                    }
                }

                if (ospike != null)
                {
                    if (spike == null)
                    {
                        spike = ospike;
                        uid = ouid;
                    }
                    else
                    {
                        for (var i = 0; i < ospike.Length; i++)
                        {
                            spike[i].AddRange(ospike[i]);
                            uid[i].AddRange(ouid[i]);
                        }
                    }
                }

                if (odintime != null)
                {
                    if (dintime == null)
                    {
                        dintime = odintime;
                        dinvalue = odinvalue;
                        // The falling edge time of the first TTL pulse in begin-trigger-channel 
                        // marks the start of the experiment timer in VLab
                        var bc = dintime[btc];
                        if (bc.Count > 1)
                        {
                            vlabt0 = bc[1];
                        }
                    }
                    else
                    {
                        for (var i = 0; i < odintime.Length; i++)
                        {
                            dintime[i].AddRange(odintime[i]);
                            dinvalue[i].AddRange(odinvalue[i]);
                        }
                    }
                    if (!isdinmark)
                    {
                        isdinmark = dintime[mc].Count > 0;
                    }
                    if (isdinmark && !isdinmarkerror)
                    {
                        if (dinvalue[mc].Count < ((AccumCondIndex.Count + CondIndex.Count) * nmpc))
                        {
                            var imi = dinvalue[mc].DiffFun((x, y) => x == y).ToList();
                            if (imi.Any())
                            {
                                var iimi = Enumerable.Range(0, imi.Count).Where(i => imi[i] == true).ToList();
                                for (var i = iimi.Count - 1; i >= 0; i--)
                                {
                                    dinvalue[mc].RemoveAt(iimi[i]);
                                    dintime[mc].RemoveAt(iimi[i]);
                                }
                                if (dinvalue[mc].Count < ((AccumCondIndex.Count + CondIndex.Count) * nmpc))
                                {
                                    isdinmarkerror = true;
                                }
                            }
                            else
                            {
                                isdinmarkerror = true;
                            }
                        }
                    }
                }
            }
        }

        public bool IsData(int electrodeidx, SignalType signaltype)
        {
            switch (signaltype)
            {
                case SignalType.Spike:
                    if (spike == null)
                    {
                        return false;
                    }
                    else
                    {
                        return spike[electrodeidx].Count == 0 ? false : true;
                    }
                default:
                    return false;
            }
        }
    }

    public interface IAnalysis : IDisposable
    {
        ISignal SearchSignal();
        ISignal SearchSignal(SignalSource source);
        ISignal Signal { get; set; }
        void AddAnalyzer(IAnalyzer analyzer);
        void RemoveAnalyzer(int analyzerid);
        ConcurrentDictionary<int, IAnalyzer> Analyzers { get; }
        int ClearDataPerAnalysis { get; }
        int RetainAnalysisPerClear { get; }
        DataSet DataSet { get; }
        void CondTestEnqueue(CONDTESTPARAM name, object value);
        void CondTestEndEnqueue(double time);
        void ExperimentEndEnqueue();
        bool IsExperimentAnalysisDone { get; set; }
        void StartAnalysis();
        void StopAnalysis();
        void Reset();
    }

    public class DotNetAnalysis : IAnalysis
    {
        bool disposed = false;
        ISignal signal;
        readonly int cleardataperanalysis, retainanalysisperclear, sleepresolution;
        int analysisidx = 0; bool isexperimentanalysisdone = false;

        DataSet dataset = new DataSet();
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        ConcurrentQueue<double[]> analysisqueue = new ConcurrentQueue<double[]>();
        List<double> analysistime = new List<double>();
        ConcurrentDictionary<int, IAnalyzer> idanalyzer = new ConcurrentDictionary<int, IAnalyzer>();

        Thread analysisthread;
        bool gotothreadevent = false;
        ManualResetEvent analysisthreadevent = new ManualResetEvent(true);
        object objlock = new object();
        object datalock = new object();
        object eventlock = new object();

        public DotNetAnalysis(int cleardataperanalysis = 1, int retainanalysisperclear = 1, int sleepresolution = 2)
        {
            this.cleardataperanalysis = Math.Max(0, cleardataperanalysis);
            this.retainanalysisperclear = Math.Max(0, retainanalysisperclear);
            this.sleepresolution = Math.Max(1, sleepresolution);
        }

        ~DotNetAnalysis()
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
            StopAnalysis();
            foreach (var aid in idanalyzer.Keys.ToArray())
            {
                IAnalyzer a;
                if (idanalyzer.TryGetValue(aid, out a) && a != null)
                {
                    a.Dispose();
                }
            }
            if (signal != null)
            {
                signal.Dispose();
            }
            disposed = true;
        }

        public ISignal SearchSignal()
        {
            foreach (var ss in Enum.GetValues(typeof(SignalSource)))
            {
                var s = SearchSignal((SignalSource)ss);
                if (s != null)
                {
                    return s;
                }
            }
            return null;
        }

        public ISignal SearchSignal(SignalSource source)
        {
            ISignal s = null;
            switch (source)
            {
                case SignalSource.Ripple:
                    s = new RippleSignal();
                    break;
            }
            if (s != null && s.IsOnline)
            {
                return s;
            }
            else
            {
                return null;
            }
        }

        public void Reset()
        {
            StopAnalysis();
            lock (datalock)
            {
                if (Signal != null)
                {
                    Signal.RestartCollectData(true);
                }
                foreach (var aid in idanalyzer.Keys.ToArray())
                {
                    IAnalyzer a;
                    if (idanalyzer.TryGetValue(aid, out a) && a != null)
                    {
                        a.Reset();
                    }
                }
                DataSet.Reset();
                analysisidx = 0;
                condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
                analysisqueue = new ConcurrentQueue<double[]>();
                analysistime = new List<double>();
            }
            StartAnalysis();
        }

        bool GotoThreadEvent
        {
            get { lock (objlock) { return gotothreadevent; } }
            set { lock (objlock) { gotothreadevent = value; } }
        }

        public void StartAnalysis()
        {
            lock (datalock)
            {
                if (analysisthread == null)
                {
                    analysisthread = new Thread(ProcessAnalysisQueue);
                    analysisthreadevent.Set();
                    analysisthread.Start();
                }
                else
                {
                    analysisthreadevent.Set();
                }
            }
        }

        public void StopAnalysis()
        {
            lock (datalock)
            {
                if (analysisthread != null)
                {
                    lock (eventlock)
                    {
                        analysisthreadevent.Reset();
                        GotoThreadEvent = true;
                    }
                    while (true)
                    {
                        if (!GotoThreadEvent)
                        {
                            return;
                        }
                    }
                }
            }
        }

        public void CondTestEnqueue(CONDTESTPARAM name, object value)
        {
            lock (datalock)
            {
                if (condtest.ContainsKey(name))
                {
                    condtest[name].Enqueue(value);
                }
                else
                {
                    var q = new ConcurrentQueue<object>();
                    q.Enqueue(value);
                    condtest[name] = q;
                }
            }
        }

        public void CondTestEndEnqueue(double time)
        {
            lock (datalock)
            {
                analysisidx++;
                int iscleardata = 0;
                if (cleardataperanalysis > 0)
                {
                    iscleardata = analysisidx % cleardataperanalysis == 0 ? 1 : 0;
                }
                analysisqueue.Enqueue(new double[] { analysisidx, iscleardata, time });
            }
        }

        public void ExperimentEndEnqueue()
        {
            lock (datalock)
            {
                if (analysisthread != null)
                {
                    analysisqueue.Enqueue(new double[] { -1 });
                }
                else
                {
                    IsExperimentAnalysisDone = true;
                }
            }
        }

        #region ThreadAnalysisFunction
        void ProcessAnalysisQueue()
        {
            List<double>[] spike;
            List<int>[] uid;
            List<double[,]> lfp;
            List<double> lfpstarttime;
            List<double>[] dintime;
            List<bool>[] dinvalue;

            double[] aqitem; object CondIndex, CondRepeat, CondState;
            bool isaqitem = false;
            while (true)
            {
            ThreadEvent:
                lock (eventlock)
                {
                    GotoThreadEvent = false;
                    analysisthreadevent.WaitOne();
                }
                isaqitem = analysisqueue.TryDequeue(out aqitem);
                if (isaqitem && aqitem[0] < 0)
                {
                    IsExperimentAnalysisDone = true;
                    continue;
                }
                if (isaqitem && condtest.ContainsKey(CONDTESTPARAM.CondIndex) && condtest[CONDTESTPARAM.CondIndex].TryDequeue(out CondIndex)
                    && condtest.ContainsKey(CONDTESTPARAM.CONDSTATE) && condtest[CONDTESTPARAM.CONDSTATE].TryDequeue(out CondState)
                    && condtest.ContainsKey(CONDTESTPARAM.CondRepeat) && condtest[CONDTESTPARAM.CondRepeat].TryDequeue(out CondRepeat))
                {
                    var aqidx = aqitem[0];
                    var aqclear = aqitem[1] == 1;
                    var aqtime = DataSet.VLabTimeToDataTime(aqitem[2]);
                    analysistime.Add(aqtime);
                    if (Signal != null)
                    {
                        // Wait the delayed data to be collected before analysis
                        var dl = aqtime + DataSet.Latency;
                        while (Signal.Time <= dl)
                        {
                            if (GotoThreadEvent)
                            {
                                goto ThreadEvent;
                            }
                            Thread.Sleep(sleepresolution);
                        }
                        Signal.GetData(out spike, out uid, out lfp, out lfpstarttime, out dintime, out dinvalue);
                        DataSet.Add(spike, uid, lfp, lfpstarttime, dintime, dinvalue,
                        (List<int>)CondIndex, (List<int>)CondRepeat, (List<List<Dictionary<string, double>>>)CondState);
                    }
                    else
                    {
                        DataSet.Add(null, null, null, null, null, null,
                            (List<int>)CondIndex, (List<int>)CondRepeat, (List<List<Dictionary<string, double>>>)CondState);
                    }

                    if (GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }
                    foreach (var aid in idanalyzer.Keys.ToArray())
                    {
                        IAnalyzer a;
                        if (idanalyzer.TryGetValue(aid, out a) && a != null)
                        {
                            a.Analyze(DataSet);
                            if (a.Controller != null)
                            {
                                a.Controller.Control(a.Result);
                            }
                        }
                    }
                    //Parallel.ForEach(Signal.Analyzers,(i)=>i.Analysis(DataSet));
                    if (GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }

                    if (aqclear)
                    {
                        var atidx = (int)aqidx - retainanalysisperclear - 1;
                        if (atidx >= 0)
                        {
                            DataSet.Remove(analysistime[atidx]);
                        }
                    }
                }
                else
                {
                    Thread.Sleep(sleepresolution);
                }
            }
        }
        #endregion

        public ISignal Signal
        { get { lock (objlock) { return signal; } } set { lock (objlock) { signal = value; } } }

        public DataSet DataSet
        { get { return dataset; } }

        public void AddAnalyzer(IAnalyzer analyzer)
        {
            lock (objlock)
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
        }

        public void RemoveAnalyzer(int analyzerid)
        {
            lock (objlock)
            {
                if (idanalyzer.ContainsKey(analyzerid))
                {
                    IAnalyzer a;
                    if (idanalyzer.TryRemove(analyzerid, out a) && a != null)
                    {
                        a.Dispose();
                    }
                }
            }
        }

        public ConcurrentDictionary<int, IAnalyzer> Analyzers
        { get { return idanalyzer; } }

        public int RetainAnalysisPerClear
        { get { return retainanalysisperclear; } }

        public int ClearDataPerAnalysis
        { get { return cleardataperanalysis; } }

        public bool IsExperimentAnalysisDone
        {
            get { lock (objlock) { return isexperimentanalysisdone; } }
            set { lock (objlock) { isexperimentanalysisdone = value; } }
        }
    }

}