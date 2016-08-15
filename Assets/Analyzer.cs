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

namespace VLabAnalysis
{
    public interface IAnalyzer
    {
        int ID { get; set; }
        SignalChannel SignalChannel { get; set; }
        void Analyze(DataSet dataset);
        IVisualizer Visualizer { get; set; }
        IController Controller { get; set; }
        ConcurrentQueue<IResult> ResultQueue { get; }
        IResult Result { get; }
        void Reset();
    }

    public class MFRAnalyzer : IAnalyzer
    {
        int id;
        SignalChannel sigch;
        IVisualizer visualizer;
        IController controller;

        ConcurrentQueue<IResult> resultqueue = new ConcurrentQueue<IResult>();
        MFRResult result;


        public MFRAnalyzer(SignalChannel sc) : this(sc, new LineVisualizer(), new OptimalController()) { }

        public MFRAnalyzer(SignalChannel sc, IVisualizer ver, IController col)
        {
            sigch = sc;
            visualizer = ver;
            controller = col;
        }

        public SignalChannel SignalChannel
        {
            get { return sigch; }
            set { sigch = value; }
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

        public ConcurrentQueue<IResult> ResultQueue
        {
            get { return resultqueue; }
        }

        public IResult Result
        {
            get { return result; }
        }

        public void Reset()
        {
            result = null;
            IResult r;
            while (resultqueue.TryDequeue(out r))
            {
            }
            if(visualizer!=null)
            {
                visualizer.Reset();
            }
            if(controller!=null)
            {
                controller.Reset();
            }
        }

        void Init(DataSet dataset)
        {
            result = new MFRResult(SignalChannel.ElectrodID, dataset.Ex.ID);
            result.Cond = dataset.Ex.Cond;
        }

        public void Analyze(DataSet dataset)
        {
            if (result == null)
            {
                Init(dataset);
            }
            if (Prepare(dataset))
            {
                var st = dataset.spike[SignalChannel.ElectrodID - 1];
                for (var i = 0; i < dataset.CondIndex.Count; i++)
                {
                    var c = dataset.CondIndex[i];
                    var t1 = dataset.CondState[i].FindStateTime(CONDSTATE.COND.ToString()) + dataset.FirstDigitalEventTime;
                    var t2 = dataset.CondState[i].FindStateTime(CONDSTATE.SUFICI.ToString()) + dataset.FirstDigitalEventTime;
                    if (result.CondResponse.ContainsKey(c))
                    {
                        result.CondResponse[c].Add(st.MFR(t1, t2));
                    }
                    else
                    {
                        result.CondResponse[c] = new List<double> { st.MFR(t1, t2) };
                    }
                }
                resultqueue.Enqueue(result.Clone());
            }
            else
            {
                foreach (var c in dataset.CondIndex)
                {
                    if (result.CondResponse.ContainsKey(c))
                    {
                        result.CondResponse[c].Add(0.0);
                    }
                    else
                    {
                        result.CondResponse[c] = new List<double> { 0.0 };
                    }
                }
                resultqueue.Enqueue(result.Clone());
            }
        }

        bool Prepare(DataSet dataset)
        {
            if (sigch.SignalType == SIGNALTYPE.Spike)
            {
                if (dataset.IsData(sigch.ElectrodID, sigch.SignalType))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            return false;
        }
    }

    public interface IResult
    {
        IResult Clone();
        int ElectrodID { get; }
        string ExperimentID { get; }
        Dictionary<int, List<double>> CondResponse { get; }
        Dictionary<string, List<object>> Cond { get; set; }
        ResultType Type { get; }
    }

    public enum ResultType
    {
        MFRResult
    }

    public class MFRResult : IResult
    {
        int electrodid;
        public string experimentid;

        Dictionary<int, List<double>> mfr = new Dictionary<int, List<double>>();
        Dictionary<string, List<object>> cond = new Dictionary<string, List<object>>();


        public MFRResult(int electrodid = 1, string experimentid = "")
        {
            this.electrodid = electrodid;
            this.experimentid = experimentid;
        }

        public IResult Clone()
        {
            var copy = (MFRResult)MemberwiseClone();
            var copymfr = new Dictionary<int, List<double>>();
            foreach (var i in mfr.Keys)
            {
                copymfr[i] = mfr[i].ToList();
            }
            copy.mfr = copymfr;
            return copy;
        }

        public Dictionary<int, List<double>> CondResponse
        { get { return mfr; } }

        public Dictionary<string, List<object>> Cond
        { get { return cond; } set { cond = value; } }

        public int ElectrodID
        { get { return electrodid; } }

        public string ExperimentID
        { get { return experimentid; } }

        public ResultType Type
        { get { return ResultType.MFRResult; } }
    }

}
