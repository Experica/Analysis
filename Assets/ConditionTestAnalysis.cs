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

namespace VLabAnalysis
{
    /// <summary>
    /// Analysis Engine for Condition Test Experiment. Use internal analysis thread, triggered by analysis event.
    /// </summary>
    public class ConditionTestAnalysis : IAnalysis
    {
        int _disposecount = 0;
        int _gotothreadevent = 0;

        bool _analyzing = false;
        public bool IsAnalyzing { get { lock (apilock) { return _analyzing; } } }
        int _analysiseventcount = 0;
        public int AnalysisEventCount { get { lock (apilock) { return _analysiseventcount; } } }
        int _analysisdonecount = 0;
        public int AnalysisDoneCount { get { return Interlocked.CompareExchange(ref _analysisdonecount, 0, -255); } }
        int _visualizationdonecount = 0;
        public int VisualizationDoneCount { get { return Interlocked.CompareExchange(ref _visualizationdonecount, 0, -255); } }
        int _cleardataperanalysis = 1;
        public int ClearDataPerAnalysis
        {
            get { lock (apilock) { return _cleardataperanalysis; } }
            set { lock (apilock) { _cleardataperanalysis = Math.Max(0, value); } }
        }
        int _retainanalysisperclear = 1;
        public int RetainAnalysisPerClear
        {
            get { lock (apilock) { return _retainanalysisperclear; } }
            set { lock (apilock) { _retainanalysisperclear = Math.Max(0, value); } }
        }
        int _sleepduration = 1;
        public int SleepDuration
        {
            get { lock (apilock) { return _sleepduration; } }
            set { lock (apilock) { _sleepduration = Math.Max(1, value); } }
        }
        bool _isexperimentend = false;
        public bool IsExperimentEnd
        {
            get { lock (apilock) { return _isexperimentend; } }
            set { lock (apilock) { _isexperimentend = value; } }
        }

        ISignal _signal;
        public ISignal Signal
        {
            get { lock (apilock) { return _signal; } }
            set { lock (apilock) { _signal = value; } }
        }
        public VLADataSet DataSet { get; } = new VLADataSet();
        public ConcurrentDictionary<int, ConcurrentDictionary<Guid, IAnalyzer>> Analyzers { get; } = new ConcurrentDictionary<int, ConcurrentDictionary<Guid, IAnalyzer>>();
        ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>> condtest = new ConcurrentDictionary<CONDTESTPARAM, ConcurrentQueue<object>>();
        ConcurrentQueue<AnalysisEvent> analysisqueue = new ConcurrentQueue<AnalysisEvent>();
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
            Stop();
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
                _analysiseventcount++;
                bool iscleardata = false;
                if (_cleardataperanalysis > 0)
                {
                    iscleardata = _analysiseventcount % _cleardataperanalysis == 0;
                }
                analysisqueue.Enqueue(new AnalysisEvent(_analysiseventcount - 1, iscleardata, time));
            }
        }

        public void ExperimentEndEnqueue()
        {
            lock (apilock)
            {
                _analysiseventcount++;
                _signal?.Stop(true);
                analysisqueue.Enqueue(new AnalysisEvent(-1));
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
                    var ra = new ConcurrentDictionary<Guid, IAnalyzer> { [analyzer.ID] = analyzer };
                    Analyzers[rank] = ra;
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
                        if (ra.TryRemove(id, out a) && !_analyzing)
                        {
                            a?.Dispose();
                        }
                        break;
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
                    analysisthread.Name = "ConditionTestAnalysis";
                    analysisthreadevent.Set();
                    _analyzing = true;
                    analysisthread.Start();
                }
                else
                {
                    if (!_analyzing)
                    {
                        _analyzing = true;
                        analysisthreadevent.Set();
                    }
                }
            }
        }

        public void Stop()
        {
            lock (apilock)
            {
                if (analysisthread != null && _analyzing)
                {
                    // lock the analysis thread event region and set thread stop and jump to stop point flags
                    lock (gotoeventlock)
                    {
                        analysisthreadevent.Reset();
                        Interlocked.Exchange(ref _gotothreadevent, 1);
                    }
                    // wait until thread jumped to thread stop point
                    while (true)
                    {
                        // just check the value, never change it so it's compared with an arbitrary value(here -255) other than 0 or 1.
                        if (0 == Interlocked.CompareExchange(ref _gotothreadevent, 0, -255))
                        {
                            _analyzing = false;
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
                analysisqueue = new ConcurrentQueue<AnalysisEvent>();
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
                _analysiseventcount = 0;
                Interlocked.Exchange(ref _analysisdonecount, 0);
                Interlocked.Exchange(ref _visualizationdonecount, 0);
                Start();
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
                isaqevent = analysisqueue.TryDequeue(out aqevent);
                if (isaqevent)
                {
                    // experiment end event
                    if (aqevent.Index < 0)
                    {
                        Interlocked.Increment(ref _analysisdonecount);
                        IsExperimentEnd = true;
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
                    analysistime[aqevent.Index] = aqeventanalysistime;
                    if (Signal != null)
                    {
                        // Wait the corresponding signal data to be buffered before analysis
                        double slept = 0;
                        while (Signal.Time <= aqeventanalysistime || slept <= datalatency)
                        {
                            Thread.Sleep(SleepDuration);
                            slept += SleepDuration;
                        }
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

        /// <summary>
        /// Visualize only works in the GUI thread which creates visualization at the first palce.
        /// So it should be called only by the GUI thread, and because it uses copyed result data in concurrentqueue, it needs no locking
        /// </summary>
        /// <param name="mode"></param>
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
                                    while (a.ResultVisualizeQueue.TryDequeue(out r) && a.ResultVisualizeQueue.IsEmpty)
                                    {
                                        a.Visualizer.Visualize(r);
                                        Interlocked.Increment(ref _visualizationdonecount);
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public void SaveVisualization(int width, int height, int dpi)
        {
            var datapath = DataSet.Ex.DataPath;
            var datadir = Path.GetDirectoryName(datapath);
            var dataname = Path.GetFileNameWithoutExtension(datapath);
            foreach (var ra in Analyzers.Values.ToArray())
            {
                if (ra != null)
                {
                    foreach (var a in ra.Values.ToArray())
                    {
                        if (a != null && a.Visualizer != null)
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
            }
        }
    }
}