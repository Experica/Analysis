/*
DotNetAnalysis.cs is part of the VLAB project.
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
    public class ConditionTestAnalysis : IAnalysis
    {
        int disposecount = 0;
        int gotothreadevent = 0;
        int cleardataperanalysis, retainanalysisperclear, sleepresolution;
        int analysiseventidx = 0;
        public int AnalysisEventIndex { get { lock (apilock) { return analysiseventidx; } } }
        int analysisdone = 0;
        public int AnalysisDone { get { return Interlocked.CompareExchange(ref analysisdone, 0, -1); } }
        int visualizationdone = 0;
        public int VisualizationDone { get { return Interlocked.CompareExchange(ref visualizationdone, 0, -1); } }

        ISignal signal;
        public ISignal Signal
        {
            get { lock (apilock) { return signal; } }
            set { lock (apilock) { signal = value; } }
        }
        VLADataSet dataset = new VLADataSet();
        ConcurrentDictionary<int, IAnalyzer> idanalyzer = new ConcurrentDictionary<int, IAnalyzer>();
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        ConcurrentQueue<AnalysisEvent> analysisqueue = new ConcurrentQueue<AnalysisEvent>();
        readonly List<double> analysistime = new List<double>();

        Thread analysisthread;
        ManualResetEvent analysisthreadevent = new ManualResetEvent(false);
        readonly object apilock = new object();
        readonly object gotoeventlock = new object();
        bool analyzing = false;
        public bool IsAnalyzing { get { lock (apilock) { return analyzing; } } }
        bool isexperimentanalysisdone = false;
        public bool IsExperimentAnalysisDone
        {
            get { lock (apilock) { return isexperimentanalysisdone; } }
            set { lock (apilock) { isexperimentanalysisdone = value; } }
        }

        public ConditionTestAnalysis(int cleardataperanalysis = 1, int retainanalysisperclear = 1, int sleepresolution = 2)
        {
            this.cleardataperanalysis = Math.Max(0, cleardataperanalysis);
            this.retainanalysisperclear = Math.Max(0, retainanalysisperclear);
            this.sleepresolution = Math.Max(1, sleepresolution);
        }

        ~ConditionTestAnalysis()
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
            if (1 == Interlocked.Exchange(ref disposecount, 1))
            {
                return;
            }
            Stop();
            if (signal != null)
            {
                signal.Dispose();
            }
            foreach (var aid in idanalyzer.Keys.ToArray())
            {
                IAnalyzer a;
                if (idanalyzer.TryGetValue(aid, out a) && a != null)
                {
                    a.Dispose();
                }
            }
        }

        public void CondTestEnqueue(CONDTESTPARAM name, object value)
        {
            lock (apilock)
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
            lock (apilock)
            {
                analysiseventidx++;
                bool iscleardata = false;
                if (cleardataperanalysis > 0)
                {
                    iscleardata = analysiseventidx % cleardataperanalysis == 0;
                }
                analysisqueue.Enqueue(new AnalysisEvent(analysiseventidx, iscleardata, time));
            }
        }

        public void ExperimentEndEnqueue()
        {
            lock (apilock)
            {
                if (signal != null)
                {
                    signal.Stop(true);
                }
                analysisqueue.Enqueue(new AnalysisEvent(index: -1));
            }
        }

        public VLADataSet DataSet { get { return dataset; } }

        public int RetainAnalysisPerClear
        {
            get { lock (apilock) { return retainanalysisperclear; } }
            set { lock (apilock) { retainanalysisperclear = value; } }
        }

        public int ClearDataPerAnalysis
        {
            get { lock (apilock) { return cleardataperanalysis; } }
            set { lock (apilock) { cleardataperanalysis = value; } }
        }

        public ConcurrentDictionary<int, IAnalyzer> Analyzers { get { return idanalyzer; } }

        public void AddAnalyzer(IAnalyzer analyzer)
        {
            lock (apilock)
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
            lock (apilock)
            {
                if (idanalyzer.ContainsKey(analyzerid))
                {
                    IAnalyzer a;
                    if (idanalyzer.TryRemove(analyzerid, out a) && a != null && !analyzing)
                    {
                        a.Dispose();
                    }
                }
            }
        }

        public void Start()
        {
            lock (apilock)
            {
                if (analysisthread == null)
                {
                    analysisthread = new Thread(ProcessAnalysisQueue);
                    analysisthread.Name = "DotNetAnalysis";
                    analysisthreadevent.Set();
                    analyzing = true;
                    analysisthread.Start();
                }
                else
                {
                    if (!analyzing)
                    {
                        analyzing = true;
                        analysisthreadevent.Set();
                    }
                }
            }
        }

        public void Stop()
        {
            lock (apilock)
            {
                if (analysisthread != null && analyzing)
                {
                    lock (gotoeventlock)
                    {
                        analysisthreadevent.Reset();
                        Interlocked.Exchange(ref gotothreadevent, 1);
                    }
                    while (true)
                    {
                        if (0 == Interlocked.CompareExchange(ref gotothreadevent, 0, 255))
                        {
                            analyzing = false;
                            break;
                        }
                    }
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
            object ocondindex = null, ocondrepeat = null, ocondstate = null;
            AnalysisEvent aqevent; bool isaqevent;

            while (true)
            {
                ThreadEvent:
                lock (gotoeventlock)
                {
                    Interlocked.Exchange(ref gotothreadevent, 0);
                    analysisthreadevent.WaitOne();
                }
                isaqevent = analysisqueue.TryDequeue(out aqevent);
                if (isaqevent)
                {
                    if (aqevent.Index < 0)
                    {
                        Interlocked.Increment(ref analysisdone);
                        IsExperimentAnalysisDone = true;
                        if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                        {
                            goto ThreadEvent;
                        }
                        Thread.Sleep(sleepresolution);
                        continue;
                    }
                }
                else
                {
                    if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }
                    Thread.Sleep(sleepresolution);
                    continue;
                }
                if (condtest.ContainsKey(CONDTESTPARAM.CondIndex) && condtest[CONDTESTPARAM.CondIndex].TryDequeue(out ocondindex)
                    && condtest.ContainsKey(CONDTESTPARAM.CONDSTATE) && condtest[CONDTESTPARAM.CONDSTATE].TryDequeue(out ocondstate))
                {
                    if (condtest.ContainsKey(CONDTESTPARAM.CondRepeat)) { condtest[CONDTESTPARAM.CondRepeat].TryDequeue(out ocondrepeat); }

                    var aqeventdatatime = DataSet.VLabTimeToDataTime(aqevent.Time);
                    analysistime.Add(aqeventdatatime);
                    if (Signal != null)
                    {
                        // Wait the delayed data to be collected before analysis
                        var delayedtime = aqeventdatatime + DataSet.DataLatency;
                        while (Signal.Time <= delayedtime)
                        {
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

                    if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
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
                    Interlocked.Increment(ref analysisdone);
                    if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }

                    if (aqevent.IsClear)
                    {
                        var clearidx = aqevent.Index - 1 - RetainAnalysisPerClear;
                        if (clearidx >= 0)
                        {
                            DataSet.Remove(analysistime[clearidx]);
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
            bool isvisualized = false;
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
                                if (!isvisualized)
                                {
                                    isvisualized = true;
                                }
                            }
                            break;
                        case VisualizeMode.All:
                            while (a.ResultVisualizeQueue.TryDequeue(out r))
                            {
                                a.Visualizer.Visualize(r);
                                if (!isvisualized)
                                {
                                    isvisualized = true;
                                }
                            }
                            break;
                        case VisualizeMode.Last:
                            while (a.ResultVisualizeQueue.TryDequeue(out r) && a.ResultVisualizeQueue.IsEmpty)
                            {
                                a.Visualizer.Visualize(r);
                                if (!isvisualized)
                                {
                                    isvisualized = true;
                                }
                            }
                            break;
                    }
                }
            }
            if (isvisualized)
            {
                Interlocked.Increment(ref visualizationdone);
            }
        }

        public void Restart()
        {
            lock (apilock)
            {
                Stop();
                if (signal != null)
                {
                    signal.Restart(true);
                }
                condtest.Clear();
                analysisqueue = new ConcurrentQueue<AnalysisEvent>();
                analysistime.Clear();
                analysiseventidx = 0;
                Interlocked.Exchange(ref analysisdone, 0);
                Interlocked.Exchange(ref visualizationdone, 0);
                DataSet.Reset();
                foreach (var aid in idanalyzer.Keys.ToArray())
                {
                    IAnalyzer a;
                    if (idanalyzer.TryGetValue(aid, out a) && a != null)
                    {
                        a.Reset();
                    }
                }
                Start();
            }
        }
    }
}