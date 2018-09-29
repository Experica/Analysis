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
using MathNet.Numerics;

namespace IExSys.Analysis
{
    /// <summary>
    /// Analysis Engine for Condition Test Experiment. Use internal analysis thread, triggered by analysis event.
    /// </summary>
    public class ConditionTestAnalysis : IAnalysis
    {
        int _disposecount = 0;
        int _gotothreadevent = 0;

        bool _isanalyzing;
        public bool IsAnalyzing { get { lock (apilock) { return _isanalyzing; } } }
        int _analysiseventcount = 0;
        public int AnalysisEventCount { get { return Interlocked.CompareExchange(ref _analysiseventcount, 0, int.MinValue); } }
        int _analysisdonecount = 0;
        public int AnalysisDoneCount { get { return Interlocked.CompareExchange(ref _analysisdonecount, 0, int.MinValue); } }
        int _visualizationdonecount = 0;
        public int VisualizationDoneCount { get { return Interlocked.CompareExchange(ref _visualizationdonecount, 0, int.MinValue); } }
        // 0: in analysis, 1: analysis done, 2: visualization done
        int _experimentanalysisstage;
        public int ExperimentAnalysisStage
        {
            get { return Interlocked.CompareExchange(ref _experimentanalysisstage, 0, int.MinValue); }
            set { Interlocked.Exchange(ref _experimentanalysisstage, value); }
        }
        int _cleardataperanalysis = 1;
        public int ClearDataPerAnalysis
        {
            get { return Interlocked.CompareExchange(ref _cleardataperanalysis, 1, int.MinValue); }
            set { Interlocked.Exchange(ref _cleardataperanalysis, value); }
        }
        int _retainanalysisperclear = 1;
        public int RetainAnalysisPerClear
        {
            get { return Interlocked.CompareExchange(ref _retainanalysisperclear, 1, int.MinValue); }
            set { Interlocked.Exchange(ref _retainanalysisperclear, value); }
        }
        int _sleepduration = 1;
        public int SleepDuration
        {
            get { return Interlocked.CompareExchange(ref _sleepduration, 1, int.MinValue); }
            set { Interlocked.Exchange(ref _sleepduration, value); }
        }

        ISignal _signal;
        public ISignal Signal
        {
            get { lock (apilock) { return _signal; } }
            set { lock (apilock) { _signal = value; } }
        }
        public DataSet DataSet { get; } = new DataSet();
        public ConcurrentDictionary<int, ConcurrentDictionary<Guid, IAnalyzer>> Analyzers { get; } = new ConcurrentDictionary<int, ConcurrentDictionary<Guid, IAnalyzer>>();
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        ConcurrentQueue<AnalysisEvent> analysiseventqueue = new ConcurrentQueue<AnalysisEvent>();
        ConcurrentDictionary<int, double> analysistime = new ConcurrentDictionary<int, double>();
        Thread analysisthread;
        ManualResetEvent analysisthreadevent = new ManualResetEvent(false);
        readonly object apilock = new object();
        readonly object gotoeventlock = new object();


        public ConditionTestAnalysis(int cleardataperanalysis = 1, int retainanalysisperclear = 1, int sleepduration = 1)
        {
            _cleardataperanalysis = Math.Max(0, cleardataperanalysis);
            _retainanalysisperclear = Math.Max(0, retainanalysisperclear);
            _sleepduration = Math.Max(1, sleepduration);
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
            if (1 == Interlocked.Exchange(ref _disposecount, 1))
            {
                return;
            }
            lock (apilock)
            {
                Stop();
                analysisthread?.Abort();
                analysisthreadevent?.Dispose();
                Signal?.Dispose();
                foreach (var ra in Analyzers.Values.ToArray())
                {
                    if (ra != null)
                    {
                        foreach (var a in ra.Values.ToArray())
                        {
                            a?.Dispose();
                        }
                    }
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
                Interlocked.Increment(ref _analysiseventcount);
                bool iscleardata = false; int cleardataperanalysis = ClearDataPerAnalysis;
                if (cleardataperanalysis > 0)
                {
                    iscleardata = _analysiseventcount % cleardataperanalysis == 0;
                }
                analysiseventqueue.Enqueue(new AnalysisEvent(_analysiseventcount - 1, iscleardata, time));
            }
        }

        public void ExperimentEndEnqueue()
        {
            lock (apilock)
            {
                Interlocked.Increment(ref _analysiseventcount);
                _signal?.Stop(true);
                analysiseventqueue.Enqueue(new AnalysisEvent(-1));
            }
        }

        public void AddAnalyzer(IAnalyzer analyzer, int rank = 0)
        {
            lock (apilock)
            {
                if (Analyzers.ContainsKey(rank))
                {
                    Analyzers[rank][analyzer.ID] = analyzer;
                }
                else
                {
                    Analyzers[rank] = new ConcurrentDictionary<Guid, IAnalyzer> { [analyzer.ID] = analyzer };
                }
            }
        }

        public void RemoveAnalyzer(Guid id)
        {
            lock (apilock)
            {
                IAnalyzer a;
                foreach (var ra in Analyzers.Values.ToArray())
                {
                    if (ra != null && ra.ContainsKey(id))
                    {
                        if (ra.TryRemove(id, out a))
                        {
                            if (!_isanalyzing)
                            {
                                a?.Dispose();
                            }
                            break;
                        }
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
                    analysisthread = new Thread(ProcessAnalysisQueue)
                    {
                        Name = "ConditionTestAnalysis"
                    };
                    analysisthreadevent.Set();
                    _isanalyzing = true;
                    analysisthread.Start();
                }
                else
                {
                    if (!_isanalyzing)
                    {
                        _isanalyzing = true;
                        analysisthreadevent.Set();
                    }
                }
            }
        }

        public void Stop()
        {
            lock (apilock)
            {
                if (analysisthread != null && _isanalyzing)
                {
                    // lock the analysis threadevent region and set thread stop and jump flags
                    lock (gotoeventlock)
                    {
                        analysisthreadevent.Reset();
                        Interlocked.Exchange(ref _gotothreadevent, 1);
                    }
                    // wait until thread jumped to thread stop point
                    while (true)
                    {
                        // just check the value, so it's compared with an arbitrary value other than 0 or 1.
                        if (0 == Interlocked.CompareExchange(ref _gotothreadevent, 0, int.MinValue))
                        {
                            _isanalyzing = false;
                            break;
                        }
                    }
                }
            }
        }

        public void Restart()
        {
            lock (apilock)
            {
                Stop();
                _signal?.Restart(true);
                condtest.Clear();
                analysiseventqueue = new ConcurrentQueue<AnalysisEvent>();
                analysistime.Clear();
                DataSet.Reset();
                foreach (var ra in Analyzers.Values.ToArray())
                {
                    if (ra != null)
                    {
                        foreach (var a in ra.Values.ToArray())
                        {
                            a?.Reset();
                        }
                    }
                }
                Interlocked.Exchange(ref _analysiseventcount, 0);
                Interlocked.Exchange(ref _analysisdonecount, 0);
                Interlocked.Exchange(ref _visualizationdonecount, 0);
                Interlocked.Exchange(ref _experimentanalysisstage, 0);
                Start();
            }
        }

        #region ThreadAnalysisFunction
        void ProcessAnalysisQueue()
        {
            Dictionary<int, List<double>> ospike;
            Dictionary<int, List<int>> ouid;
            List<double[,]> olfp;
            List<double> olfpstarttime;
            Dictionary<int, List<double>> odintime;
            Dictionary<int, List<int>> odinvalue;
            object ocondindex = null, oevent = null, osyncevent = null;
            AnalysisEvent aqevent; bool isaqevent;

            while (true)
            {
                ThreadEvent:
                lock (gotoeventlock)
                {
                    Interlocked.Exchange(ref _gotothreadevent, 0);
                    analysisthreadevent.WaitOne();
                }
                isaqevent = analysiseventqueue.TryDequeue(out aqevent);
                if (isaqevent)
                {
                    // experiment end event
                    if (aqevent.Index < 0)
                    {
                        Interlocked.Increment(ref _analysisdonecount);
                        ExperimentAnalysisStage = 1;
                        if (1 == Interlocked.CompareExchange(ref _gotothreadevent, 0, 1))
                        {
                            goto ThreadEvent;
                        }
                        Thread.Sleep(SleepDuration);
                        continue;
                    }
                }
                else
                {
                    if (1 == Interlocked.CompareExchange(ref _gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }
                    Thread.Sleep(SleepDuration);
                    continue;
                }
                // condtest analysis event
                if (condtest.ContainsKey(CONDTESTPARAM.CondIndex) && condtest[CONDTESTPARAM.CondIndex].TryDequeue(out ocondindex)
                    && condtest.ContainsKey(CONDTESTPARAM.Event) && condtest[CONDTESTPARAM.Event].TryDequeue(out oevent)
                    && condtest.ContainsKey(CONDTESTPARAM.SyncEvent) && condtest[CONDTESTPARAM.SyncEvent].TryDequeue(out osyncevent))
                {
                    var datalatency = DataSet.DataLatency;
                    var aqeventanalysistime = DataSet.VLabTimeToDataTime(aqevent.Time) + datalatency;
                    if (Signal != null)
                    {
                        // Wait the corresponding signal data to be buffered before analysis
                        double slept = 0;
                        while ((DataSet.IsAccurateVLabTimeZero && Signal.Time <= aqeventanalysistime) || slept <= datalatency)
                        {
                            Thread.Sleep(SleepDuration);
                            slept += SleepDuration;
                        }
                        analysistime[aqevent.Index] = Signal.Time;
                        Signal.Read(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue);
                        DataSet.Add(ospike, ouid, olfp, olfpstarttime, odintime, odinvalue,
                        (List<int>)ocondindex, (List<List<Dictionary<string, double>>>)oevent, (List<List<string>>)osyncevent);
                    }
                    else
                    {
                        DataSet.Add(null, null, null, null, null, null,
                            (List<int>)ocondindex, (List<List<Dictionary<string, double>>>)oevent, (List<List<string>>)osyncevent);
                    }

                    if (1 == Interlocked.CompareExchange(ref _gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }

                    try
                    {
                        // sequential analysis in rank order: high to low
                        var ranks = Analyzers.Keys.ToArray(); Array.Sort(ranks, (i, j) => j.CompareTo(i)); ConcurrentDictionary<Guid, IAnalyzer> ra;
                        foreach (var rank in ranks)
                        {
                            if (Analyzers.TryGetValue(rank, out ra) && ra != null)
                            {
                                foreach (var a in ra.Values.ToArray())
                                {
                                    if (a != null)
                                    {
                                        a.Analyze(DataSet);
                                        if (a.Controller != null)
                                        {
                                            a.Controller.Control(a.Result);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e) { Debug.LogException(e); }

                    Interlocked.Increment(ref _analysisdonecount);
                    if (1 == Interlocked.CompareExchange(ref _gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }

                    if (aqevent.IsClear)
                    {
                        var analysiseventclearidx = aqevent.Index - RetainAnalysisPerClear;
                        if (analysistime.ContainsKey(analysiseventclearidx))
                        {
                            DataSet.Remove(analysistime[analysiseventclearidx]);
                        }
                    }
                }
                else
                {
                    if (1 == Interlocked.CompareExchange(ref _gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }
                    Thread.Sleep(SleepDuration);
                }
            }
        }
        #endregion

        #region GUI Thread Only
        // Visualize only works in the GUI thread which creates visualization at the first palce. 
        // So it should be called only by the GUI thread, and because it uses copyed result data in concurrentqueue, it needs no locking

        public void VisualizeResults(VisualizeMode mode = VisualizeMode.First)
        {
            // sequential visualize in rank order: high to low
            var ranks = Analyzers.Keys.ToArray(); Array.Sort(ranks, (i, j) => j.CompareTo(i)); ConcurrentDictionary<Guid, IAnalyzer> ra;
            foreach (var rank in ranks)
            {
                if (Analyzers.TryGetValue(rank, out ra) && ra != null)
                {
                    foreach (var a in ra.Values.ToArray())
                    {
                        if (a != null && a.Visualizer != null)
                        {
                            IResult r;
                            switch (mode)
                            {
                                case VisualizeMode.First:
                                    if (a.ResultVisualizeQueue.TryDequeue(out r))
                                    {
                                        a.Visualizer.Visualize(r);
                                        Interlocked.Increment(ref _visualizationdonecount);
                                    }
                                    break;
                                case VisualizeMode.All:
                                    while (a.ResultVisualizeQueue.TryDequeue(out r))
                                    {
                                        a.Visualizer.Visualize(r);
                                        Interlocked.Increment(ref _visualizationdonecount);
                                    }
                                    break;
                                case VisualizeMode.Last:
                                    while (a.ResultVisualizeQueue.TryDequeue(out r))
                                    {
                                        if (a.ResultVisualizeQueue.IsEmpty)
                                        {
                                            a.Visualizer.Visualize(r);
                                            Interlocked.Increment(ref _visualizationdonecount);
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public void SaveVisualization()
        {
            var config = DataSet.Config;
            if (config != null && config.SaveVisualizationWhenExperimentAnalysisDone)
            {
                var datapath = DataSet.Ex.DataPath;
                SaveVisualization(Path.GetDirectoryName(datapath), Path.GetFileNameWithoutExtension(datapath), config.PlotExportWidth, config.PlotExportHeight, config.PlotExportDPI);
            }
        }

        public void SaveVisualization(string dir, string name, int width, int height, int dpi)
        {
            foreach (var ra in Analyzers.Values.ToArray())
            {
                if (ra != null)
                {
                    foreach (var a in ra.Values.ToArray())
                    {
                        if (a != null && a.Visualizer != null)
                        {
                            var filename = name + "_Ch" + a.SignalDescription.Channel + "_" + a.GetType().Name + "_" + a.Visualizer.GetType().Name;
                            var filedir = Path.Combine(dir, "Ch" + a.SignalDescription.Channel);
                            if (!Directory.Exists(filedir))
                            {
                                Directory.CreateDirectory(filedir);
                            }
                            a.Visualizer.Save(Path.Combine(filedir, filename), width, height, dpi);
                        }
                    }
                }
            }
        }

        public void LayoutVisualization(bool isfront, bool isalign)
        {
            if (!isfront) return;
            var cn = 5f;
            if (isalign)
            {
                cn = Mathf.Floor(Screen.currentResolution.width / DataSet.Config.VisualizerWidth);
            }
            foreach (var ra in Analyzers.Values.ToArray())
            {
                if (ra != null)
                {
                    foreach (var a in ra.Values.ToArray())
                    {
                        if (a != null && a.Visualizer != null)
                        {
                            if (isalign)
                            {
                                var ci = (a.SignalDescription.Channel - 1) % cn;
                                var ri = Mathf.Floor((a.SignalDescription.Channel - 1) / cn);
                                a.Visualizer.Position = new Vector2(ci * DataSet.Config.VisualizerWidth, ri * DataSet.Config.VisualizerHeight);
                            }
                            a.Visualizer.ShowInFront();
                        }
                    }
                }
            }
        }
        #endregion

    }
}