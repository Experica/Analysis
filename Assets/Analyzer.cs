/*
Analyzer.cs is part of the VLAB project.
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
using System.Linq;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Collections.Concurrent;
using System.Threading;
using VLab;
using System;
using MathNet.Numerics.Statistics;

namespace VLabAnalysis
{
    public interface IAnalyzer : IDisposable
    {
        int ID { get; set; }
        Signal Signal { get; set; }
        void Analyze(DataSet dataset);
        IVisualizer Visualizer { get; set; }
        IController Controller { get; set; }
        ConcurrentQueue<IResult> ResultVisualizeQueue { get; }
        IResult Result { get; }
        void Reset();
    }

    public class MFRAnalyzer : IAnalyzer
    {
        int disposecount = 0;
        int id;
        Signal signal;
        IVisualizer visualizer;
        IController controller;
        ConcurrentQueue<IResult> resultvisualizequeue = new ConcurrentQueue<IResult>();
        IResult result;

        public MFRAnalyzer(Signal s) : this(s, new D2Visualizer(), new OPTController()) { }

        public MFRAnalyzer(Signal s, IVisualizer v, IController c)
        {
            signal = s;
            visualizer = v;
            controller = c;
            Reset();
        }

        ~MFRAnalyzer()
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
            if (visualizer != null)
            {
                visualizer.Dispose();
            }
            if (controller != null)
            {
                controller.Dispose();
            }
        }

        public int ID
        {
            get { return id; }
            set { id = value; }
        }

        public Signal Signal
        {
            get { return signal; }
            set { signal = value; }
        }

        public IVisualizer Visualizer
        {
            get { return visualizer; }
            set { visualizer = value; }
        }

        public IController Controller
        {
            get { return controller; }
            set { controller = value; }
        }

        public ConcurrentQueue<IResult> ResultVisualizeQueue
        {
            get { return resultvisualizequeue; }
        }

        public IResult Result
        {
            get { return result; }
        }

        public void Reset()
        {
            result = null;
            IResult r;
            while (resultvisualizequeue.TryDequeue(out r))
            {
            }
            if (controller != null)
            {
                controller.Reset();
            }
            if (visualizer != null)
            {
                visualizer.Reset();
            }
        }

        public void Analyze(DataSet dataset)
        {
            if (result == null)
            {
                result = new MFRResult(Signal.Channel, dataset.Ex.ID, dataset.CondIndex, dataset.CondRepeat, dataset.CondTestCond, dataset.Ex.EnvParam);
            }
            var nct = dataset.CondIndex.Count;
            var nr = result.CondResponse.Count;
            if (nr >= nct) return;
            if (dataset.IsData(Signal.Channel - 1, Signal.Type))
            {
                var st = dataset.Spike[Signal.Channel - 1];
                var uid = dataset.UID[Signal.Channel - 1];
                var uuid = uid.Distinct().ToArray();
                var onoff = dataset.CondOnOffTime;
                for (var i = nr; i < nct; i++)
                {
                    var ur = new Dictionary<int, double>();
                    foreach (var u in uuid)
                    {
                        ur[u] = st.GetUnitSpike(uid, u).MFR(onoff[0][i], onoff[1][i]);
                    }
                    result.CondResponse.Add(ur);
                }
            }
            else
            {
                for (var i = nr; i < nct; i++)
                {
                    // null for no spikes at all in each condition test
                    result.CondResponse.Add(null);
                }
            }
            resultvisualizequeue.Enqueue(result.DeepCopy());
        }
    }

    public interface IResult
    {
        IResult DeepCopy();
        int SignalID { get; }
        string ExperimentID { get; }
        List<Dictionary<int, double>> CondResponse { get; }
        List<int> CondIndex { get; }
        List<int> CondRepeat { get; }
        Dictionary<string, List<object>> CondTestCond { get; }
        Dictionary<string, object> EnvParam { get; }
    }

    public class MFRResult : IResult
    {
        int signalid;
        string experimentid;
        List<int> condindex;
        List<int> condrepeat;
        Dictionary<string, List<object>> condtestcond;
        Dictionary<string, object> envparam;
        List<Dictionary<int, double>> condmfr = new List<Dictionary<int, double>>();

        public MFRResult(int signalid, string experimentid, List<int> condindex, List<int> condrepeat,
            Dictionary<string, List<object>> condtestcond, Dictionary<string, object> envparam)
        {
            this.signalid = signalid;
            this.experimentid = experimentid;
            this.condindex = condindex;
            this.condrepeat = condrepeat;
            this.condtestcond = condtestcond;
            this.envparam = envparam;
        }

        public IResult DeepCopy()
        {
            var clone = (MFRResult)MemberwiseClone();
            clone.experimentid = string.Copy(experimentid);
            var ccondmfr = new List<Dictionary<int, double>>();
            foreach (var d in condmfr)
            {
                var cd = new Dictionary<int, double>();
                foreach (var u in d.Keys)
                {
                    cd[u] = d[u];
                }
                ccondmfr.Add(cd);
            }
            clone.condmfr = ccondmfr;
            return clone;
        }

        public int SignalID
        { get { return signalid; } }

        public string ExperimentID
        { get { return experimentid; } }

        public List<Dictionary<int, double>> CondResponse
        { get { return condmfr; } }

        public List<int> CondIndex
        { get { return condindex; } }

        public List<int> CondRepeat
        { get { return condrepeat; } }

        public Dictionary<string, List<object>> CondTestCond
        { get { return condtestcond; } }

        public Dictionary<string, object> EnvParam
        { get { return envparam; } }
    }

}
