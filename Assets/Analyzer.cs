using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra.Double;
using VLab;

namespace VLabAnalysis
{
    public interface IAnalyzer
    {
        SignalChannel SignalChannel { get; set; }
        void Analysis(DataSet dataset);
    }

    public class mfrAnalyzer : IAnalyzer
    {
        SignalChannel sigch;
        DataSet dataset;
        IVisualizer visualizer;
        List<double> spike;
        List<int> uid;
        List<int> condindex;
        Experiment ex;

        public mfrAnalyzer() : this(new lineVisualizer()) { }

        public mfrAnalyzer(IVisualizer ver)
        {
            visualizer = ver;
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

        public void Analysis(DataSet dataset)
        {
            this.dataset = dataset;
            if(PrepareData())
            {
                // do analysis
            }
            visualizer.Visualize();
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
                    ex = dataset.ex;
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
