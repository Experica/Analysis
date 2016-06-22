using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra.Double;
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

        public void Analysis(DataSet dataset)
        {
            this.dataset = dataset;
            if(PrepareData())
            {
                // do analysis
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
