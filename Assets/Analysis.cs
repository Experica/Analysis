/*
Analysis.cs is part of the VLAB project.
Copyright (c) 2017 Li Alex Zhang and Contributors

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

    public enum VisualizeMode
    {
        First,
        Last,
        All
    }

    public interface IAnalysis : IDisposable
    {
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
        void SaveVisualization(int width, int height, int dpi);
        void VisualizeResults(VisualizeMode mode);
    }

    public class DotNetAnalysis : IAnalysis
    {
        int disposecount = 0;
        readonly int cleardataperanalysis, retainanalysisperclear, sleepresolution;

        ISignal signal; readonly object signallock = new object();
        public ISignal Signal
        { get { lock (signallock) { return signal; } } set { lock (signallock) { signal = value; } } }
        DataSet dataset = new DataSet();
        ConcurrentDictionary<int, IAnalyzer> idanalyzer = new ConcurrentDictionary<int, IAnalyzer>();
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        ConcurrentQueue<double[]> analysisqueue = new ConcurrentQueue<double[]>();
        readonly List<double> analysistime = new List<double>();
        int analysisidx = 0;

        Thread analysisthread;
        ManualResetEvent analysisthreadevent = new ManualResetEvent(true);
        readonly object datalock = new object();
        readonly object gotoeventlock = new object();
        bool gotothreadevent = false; readonly object gotolock = new object();
        bool GotoThreadEvent
        {
            get { lock (gotolock) { return gotothreadevent; } }
            set { lock (gotolock) { gotothreadevent = value; } }
        }
        bool isanalyzing = false; readonly object isanalyzelock = new object();
        bool IsAnalyzing
        {
            get { lock (isanalyzelock) { return isanalyzing; } }
            set { lock (isanalyzelock) { isanalyzing = value; } }
        }
        bool isexperimentanalysisdone = false; readonly object isexanalyzedonelock = new object();
        public bool IsExperimentAnalysisDone
        {
            get { lock (isexanalyzedonelock) { return isexperimentanalysisdone; } }
            set { lock (isexanalyzedonelock) { isexperimentanalysisdone = value; } }
        }

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
            if (Interlocked.Exchange(ref disposecount, 1) == 1) return;
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
        }

        public void Reset()
        {
            lock (datalock)
            {
                StopAnalysis();
                if (signal != null)
                {
                    signal.Restart(true);
                }
                DataSet.Reset();
                foreach (var aid in idanalyzer.Keys.ToArray())
                {
                    IAnalyzer a;
                    if (idanalyzer.TryGetValue(aid, out a) && a != null)
                    {
                        a.Reset();
                    }
                }
                condtest.Clear();
                analysisqueue = new ConcurrentQueue<double[]>();
                analysistime.Clear();
                analysisidx = 0;
                StartAnalysis();
            }
        }

        public void SaveVisualization(int width, int height, int dpi)
        {
            var datapath = DataSet.Ex.DataPath;
            var datadir = Path.GetDirectoryName(datapath);
            var dataname = Path.GetFileNameWithoutExtension(datapath);
            foreach (var i in Analyzers.Keys.ToList())
            {
                IAnalyzer a;
                if (Analyzers.TryGetValue(i, out a) && a != null && a.Visualizer != null)
                {
                    var filename = dataname + "_Ch" + a.Signal.Channel + "_" + a.GetType().Name + "_" + a.Visualizer.GetType().Name;
                    var filedir = Path.Combine(datadir, "Ch" + a.Signal.Channel);
                    if (!Directory.Exists(filedir))
                    {
                        Directory.CreateDirectory(filedir);
                    }
                    a.Visualizer.Save(Path.Combine(filedir, filename), width, height, dpi);
                }
            }
        }

        public void VisualizeResults(VisualizeMode mode = VisualizeMode.First)
        {
            foreach (var i in Analyzers.Keys.ToArray())
            {
                IAnalyzer a;
                if (Analyzers.TryGetValue(i, out a) && a != null && a.Visualizer != null)
                {
                    IResult r;
                    switch (mode)
                    {
                        case VisualizeMode.First:
                            if (a.ResultVisualizeQueue.TryDequeue(out r))
                            {
                                a.Visualizer.Visualize(r);
                            }
                            break;
                        case VisualizeMode.All:
                            while (a.ResultVisualizeQueue.TryDequeue(out r))
                            {
                                a.Visualizer.Visualize(r);
                            }
                            break;
                        case VisualizeMode.Last:
                            while (a.ResultVisualizeQueue.TryDequeue(out r) && a.ResultVisualizeQueue.IsEmpty)
                            {
                                a.Visualizer.Visualize(r);
                            }
                            break;
                    }
                }
            }
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
                    IsAnalyzing = true;
                }
                else
                {
                    if (!IsAnalyzing)
                    {
                        analysisthreadevent.Set();
                        IsAnalyzing = true;
                    }
                }
            }
        }

        public void StopAnalysis()
        {
            lock (datalock)
            {
                if (analysisthread != null && IsAnalyzing)
                {
                    lock (gotoeventlock)
                    {
                        analysisthreadevent.Reset();
                        GotoThreadEvent = true;
                    }
                    while (true)
                    {
                        if (!GotoThreadEvent)
                        {
                            IsAnalyzing = false;
                            break;
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
                if (signal != null)
                {
                    signal.Stop(true);
                }
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
            List<double>[] ospike;
            List<int>[] ouid;
            List<double[,]> olfp;
            List<double> olfpstarttime;
            List<double>[] odintime;
            List<int>[] odinvalue;

            object ocondindex, ocondrepeat, ocondstate; double[] aqitem; bool isaqitem = false;
            while (true)
            {
                ThreadEvent:
                lock (gotoeventlock)
                {
                    GotoThreadEvent = false;
                    analysisthreadevent.WaitOne();
                }
                isaqitem = analysisqueue.TryDequeue(out aqitem);
                if (isaqitem && aqitem[0] == -1)
                {
                    IsExperimentAnalysisDone = true;
                    continue;
                }
                if (isaqitem && condtest.ContainsKey(CONDTESTPARAM.CondIndex) && condtest[CONDTESTPARAM.CondIndex].TryDequeue(out ocondindex)
                    && condtest.ContainsKey(CONDTESTPARAM.CONDSTATE) && condtest[CONDTESTPARAM.CONDSTATE].TryDequeue(out ocondstate)
                    && condtest.ContainsKey(CONDTESTPARAM.CondRepeat) && condtest[CONDTESTPARAM.CondRepeat].TryDequeue(out ocondrepeat))
                {
                    var aqidx = aqitem[0];
                    var aqclear = aqitem[1] == 1;
                    var aqdatatime = DataSet.VLabTimeToDataTime(aqitem[2]);
                    analysistime.Add(aqdatatime);
                    if (Signal != null)
                    {
                        // Wait the delayed data to be collected before analysis
                        var delayedtime = aqdatatime + DataSet.DataLatency;
                        while (Signal.Time <= delayedtime)
                        {
                            if (GotoThreadEvent)
                            {
                                goto ThreadEvent;
                            }
                            Thread.Sleep(sleepresolution);
                        }
                        Signal.Read(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue);
                        DataSet.Add(ospike, ouid, olfp, olfpstarttime, odintime, odinvalue,
                        (List<int>)ocondindex, (List<int>)ocondrepeat, (List<List<Dictionary<string, double>>>)ocondstate);
                    }
                    else
                    {
                        DataSet.Add(null, null, null, null, null, null,
                            (List<int>)ocondindex, (List<int>)ocondrepeat, (List<List<Dictionary<string, double>>>)ocondstate);
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
                        var actidx = (int)aqidx - 1 - retainanalysisperclear;
                        if (actidx >= 0)
                        {
                            DataSet.Remove(analysistime[actidx]);
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

        public DataSet DataSet
        { get { return dataset; } }

        public void AddAnalyzer(IAnalyzer analyzer)
        {
            lock (datalock)
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
            lock (datalock)
            {
                if (idanalyzer.ContainsKey(analyzerid))
                {
                    IAnalyzer a;
                    if (idanalyzer.TryRemove(analyzerid, out a) && a != null && !IsAnalyzing)
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
    }
}