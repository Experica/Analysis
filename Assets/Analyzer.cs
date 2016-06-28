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
                foreach(var c in dataset.Ex.cond.Values)
                {
                    condn = c.Count;
                    break;
                }
               for(var i=0;i<condn;i++)
                {
                    result.mfr[i] = new List<double>();
                }
            }

            if(PrepareData())
            {
                
            }
            else
            {
                foreach(var ci in dataset.CondIndex)
                {
                    result.mfr[ci].Add(0.0);
                }
            }
            results.Enqueue(result.DeepCopy());
        }

        bool PrepareData()
        {
            if(sigch.signaltype== SIGNALTYPE.Spike||sigch.signaltype== SIGNALTYPE.All)
            {
                if(dataset.IsData(sigch.elec, sigch.signaltype))
                {
                    spike = dataset.spike[sigch.elec];
                    uid = dataset.uid[sigch.elec];
                    condindex = dataset.CondIndex;
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
