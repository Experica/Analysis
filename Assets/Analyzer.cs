/*
Analyzer.cs is part of the VLAB project.
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
        ConcurrentQueue<IVisualizeResult> VisualizeResultQueue { get; }
        IResult Result { get; }
        void Reset();
    }

    public class MFRAnalyzer : IAnalyzer
    {
        bool disposed;
        int id;
        Signal signal;
        IVisualizer visualizer;
        IController controller;
        ConcurrentQueue<IVisualizeResult> visualizeresultqueue = new ConcurrentQueue<IVisualizeResult>();
        IResult result;
        Dictionary<string, List<bool>> factorvaluedim = new Dictionary<string, List<bool>>();
        Dictionary<string, List<string>> factorunit = new Dictionary<string, List<string>>();


        public MFRAnalyzer(Signal s) : this(s, new D2Visualizer(), new OPTController()) { }

        public MFRAnalyzer(Signal s, IVisualizer v, IController c)
        {
            signal = s;
            visualizer = v;
            controller = c;
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
            if (!disposed)
            {
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
                disposed = true;
            }
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

        public int ID
        {
            get { return id; }
            set { id = value; }
        }

        public ConcurrentQueue<IVisualizeResult> VisualizeResultQueue
        {
            get { return visualizeresultqueue; }
        }

        public IResult Result
        {
            get { return result; }
        }

        public void Reset()
        {
            result = null;
            IVisualizeResult r;
            while (visualizeresultqueue.TryDequeue(out r))
            {
            }
            factorvaluedim.Clear();
            factorunit.Clear();
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
                result = new MFRResult(Signal.Channel, dataset.Ex.ID);
                result.Cond = dataset.Ex.Cond;
                foreach (var f in result.Cond.Keys)
                {
                    List<bool> valuedim;
                    factorunit[f] = f.GetFactorUnit(out valuedim, result.ExperimentID);
                    factorvaluedim[f] = valuedim;
                }
            }
            if (Prepare(dataset))
            {
                var latency = dataset.Ex.Latency;
                var timerdriftspeed = dataset.Ex.TimerDriftSpeed;
                var delay = dataset.Ex.Delay;
                var st = dataset.spike[Signal.Channel - 1];
                var uid = dataset.uid[Signal.Channel - 1];
                var uuid = uid.Distinct().ToArray();
                for (var i = 0; i < dataset.CondIndex.Count; i++)
                {
                    var ci = dataset.CondIndex[i];
                    var t1 = dataset.CondState[i].FindStateTime(CONDSTATE.COND.ToString());
                    var t2 = dataset.CondState[i].FindStateTime(CONDSTATE.SUFICI.ToString());
                    t1 = t1 + t1 * timerdriftspeed + latency + dataset.VLabZeroTime + delay;
                    t2 = t2 + t2 * timerdriftspeed + latency + dataset.VLabZeroTime + delay;
                    if (!result.CondResponse.ContainsKey(ci))
                    {
                        result.CondResponse.Add(ci, new Dictionary<int, List<double>>());
                    }
                    foreach (var u in uuid)
                    {
                        if (!result.CondResponse[ci].ContainsKey(u))
                        {
                            result.CondResponse[ci].Add(u, new List<double>());
                        }
                        result.CondResponse[ci][u].Add(st.GetUnitSpike(uid, u).MFR(t1, t2));
                    }
                    result.CondRepeat[ci] = dataset.CondRepeat[i];
                }
                var fn = result.Cond.Count;
                if (fn == 1)
                {
                    var fl = result.Cond.ElementAt(0);
                    var valuedim = factorvaluedim[fl.Key];
                    var valuedimidx = Enumerable.Range(0, valuedim.Count).Where(i => valuedim[i] == true).ToList();
                    var vdn = valuedimidx.Count;
                    var x = result.CondResponse.Keys.Select(i => fl.Value[i]).GetFactorLevel<double>(valuedim);
                    var y = new Dictionary<int, List<double>>(); var yse = new Dictionary<int, List<double>>();
                    var alluuid = result.CondResponse.Values.SelectMany(i => i.Keys).Distinct().ToList();
                    foreach (var c in result.CondResponse.Keys)
                    {
                        var v = result.CondResponse[c];
                        var r = result.CondRepeat[c];
                        foreach (var u in alluuid)
                        {
                            if (!y.ContainsKey(u))
                            {
                                y.Add(u, new List<double>());
                                yse.Add(u, new List<double>());
                            }
                            var m = 0.0; var se = 0.0;
                            if (v.ContainsKey(u))
                            {
                                var uv = v[u]; var uvn = uv.Count;
                                if (uvn < r)
                                {
                                    uv.AddRange(new double[r - uvn]);
                                }
                                m = uv.Mean(); se = uv.SEM();
                            }
                            y[u].Add(m);
                            yse[u].Add(se);
                        }
                    }
                    if (vdn == 1)
                    {
                        var x1 = x[valuedimidx[0]]; var n1 = x1.Length; var si = Enumerable.Range(0, n1).ToArray(); Array.Sort(x1, si);
                        var sy = new Dictionary<int, double[,]>(); var syse = new Dictionary<int, double[,]>();
                        foreach (var u in y.Keys)
                        {
                            sy[u] = new double[1, n1]; syse[u] = new double[1, n1];
                            for (var i = 0; i < n1; i++)
                            {
                                sy[u][0, i] = y[u][si[i]];
                                syse[u][0, i] = yse[u][si[i]];
                            }
                        }
                        result.X = new List<double[]> { x1 }; result.Y = sy; result.YSE = syse;
                        result.XUnit = new List<string> { factorunit[fl.Key][valuedimidx[0]] };
                        result.YUnit = result.Type.GetResponseUnit();
                    }
                    else if (vdn == 2)
                    {
                        var x1 = x[valuedimidx[0]]; var x2 = x[valuedimidx[1]];
                        var ux1 = x1.Distinct().ToList(); var ux2 = x2.Distinct().ToList(); ux1.Sort(); ux2.Sort(); var n1 = ux1.Count(); var n2 = ux2.Count();
                        var sy = new Dictionary<int, double[,]>(); var syse = new Dictionary<int, double[,]>();
                        foreach (var u in y.Keys)
                        {
                            sy[u] = new double[n1, n2]; syse[u] = new double[n1, n2];
                            for (var i = 0; i < x1.Length; i++)
                            {
                                sy[u][ux1.IndexOf(x1[i]), ux2.IndexOf(x2[i])] = y[u][i];
                                syse[u][ux1.IndexOf(x1[i]), ux2.IndexOf(x2[i])] = yse[u][i];
                            }
                        }
                        result.X = new List<double[]> { ux1.ToArray(), ux2.ToArray() }; result.Y = sy; result.YSE = syse;
                        result.XUnit = new List<string> { factorunit[fl.Key][valuedimidx[0]], factorunit[fl.Key][valuedimidx[1]] };
                        result.YUnit = result.Type.GetResponseUnit();
                    }
                    else
                    {

                    }
                }
                else if (fn == 2)
                {

                }
                else
                {

                }
                visualizeresultqueue.Enqueue(result.GetVisualizeResult(VisualizeResultType.D2VisualizeResult));
            }
        }

        bool Prepare(DataSet dataset)
        {
            if (signal.Type == SignalType.Spike)
            {
                return true;
                return dataset.IsData(signal.Channel, signal.Type);
            }
            return false;
        }

    }

    public interface IResult
    {
        IResult Clone();
        IVisualizeResult GetVisualizeResult(VisualizeResultType type);
        int SignalID { get; }
        string ExperimentID { get; }
        Dictionary<int, Dictionary<int, List<double>>> CondResponse { get; }
        Dictionary<int, int> CondRepeat { get; }
        Dictionary<string, List<object>> Cond { get; set; }
        List<double[]> X { get; set; }
        Dictionary<int, double[,]> Y { get; set; }
        Dictionary<int, double[,]> YSE { get; set; }
        List<string> XUnit { get; set; }
        string YUnit { get; set; }
        ResultType Type { get; }
    }

    public enum ResultType
    {
        MFRResult
    }

    public class MFRResult : IResult
    {
        int signalid;
        string experimentid;
        Dictionary<int, Dictionary<int, List<double>>> mfr = new Dictionary<int, Dictionary<int, List<double>>>();
        Dictionary<int, int> condrepeat = new Dictionary<int, int>();
        Dictionary<string, List<object>> cond = new Dictionary<string, List<object>>();
        List<double[]> x = new List<double[]>();
        Dictionary<int, double[,]> y = new Dictionary<int, double[,]>();
        Dictionary<int, double[,]> yse = new Dictionary<int, double[,]>();
        List<string> xunit = new List<string>();
        string yunit = "Response (spike/s)";


        public MFRResult(int signalid = 1, string experimentid = "")
        {
            this.signalid = signalid;
            this.experimentid = experimentid;
        }

        public IResult Clone()
        {
            var clone = (MFRResult)MemberwiseClone();
            var cmfr = new Dictionary<int, Dictionary<int, List<double>>>(); var ccondrepeat = new Dictionary<int, int>();
            foreach (var c in mfr.Keys)
            {
                var v = new Dictionary<int, List<double>>();
                foreach (var u in mfr[c].Keys)
                {
                    v[u] = mfr[c][u].ToList();
                }
                cmfr[c] = v;
                ccondrepeat[c] = condrepeat[c];
            }
            clone.mfr = cmfr; clone.condrepeat = ccondrepeat;
            var cx = new List<double[]>(); var cxunit = new List<string>();
            var cy = new Dictionary<int, double[,]>(); var cyse = new Dictionary<int, double[,]>();
            for (var i = 0; i < x.Count; i++)
            {
                cx.Add(x[i].ToArray());
                cxunit.Add(string.Copy(xunit[i]));
            }
            foreach (var u in y.Keys)
            {
                cy.Add(u, (double[,])y[u].Clone());
                cyse.Add(u, (double[,])yse[u].Clone());
            }
            clone.x = cx; clone.xunit = cxunit; clone.yunit = string.Copy(yunit); clone.y = cy; clone.yse = cyse;
            return clone;
        }

        public IVisualizeResult GetVisualizeResult(VisualizeResultType type)
        {
            switch (type)
            {
                default:
                    var vr = new D2VisualizeResult(signalid, experimentid);
                    for (var i = 0; i < x.Count; i++)
                    {
                        vr.X.Add(x[i].ToArray());
                        vr.XUnit.Add(string.Copy(xunit[i]));
                    }
                    foreach (var u in y.Keys)
                    {
                        vr.Y.Add(u, (double[,])y[u].Clone());
                        vr.YSE.Add(u, (double[,])yse[u].Clone());
                    }
                    vr.YUnit = string.Copy(yunit);
                    return vr;
            }
        }

        public Dictionary<int, Dictionary<int, List<double>>> CondResponse
        { get { return mfr; } }

        public Dictionary<string, List<object>> Cond
        { get { return cond; } set { cond = value; } }

        public int SignalID
        { get { return signalid; } }

        public string ExperimentID
        { get { return experimentid; } }

        public ResultType Type
        { get { return ResultType.MFRResult; } }

        public List<double[]> X
        { get { return x; } set { x = value; } }

        public Dictionary<int, double[,]> Y
        { get { return y; } set { y = value; } }

        public Dictionary<int, double[,]> YSE
        { get { return yse; } set { yse = value; } }

        public List<string> XUnit
        { get { return xunit; } set { xunit = value; } }

        public string YUnit
        { get { return yunit; } set { yunit = value; } }

        public Dictionary<int, int> CondRepeat
        { get { return condrepeat; } }
    }

    public interface IVisualizeResult
    {
        int SignalID { get; }
        string ExperimentID { get; }
        List<double[]> X { get; }
        Dictionary<int, double[,]> Y { get; }
        Dictionary<int, double[,]> YSE { get; }
        List<string> XUnit { get; }
        string YUnit { get; set; }
        VisualizeResultType Type { get; }
    }

    public enum VisualizeResultType
    {
        D2VisualizeResult
    }

    public class D2VisualizeResult : IVisualizeResult
    {
        int signalid;
        string experimentid;
        List<double[]> x = new List<double[]>();
        Dictionary<int, double[,]> y = new Dictionary<int, double[,]>();
        Dictionary<int, double[,]> yse = new Dictionary<int, double[,]>();
        List<string> xunit = new List<string>();
        string yunit = "";

        public D2VisualizeResult(int signalid = 1, string experimentid = "")
        {
            this.signalid = signalid;
            this.experimentid = experimentid;
        }

        public string ExperimentID
        { get { return experimentid; } }

        public int SignalID
        { get { return signalid; } }

        public VisualizeResultType Type
        { get { return VisualizeResultType.D2VisualizeResult; } }

        public List<string> XUnit
        { get { return xunit; } }

        public string YUnit
        { get { return yunit; } set { yunit = value; } }

        public List<double[]> X
        { get { return x; } }

        public Dictionary<int, double[,]> Y
        { get { return y; } }

        public Dictionary<int, double[,]> YSE
        { get { return yse; } }
    }

}
