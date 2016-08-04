// -----------------------------------------------------------------------------
// Analysis.cs is part of the VLAB project.
// Copyright (c) 2016 Li Alex Zhang and Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included 
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
// OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// -----------------------------------------------------------------------------

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

    public enum ANALYSISINTERFACE
    {
        IAnalyzer,
        IVisualizer,
        IController
    }

    public static class AnalysisFactory
    {
        public static IAnalysis GetAnalysisSystem(this AnalysisSystem analysissystem, int cleardataperanalysis = 1)
        {
            IAnalysis als;
            switch (analysissystem)
            {
                default:
                    als = new DotNetAnalysis(cleardataperanalysis);
                    break;
            }
            return als;
        }

        public static Type[] FindAll(ANALYSISINTERFACE i)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var ts = assemblies.Where(a => a.GetName().Name == "Assembly-CSharp").SelectMany(s => s.GetTypes())
                .Where(t => t.Namespace == "VLabAnalysis" && t.IsClass && t.GetInterface(i.ToString()) != null).ToArray();
            return ts;
        }

        public static IAnalyzer Get(SIGNALTYPE signaltype)
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
    }

    

    public interface IAnalysis
    {
        bool SearchSignal();
        ISignal Signal { get; }
        void Reset();
        int ClearDataPerAnalysis { get; set; }
        DataSet DataSet { get; }
        void CondTestEndEnqueue(double time);
        void CondTestEnqueue(CONDTESTPARAM name, object value);
        bool IsAnalysisDone { get; set; }
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

        public double exstarttime = -1;

        Experiment ex;
        object lockobj = new object();
        object datalock = new object();

        public Experiment Ex
        {
            get
            {
                lock(lockobj)
                {
                    return ex;
                }
            }
            set
            {
                lock(lockobj)
                {
                    ex = value;
                }
            }
        }

        public int CondN
        {
            get
            {
                int cn = 0;
                if (Ex.Cond != null)
                {
                    foreach (var c in Ex.Cond.Values)
                    {
                        cn = c.Count;
                        break;
                    }
                }
                return cn;
            }
        }

        public void Clear()
        {
            if (spike != null)
            {
                for (var i = 0; i < spike.Length; i++)
                {
                    spike[i].Clear();
                    uid[i].Clear();
                }
            }
            if (lfp != null)
            {
                lfp.Clear();
                lfpstarttime.Clear();
            }
            if (AccumCondIndex != null)
            {
                AccumCondIndex.Clear();
            }
        }

        public void Remove(double time)
        {
            lock (datalock)
            {
                double endtime;
                if (exstarttime > 0)
                {
                    endtime = exstarttime + time;
                    if (spike != null)
                    {
                        for (var i = 0; i < spike.Length; i++)
                        {
                            AnalysisMethod.Sub(ref spike[i], ref uid[i], endtime,double.PositiveInfinity);
                        }
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
                    lfp.AddRange(alfp);
                    lfpstarttime.AddRange(alfpstarttime);
                }
                if (exstarttime < 0 && adigintime.Count > 1)
                {
                    exstarttime = adigintime[1];
                }
                if (digintime == null)
                {
                    digintime = adigintime;
                    digin = adigin;
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
                    AccumCondIndex.AddRange(CondIndex);
                    CondIndex = acondindex;
                }
                if(CondState==null)
                {
                    CondState = acondstate;
                    AccumCondState = new List<List<Dictionary<string, double>>>();
                }
                else
                {
                    AccumCondState.AddRange(CondState);
                    CondState = acondstate;
                }
            }
        }

        public bool IsData(int elec, SIGNALTYPE signaltype)
        {
            bool v = true;
            switch (signaltype)
            {
                case SIGNALTYPE.Spike:
                    if (spike == null)
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
                    if (lfp == null)
                    {
                        v = false;
                        break;
                    }
                    else
                    {
                        bool vv = false;
                        foreach (var l in lfp)
                        {
                            if (l.GetLength(1) > 0)
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

    public class DotNetAnalysis : IAnalysis, IDisposable
    {
        ISignal signal;
        RippleSignal ripple = new RippleSignal();
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        int cleardataperanalysis;
        ConcurrentQueue<int[]> analysisqueue = new ConcurrentQueue<int[]>();
        ConcurrentQueue<double[]> analysistimequeue = new ConcurrentQueue<double[]>();
        int analysisidx = 0;
        Thread analysisthread;
        DataSet dataset = new DataSet();
        bool isanalysisdone = true;
        ManualResetEvent analysisthreadevent = new ManualResetEvent(true);
        readonly int sleepresolution;
        object lockobj = new object();

        public DotNetAnalysis(int cleardataperanalysis = 1, int sleepresolution = 2)
        {
            this.cleardataperanalysis = cleardataperanalysis;
            this.sleepresolution = Math.Max(0, sleepresolution);
        }

        public bool SearchSignal()
        {
            foreach (var s in Enum.GetValues(typeof(SIGNALSYSTEM)))
            {
                if (SearchSignal((SIGNALSYSTEM)s))
                {
                    return true;
                }
            }
            return false;
        }

        bool SearchSignal(SIGNALSYSTEM sigsys)
        {
            bool v = false;
            switch (sigsys)
            {
                case SIGNALSYSTEM.Ripple:
                    if (ripple.IsSignalOnline)
                    {
                        signal = ripple;
                        return true;
                    }
                    break;
                default:
                    return false;
            }
            return v;
        }

        public ISignal Signal
        {
            get { return signal; }
        }

        public int ClearDataPerAnalysis
        {
            get
            {
                int t = 0;
                lock (lockobj)
                {
                    t = cleardataperanalysis;
                }
                return t;
            }
            set { lock (lockobj) { cleardataperanalysis = value; } }
        }

        public DataSet DataSet
        {
            get
            {
                return dataset;
            }
        }

        /// <summary>
        /// stop processing thread then safely clear all
        /// </summary>
        public void Reset()
        {
            analysisthreadevent.Reset();
            while (true)
            {
                if (IsAnalysisDone)
                {
                    condtest.Clear();
                    DataSet.Clear();
                    int[] aq;
                    while (true)
                    {
                        if (!analysisqueue.TryDequeue(out aq))
                        {
                            break;
                        }
                    }
                    analysisthreadevent.Set();
                    break;
                }
            }
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
                ripple.Dispose();
            }
        }

        public void CondTestEnqueue(CONDTESTPARAM name, object value)
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

        public void CondTestEndEnqueue(double time)
        {
            analysisidx++;
            int iscleardata = 0;
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

        #region Thread Safe
        /// <summary>
        /// thread safe
        /// </summary>
        public bool IsAnalysisDone
        {
            get
            {
                bool t = false;
                lock (lockobj)
                {
                    t = isanalysisdone;
                }
                return t;
            }
            set
            {
                lock (lockobj)
                {
                    isanalysisdone = value;
                }
            }
        }
        #endregion

        /// <summary>
        /// This is the analysisqueue processing thread function, make sure it doesn't conflict
        /// with the class calling thread
        /// </summary>
        void ProcessAnalysisQueue()
        {
            List<double>[] spike;
            List<int>[] uid;
            List<double[,]> lfp;
            List<double> lfpstarttime;
            List<double> digintime;
            Dictionary<string, List<int>> digin;
            int[] aq;
            object CondIndex, CondState;
            while (true)
            {
                analysisthreadevent.WaitOne();
                if (analysisqueue.TryDequeue(out aq) && condtest.ContainsKey( CONDTESTPARAM.CondIndex)
                    && condtest[CONDTESTPARAM.CondIndex].TryDequeue(out CondIndex) && condtest.ContainsKey( CONDTESTPARAM.CONDSTATE)
                    && condtest[ CONDTESTPARAM.CONDSTATE].TryDequeue(out CondState))
                {
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
                    IsAnalysisDone = false;
                    foreach (var a in Signal.Analyzers)
                    {
                        a.Analysis(DataSet);
                    }
                    //Parallel.ForEach(Signal.Analyzers,(i)=>i.Analysis(DataSet));
                    IsAnalysisDone = true;
                    // Clearing old data
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


    }

    public class AnalysisResult
    {
        public ConcurrentDictionary<int, List<double>> mfr = new ConcurrentDictionary<int, List<double>>();
        public int Elec;
        public Dictionary<string, List<object>> cond=new Dictionary<string, List<object>>();
        public string ExID;

        public AnalysisResult(int elec=1,string exid="")
        {
            Elec = elec;
            ExID = exid;
        }

        public AnalysisResult DeepCopy()
        {
            var copy = new AnalysisResult(Elec,ExID);
            foreach(var i in mfr.Keys)
            {
                var v = new List<double>();
                foreach(var fr in mfr[i])
                {
                    v.Add(fr);
                }
                copy.mfr[i] = v;
            }
            foreach(var c in cond.Keys)
            {
                var v = new List<object>();
                foreach(var cv in cond[c])
                {
                    v.Add(cv);
                }
                copy.cond[c] = v;
            }
            return copy;
        }
    }

    public static class AnalysisMethod
    {
        public static int Count(this List<double> st, double start,double end)
        {
            int c = 0;
            if (end > start)
            {
                foreach (var t in st)
                {
                    if (t >= start && t < end)
                    {
                        c++;
                    }
                }
            }
            return c;
        }

        public static void Sub(ref List<double> st,ref List<int> uid,double start,double end)
        {
            var si = st.FindIndex(i => i >= start);
            if(si<0)
            {
                st.Clear();
                if(uid!=null)
                {
                    uid.Clear();
                }
            }
            else
            {
                st.RemoveRange(0, si);
                var ei = st.FindIndex(i => i >= end);
                if(ei>=si)
                {
                    var l = st.Count;
                    st.RemoveRange(ei, l - ei);
                    if (uid != null)
                    {
                        uid.RemoveRange(0, si);
                        uid.RemoveRange(ei, l - ei);
                    }
                }
            } 
        }

        public static double FindCondStateTime(this List<Dictionary<string, double>> condstate,string state)
        {
            foreach(var c in condstate)
            {
                if(c.ContainsKey(state))
                {
                    return c[state];
                }
            }
            return 0;
        }

        public static double MFR(this List<double> st, double start, double end)
        {
            if (end > start)
            {
                return st.Count(start, end) / ((end - start) / 1000);
            }
            return 0;
        }

        public static double SEM(this List<double> x)
        {
           return x.StandardDeviation() / Math.Sqrt(x.Count);
        }

        public static string GetUnit(this string factorname)
        {
            string u = "";
            switch(factorname)
            {
                case "ori":
                    u = "deg";
                    break;
            }
            return u;
        }

    }

}