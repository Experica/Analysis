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
        public static IAnalysis GetAnalysisSystem(this AnalysisSystem analysissystem, int cleardataperanalysis = 1)
        {
            switch (analysissystem)
            {
                default:
                    return new DotNetAnalysis(cleardataperanalysis);
            }
        }

        public static Type[] FindAll(this AnalysisInterface i)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var ts = assemblies.Where(a => a.GetName().Name == "Assembly-CSharp").SelectMany(s => s.GetTypes())
                .Where(t => t.Namespace == "VLabAnalysis" && t.IsClass && t.GetInterface(i.ToString()) != null).ToArray();
            return ts;
        }

        public static IAnalyzer Get(this int electrodid, SignalType signaltype)
        {
            switch (signaltype)
            {
                case SignalType.Spike:
                    return new MFRAnalyzer(new Signal(electrodid, signaltype));
                default:
                    return null;
            }
        }

        public static IAnalyzer Get(this Type atype, int electrodid, SignalType signaltype)
        {
            if (typeof(IAnalyzer).IsAssignableFrom(atype))
            {
                return (IAnalyzer)Activator.CreateInstance(atype, new Signal(electrodid, signaltype));
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
        public List<double> digintime;
        public Dictionary<string, List<int>> digin;
        public List<int> AccumCondIndex,CondIndex;
        public List<List<Dictionary<string, double>>> AccumCondState,CondState;

        double firstdigitaleventtime = -1;
        Experiment ex;
        int ncond;

        object objlock = new object();
        object datalock = new object();

        #region Thread Safe
        public Experiment Ex
        {
            get { lock (objlock) { return ex; } }
            set
            {
                lock (objlock)
                {
                    ex = value;
                    ncond = GetCount(ex.Cond);
                }
            }
        }

        public int NCond
        {
            get { lock (objlock) { return ncond; } }
        }

        public double FirstDigitalEventTime
        {
            get { lock (objlock) { return firstdigitaleventtime; } }
            set { lock (objlock) { firstdigitaleventtime = value; } }
        }
        #endregion

        int GetCount(Dictionary<string,List<object>> cond)
        {
            if(cond!=null)
            {
                foreach(var c in cond.Values)
                {
                    return c.Count;
                }
            }
            return 0;
        }

        public void Reset()
        {
            lock (datalock)
            {
                spike = null;
                uid = null;
                lfp = null;
                lfpstarttime = null;
                digintime = null;
                digin = null;
                CondIndex = null;
                AccumCondIndex = null;
                CondState = null;
                AccumCondState = null;
                FirstDigitalEventTime = -1;
                GC.Collect();
            }
        }

        public void Remove(double time)
        {
            lock (datalock)
            {
                if (FirstDigitalEventTime >= 0)
                {
                    var endtime = FirstDigitalEventTime + time;
                    if (spike != null)
                    {
                        for (var i = 0; i < spike.Length; i++)
                        {
                            VLAExtention.Sub(spike[i], uid[i], endtime,double.PositiveInfinity);
                        }
                    }
                    if(digintime!=null)
                    {
                        VLAExtention.Sub(digintime,null, endtime, double.PositiveInfinity);
                    }
                }
            }
        }

        public void Add(List<double>[] aspike, List<int>[] auid,
            List<double[,]> alfp, List<double> alfpstarttime,
            List<double> adigintime, Dictionary<string, List<int>> adigin,
            List<int> acondindex, List<List<Dictionary<string, double>>> acondstate)
        {
            lock (datalock)
            {
                if (spike == null)
                {
                    spike = aspike;
                    uid = auid;
                }
                else
                {
                    if (aspike != null)
                    {
                        for (var i = 0; i < aspike.Length; i++)
                        {
                            spike[i].AddRange(aspike[i]);
                            uid[i].AddRange(auid[i]);
                        }
                    }
                }
                if (lfp == null)
                {
                    lfp = alfp;
                    lfpstarttime = alfpstarttime;
                }
                else
                {
                    if (alfp != null)
                    {
                        lfp.AddRange(alfp);
                        lfpstarttime.AddRange(alfpstarttime);
                    }
                }
                if (digintime == null)
                {
                    // we define the falling edge time of the first TTL pulse as 
                    // the first digital event time which mark the start of the experiment
                    // timer in VLab, so we can align signal time and VLab time. 
                    if(adigintime!=null&&adigintime.Count>1)
                    {
                        digintime = adigintime;
                        digin = adigin;
                        FirstDigitalEventTime = adigintime[1];
                    }
                }
                else
                {
                    if (adigintime != null)
                    {
                        digintime.AddRange(adigintime);
                        foreach (var f in digin.Keys)
                        {
                            digin[f].AddRange(adigin[f]);
                        }
                    }
                }
                if (CondIndex == null)
                {
                    CondIndex = acondindex;
                    AccumCondIndex = new List<int>();
                }
                else
                {
                    if (acondindex != null)
                    {
                        AccumCondIndex.AddRange(CondIndex);
                        CondIndex = acondindex;
                    }
                }
                if(CondState==null)
                {
                    CondState = acondstate;
                    AccumCondState = new List<List<Dictionary<string, double>>>();
                }
                else
                {
                    if (acondstate != null)
                    {
                        AccumCondState.AddRange(CondState);
                        CondState = acondstate;
                    }
                }
            }
        }

        public bool IsData(int electrodid, SignalType signaltype)
        {
            //lock (datalock)
            //{
                switch (signaltype)
                {
                    case SignalType.Spike:
                        if (spike == null)
                        {
                            return false;
                        }
                        else
                        {
                            var st = spike[electrodid];
                            if (st == null || st.Count == 0)
                            {
                                return false;
                            }
                            return true;
                        }
                    case SignalType.LFP:
                        if (lfp == null)
                        {
                            return false;
                        }
                        else
                        {
                            return false;
                        }
                    default:
                        return false;
                }
            //}
        }
    }

    public interface IAnalysis
    {
        bool SearchSignal();
        bool SearchSignal(SignalSource sigsys);
        ISignal Signal { get; }
        void Reset();
        int ClearDataPerAnalysis { get; set; }
        DataSet DataSet { get; }
        void CondTestEndEnqueue(double time);
        void CondTestEnqueue(CONDTESTPARAM name, object value);
        void ExperimentEndEnqueue();
        bool IsAnalysisDone { get; set; }
    }

    public class DotNetAnalysis : IAnalysis, IDisposable
    {
        ISignal signal;
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        int cleardataperanalysis;
        ConcurrentQueue<int[]> analysisqueue = new ConcurrentQueue<int[]>();
        ConcurrentQueue<double[]> analysistimequeue = new ConcurrentQueue<double[]>();
        int analysisidx = 0;
        Thread analysisthread;
        DataSet dataset = new DataSet();
        bool gotothreadevent = false;
        bool isanalysisdone = false;
        ManualResetEvent analysisthreadevent = new ManualResetEvent(true);
        readonly int sleepresolution;

        object lockobj = new object();
        object datalock = new object();
        object eventlock = new object();

        public DotNetAnalysis(int cleardataperanalysis = 1, int sleepresolution = 2)
        {
            this.cleardataperanalysis = cleardataperanalysis;
            this.sleepresolution = Math.Max(0, sleepresolution);
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
            if (disposing)
            {
                //signal.Dispose();
            }
        }

        public bool SearchSignal()
        {
            foreach (var s in Enum.GetValues(typeof(SignalSource)))
            {
                if (SearchSignal((SignalSource)s))
                {
                    return true;
                }
            }
            return false;
        }

        public bool SearchSignal(SignalSource sigsys)
        {
            switch (sigsys)
            {
                case SignalSource.Ripple:
                    var ripple = new RippleSignal();
                    if (ripple.IsOnline)
                    {
                        signal = ripple;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// stop processing thread then safely clear all
        /// </summary>
        public void Reset()
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
                            DataSet.Reset();
                            InitQueues();
                            if (Signal != null)
                            {
                                Signal.Reset();
                            }
                            analysisidx = 0;
                            analysisthreadevent.Set();
                            return;
                        }
                    }
                }
                else
                {
                    if(Signal!=null)
                    {
                        Signal.Reset();
                    }
                    InitQueues();
                    analysisidx = 0;
                }
            }
        }

        void InitQueues()
        {
            condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
            analysisqueue = new ConcurrentQueue<int[]>();
            analysistimequeue = new ConcurrentQueue<double[]>();
        }

        #region Thread Safe
        bool GotoThreadEvent
        {
            get { lock (lockobj) { return gotothreadevent; } }
            set { lock (lockobj) { gotothreadevent = value; } }
        }

        public int ClearDataPerAnalysis
        {
            get { lock (lockobj) { return cleardataperanalysis; } }
            set { lock (lockobj) { cleardataperanalysis = value; } }
        }

        public bool IsAnalysisDone
        {
            get { lock (lockobj) { return isanalysisdone; } }
            set { lock (lockobj) { isanalysisdone = value; } }
        }
        #endregion

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
                int iscleardata = 1;
                if (cleardataperanalysis > 0)
                {
                    iscleardata = analysisidx % cleardataperanalysis == 0 ? 1 : 0;
                }
                analysisqueue.Enqueue(new int[] { analysisidx, iscleardata });
                analysistimequeue.Enqueue(new double[] { analysisidx, time });
                if (analysisthread == null)
                {
                    analysisthread = new Thread(ProcessAnalysisQueue);
                    analysisthreadevent.Set();
                    analysisthread.Start();
                }
            }
        }

        public void ExperimentEndEnqueue()
        {
            lock(datalock)
            {
                if (analysisthread != null)
                {
                    analysisqueue.Enqueue(new int[] { -1, 0 });
                }
                else
                {
                    IsAnalysisDone = true;
                }
            }
        }

        #region ThreadFunction
        void ProcessAnalysisQueue()
        {
            List<double>[] spike;
            List<int>[] uid;
            List<double[,]> lfp;
            List<double> lfpstarttime;
            List<double> digintime;
            Dictionary<string, List<int>> digin;
            int[] aq;object CondIndex, CondState;
            bool isanalysisqueue;

            while (true)
            {
                ThreadEvent:
                lock (eventlock)
                {
                    GotoThreadEvent = false;
                    analysisthreadevent.WaitOne();
                }
                isanalysisqueue = analysisqueue.TryDequeue(out aq);
                if(isanalysisqueue&&aq[0]<0)
                {
                    IsAnalysisDone = true;
                    continue;
                }
                if (isanalysisqueue && condtest.ContainsKey( CONDTESTPARAM.CondIndex)
                    && condtest[CONDTESTPARAM.CondIndex].TryDequeue(out CondIndex) && condtest.ContainsKey( CONDTESTPARAM.CONDSTATE)
                    && condtest[ CONDTESTPARAM.CONDSTATE].TryDequeue(out CondState))
                {
                    if (GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }
                    if (Signal != null)
                    {
                        Signal.GetData(out spike, out uid, out lfp, out lfpstarttime, out digintime, out digin);
                        DataSet.Add(spike, uid, lfp, lfpstarttime, digintime, digin,
                        (List<int>)CondIndex, (List<List<Dictionary<string, double>>>)CondState);
                    }
                    else
                    {
                        DataSet.Add(null, null, null, null, null, null,
                            (List<int>)CondIndex, (List<List<Dictionary<string, double>>>)CondState);
                    }

                    if(GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }
                    foreach (var a in Signal.Analyzers.Values)
                    {
                        a.Analyze(DataSet);
                        if(a.Controller!=null)
                        {
                            a.Controller.Control(a.Result);
                        }
                    }
                    //Parallel.ForEach(Signal.Analyzers,(i)=>i.Analysis(DataSet));
                    if(GotoThreadEvent)
                    {
                        goto ThreadEvent;
                    }

                    // Clear old data
                    var aqidx = aq[0];
                    var aqclear = aq[1] == 1;
                    if (aqclear)
                    {
                        double[] atq;
                    FindTime:
                        if (analysistimequeue.TryDequeue(out atq))
                        {
                            var atqidx = atq[0];
                            var atqtime = atq[1];
                            if (atqidx >= aqidx - ClearDataPerAnalysis + 1)
                            {
                                DataSet.Remove(atqtime);
                            }
                            else
                            {
                                goto FindTime;
                            }
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
        {
            get { return signal; }
        }

        public DataSet DataSet
        {
            get
            {
                return dataset;
            }
        }

        
    }

}