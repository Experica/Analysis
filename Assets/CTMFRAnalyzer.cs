/*
CTMFRAnalyzer.cs is part of the Experica.
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
using System.Linq;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Collections.Concurrent;
using System.Threading;
using System;
using MathNet.Numerics.Statistics;

namespace Experica.Analysis
{
    public class CTMFRAnalyzer : IAnalyzer
    {
        int disposecount = 0;
        readonly object apilock = new object();
        SignalDescription signaldescription;
        IVisualizer visualizer;
        IController controller;
        ConcurrentQueue<IResult> resultvisualizequeue = new ConcurrentQueue<IResult>();
        IResult result;

        public CTMFRAnalyzer(SignalDescription s) : this(s, new D2Visualizer(), new NoneController()) { }

        public CTMFRAnalyzer(SignalDescription s, IVisualizer v, IController c)
        {
            signaldescription = s;
            visualizer = v;
            controller = c;
            Reset();
        }

        ~CTMFRAnalyzer()
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
            lock (apilock)
            {
                if (visualizer != null)
                {
                    visualizer.Dispose();
                    visualizer = null;
                }
                if (controller != null)
                {
                    controller.Dispose();
                    controller = null;
                }
            }
        }

        public void Reset()
        {
            lock (apilock)
            {
                result = null;
                resultvisualizequeue = new ConcurrentQueue<IResult>();
                if (controller != null)
                {
                    controller.Reset();
                }
                if (visualizer != null)
                {
                    visualizer.Reset();
                }
            }
        }

        public Guid ID { get; } = Guid.NewGuid();

        public SignalDescription SignalDescription
        {
            get { lock (apilock) { return signaldescription; } }
            set { lock (apilock) { signaldescription = value; } }
        }

        public IVisualizer Visualizer
        {
            get { lock (apilock) { return visualizer; } }
            set { lock (apilock) { visualizer = value; } }
        }

        public IController Controller
        {
            get { lock (apilock) { return controller; } }
            set { lock (apilock) { controller = value; } }
        }

        public ConcurrentQueue<IResult> ResultVisualizeQueue { get { lock (apilock) { return resultvisualizequeue; } } }

        public IResult Result { get { lock (apilock) { return result; } } }

        public void Analyze(DataSet dataset)
        {
            lock (apilock)
            {
                if (result == null)
                {
                    result = new CTMFRResult(SignalDescription.Channel, dataset.Ex.ID, dataset);
                }
                var condindex = dataset.CondIndex;
                var on = dataset.CondOnTime;
                var off = dataset.CondOffTime;
                if (condindex == null || condindex.Count != on.Count) return;
                var nct = condindex.Count;
                var uctmfr = result.UnitCondTestResponse;
                if (dataset.IsData(SignalDescription.Channel, SignalDescription.Type))
                {
                    var st = dataset.Spike[SignalDescription.Channel];
                    var uid = dataset.UID[SignalDescription.Channel];
                    var uuid = uid.Distinct().ToArray();
                    foreach (var u in uuid)
                    {
                        var ust = st.GetUnitSpike(uid, u);
                        if (!uctmfr.ContainsKey(u))
                        {
                            uctmfr[u] = new List<double>();
                        }
                        var nr = uctmfr[u].Count;
                        for (var i = nr; i < nct; i++)
                        {
                            uctmfr[u].Add(ust.MFR(on[i], off[i]));
                        }
                    }
                    resultvisualizequeue.Enqueue(result.Copy());
                }
            }
        }
    }

    public class CTMFRResult : IResult
    {
        public CTMFRResult(int signalchannel, string experimentid, DataSet dataset)
        {
            SignalChannel = signalchannel;
            ExperimentID = experimentid;
            DataSet = dataset;
        }

        public IResult Copy()
        {
            var clone = (CTMFRResult)MemberwiseClone();
            clone.ExperimentID = string.Copy(ExperimentID);
            clone.UnitCondTestResponse = UnitCondTestResponse.ToDictionary(i => i.Key, i => i.Value.ToList());
            return clone;
        }

        public int SignalChannel { get; }

        public string ExperimentID { get; private set; }

        public DataSet DataSet { get; }

        public Dictionary<int, List<double>> UnitCondTestResponse { get; private set; } = new Dictionary<int, List<double>>();
    }
}
