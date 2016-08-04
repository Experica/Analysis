// -----------------------------------------------------------------------------
// Analyzer.cs is part of the VLAB project.
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
using System.Linq;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Collections.Concurrent;
using VLab;
using System;

namespace VLabAnalysis
{
    public interface IAnalyzer
    {
        SignalChannel SignalChannel { get; set; }
        void Analysis(DataSet dataset);
        IVisualizer Visualizer { get; set; }
        IController Controller { get; set; }
        ConcurrentQueue< AnalysisResult> Results { get; }
    }

    public class mfrAnalyzer : IAnalyzer
    {
        SignalChannel sigch;
        DataSet dataset;
        IVisualizer visualizer;
        IController controller;
        List<double> spike;
        List<int> uid;
        List<int> condindex;
        Experiment ex;
        ConcurrentQueue< AnalysisResult> results=new ConcurrentQueue<AnalysisResult>();
        AnalysisResult result = new AnalysisResult();
        int condn;

        public mfrAnalyzer() : this(new lineVisualizer(),new OptimalCondition()) { }

        public mfrAnalyzer(IVisualizer ver,IController col)
        {
            visualizer = ver;
            controller = col;
        }

        public SignalChannel SignalChannel
        {
            get
            {
                return sigch;
            }

            set
            {
                sigch = value;
            }
        }

        public IVisualizer Visualizer
        {
            get
            {
                return visualizer;
            }

            set
            {
                visualizer = value;
            }
        }

        public IController Controller
        {
            get
            {
                return controller;
            }

            set
            {
                controller = value;
            }
        }

        public ConcurrentQueue< AnalysisResult > Results
        {
            get
            {
                return results;
            }
        }

        public void Analysis(DataSet dataset)
        {
            this.dataset = dataset;
           if(result.mfr.Count==0)
            {
               for(var i=0;i<dataset.CondN;i++)
                {
                    result.mfr[i] = new List<double>();
                }
                result.Elec = SignalChannel.elec;
                result.ExID = dataset.Ex.ID;
            }
            result.cond = dataset.Ex.Cond;
           if(result.mfr.Count==0)
            {
                return;
            }
            if(PrepareData())
            {
                var st = dataset.spike[SignalChannel.elec-1];
                for(var i=0;i<dataset.CondIndex.Count;i++)
                {
                    var c = dataset.CondIndex[i];
                    var t1 = dataset.CondState[i].FindCondStateTime("COND")+dataset.exstarttime;
                    var t2 = dataset.CondState[i].FindCondStateTime( "SUFICI") + dataset.exstarttime;
                    result.mfr[c].Add(st.MFR(t1, t2));
                }
                results.Enqueue(result.DeepCopy());
            }
            else
            {
                foreach(var ci in dataset.CondIndex)
                {
                    result.mfr[ci].Add(0.0);
                }
                results.Enqueue(result.DeepCopy());
            }
        }

        bool PrepareData()
        {
            if(sigch.signaltype== SIGNALTYPE.Spike||sigch.signaltype== SIGNALTYPE.All)
            {
                if(dataset.IsData(sigch.elec, sigch.signaltype))
                {
                    spike = dataset.spike[sigch.elec];
                    uid = dataset.uid[sigch.elec];
                    condindex = dataset.AccumCondIndex;
                    ex = dataset.Ex;
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
}
