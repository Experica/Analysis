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
using MathNet;

namespace VLabAnalysis
{ 
    public enum ANALYSISINTERFACE
    {
        IAnalyzer,
        IVisualizer,
        IController
    }

    public static class AnalysisFactory
    {
        public static IAnalysis GetAnalysisSystem(string name = "DotNet", int cleardataperanalysis = 1)
        {
            IAnalysis als;
            switch (name)
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
        ConcurrentDictionary<string, ConcurrentQueue<object>> CondTest { get;  }
        void Reset();
        int ClearDataPerAnalysis { get; set; }
        DataSet DataSet { get; }
        void AddAnalysisQueue();
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
        public List<int> CondIndex;

        Experiment ex;
        object lockobj = new object();

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
            if (CondIndex != null)
            {
                CondIndex.Clear();
            }
        }

        public void Add(List<double>[] aspike, List<int>[] auid,
            List<double[,]> alfp, List<double> alfpstarttime,
            List<double> adigintime, Dictionary<string, List<int>> adigin,
            List<int> acondindex)
        {
            if (spike == null)
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
            if (digintime == null)
            {
                digintime = adigintime;
                digin = adigin;
            }
            else
            {
                digintime.AddRange(adigintime);
                foreach (var f in digin.Keys)
                {
                    digin[f].AddRange(adigin[f]);
                }
            }
            if (CondIndex == null)
            {
                CondIndex = acondindex;
            }
            else
            {
                CondIndex.AddRange(acondindex);
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
        ConcurrentDictionary<string,ConcurrentQueue< object>> condtest = new ConcurrentDictionary<string, ConcurrentQueue<object>>();
        int cleardataperanalysis;
        ConcurrentQueue<bool> analysisqueue = new ConcurrentQueue<bool>();
        int analysisidx = 0;
        Thread thread;
        DataSet dataset = new DataSet();
        bool isanalysisdone=true;
        ManualResetEvent threadevent = new ManualResetEvent(true);
        object lockobj = new object();

        public DotNetAnalysis(int cleardataperanalysis = 1)
        {
            this.cleardataperanalysis = cleardataperanalysis;
        }

        public bool SearchSignal()
        {
            foreach(var s in Enum.GetValues(typeof( SIGNALSYSTEM)))
            {
                if(SearchSignal((SIGNALSYSTEM)s))
                {
                    return true;
                }
            }
            return false;
        }

        bool SearchSignal(SIGNALSYSTEM sigsys)
        {
            bool v = false;
            switch(sigsys)
            {
                case SIGNALSYSTEM.Ripple:
                    if(ripple.IsSignalOnline)
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

        public ConcurrentDictionary<string,ConcurrentQueue< object>> CondTest
        {
            get { return condtest; }
        }

        public int ClearDataPerAnalysis
        {
            get { return cleardataperanalysis; }
            set { cleardataperanalysis = value; }
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
            threadevent.Reset();
            while(true)
            {
                if(IsAnalysisDone)
                {
                    CondTest.Clear();
                    DataSet.Clear();
                    bool aq;
                    while(true)
                    {
                        if(!analysisqueue.TryDequeue(out aq))
                        {
                            break;
                        }
                    }
                    threadevent.Set();
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

        public void AddAnalysisQueue()
        {
            analysisidx++;
            bool iscleardata=false;
            if (cleardataperanalysis > 0)
            {
                iscleardata = analysisidx % cleardataperanalysis == 0;
            }
            analysisqueue.Enqueue(iscleardata);
            if (thread == null)
            {
                thread = new Thread(ProcessAnalysisQueue);
                thread.Start();
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
                lock(lockobj)
                {
                    t = isanalysisdone;
                }
                return t;
            }
            set
            {
                lock(lockobj)
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
            Dictionary<string, List< int>> digin;
            bool iscleardata;
            object CondIndex;
            while (true)
            {
                threadevent.WaitOne();
                if (analysisqueue.TryDequeue(out iscleardata) &&condtest.ContainsKey("CondIndex")&&condtest["CondIndex"].TryDequeue(out CondIndex))
                {
                    Signal.GetSignal(out spike, out uid, out lfp, out lfpstarttime,out digintime,out digin);

                    DataSet.Add(spike, uid, lfp, lfpstarttime,digintime,digin, (List<int>)CondIndex);
                    IsAnalysisDone = false;
                    foreach(var a in Signal.Analyzers)
                    {
                        a.Analysis(DataSet);
                    }
                    //Parallel.ForEach(Signal.Analyzers,(i)=>i.Analysis(DataSet));
                    IsAnalysisDone = true;
                    if(iscleardata)
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

    public class AnalysisResult
    {
        public ConcurrentDictionary<int, List<double>> mfr = new ConcurrentDictionary<int, List<double>>();

        public AnalysisResult DeepCopy()
        {
            var copy = new AnalysisResult();
            foreach(var i in mfr.Keys)
            {
                var v = new List<double>();
                foreach(var fr in mfr[i])
                {
                    v.Add(fr);
                }
                copy.mfr[i] = v;
            }
            return copy;
        }
    }

    public class SpikeAnalysis
    {
        public static int Count(List<double> st,double start,double end)
        {
            int c = 0;
            foreach(var t in st)
            {
                if(t>=start && t<end)
                {
                    c++;
                }
            }
            return c;
        }
    }

}